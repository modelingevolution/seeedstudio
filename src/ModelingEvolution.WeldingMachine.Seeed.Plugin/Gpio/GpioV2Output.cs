using System.Runtime.InteropServices;

namespace ModelingEvolution.WeldingMachine.Seeed.Plugin.Gpio;

/// <summary>
/// A GPIO line held through the v2 character device. The request's file descriptor IS the ownership:
/// while it is open no other process can request the line, and closing it releases the line.
///
/// <para><b>Releasing does NOT reset the pin.</b> Measured on a reServer J4012 (JetPack 6.2.1): a line
/// requested HIGH whose holder was killed with SIGKILL stayed HIGH after the kernel released it. That is
/// why <see cref="Dispose"/> drives LOW before closing, and why the welder drives LOW the moment it
/// attaches — the only moments software can act. A crash in between leaves the output as it was.</para>
/// </summary>
public sealed unsafe class GpioV2Output : IDigitalOutput
{
    private readonly object _gate = new();
    private int _fd;

    private GpioV2Output(int fd, string location)
    {
        _fd = fd;
        Location = location;
    }

    /// <inheritdoc/>
    public string Location { get; }

    /// <inheritdoc/>
    public void Write(bool high)
    {
        lock (_gate)
        {
            var values = new GpioV2LineValues { Bits = high ? 1UL : 0UL, Mask = 1UL };
            if (GpioV2Native.Ioctl(LiveFd(), GpioV2Native.SetValues, &values) < 0)
                throw Errno($"set {Location} = {(high ? 1 : 0)}");
        }
    }

    /// <inheritdoc/>
    public bool Read()
    {
        lock (_gate)
        {
            var values = new GpioV2LineValues { Mask = 1UL };
            if (GpioV2Native.Ioctl(LiveFd(), GpioV2Native.GetValues, &values) < 0)
                throw Errno($"read {Location}");
            return (values.Bits & 1UL) != 0;
        }
    }

    /// <summary>Drives LOW, then releases the line. Never throws.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_fd <= 0) return;
            var low = new GpioV2LineValues { Bits = 0UL, Mask = 1UL };
            GpioV2Native.Ioctl(_fd, GpioV2Native.SetValues, &low);
            GpioV2Native.Close(_fd);
            _fd = 0;
        }
    }

    private int LiveFd() => _fd > 0 ? _fd : throw new ObjectDisposedException(Location);

    private GpioException Errno(string what)
    {
        var errno = Marshal.GetLastPInvokeError();
        return new GpioException($"GPIO {what} failed: errno {errno} ({Marshal.GetPInvokeErrorMessage(errno)}).", errno);
    }

    /// <summary>The production factory: scans <c>/dev/gpiochip*</c> for the named line.</summary>
    public sealed class Factory : IDigitalOutputFactory
    {
        private readonly string _devDirectory;

        /// <param name="devDirectory">Where to look for gpiochip nodes; <c>/dev</c> on a device.</param>
        public Factory(string devDirectory = "/dev") => _devDirectory = devDirectory;

        /// <inheritdoc/>
        public IDigitalOutput OpenLowOutput(string lineName, string consumer)
        {
            var chips = Directory.Exists(_devDirectory)
                ? Directory.GetFiles(_devDirectory, "gpiochip*").Order(StringComparer.Ordinal).ToArray()
                : [];
            if (chips.Length == 0)
                throw new GpioException(
                    $"No GPIO character devices under {_devDirectory}. This welder needs a Seeed reComputer/reServer " +
                    "Industrial, and a container must see /dev/gpiochip* (privileged, or devices: /dev/gpiochip0).");

            foreach (var chip in chips)
            {
                var chipFd = GpioV2Native.Open(chip, GpioV2Native.O_RDWR | GpioV2Native.O_CLOEXEC);
                if (chipFd < 0) continue;
                try
                {
                    var info = default(GpioChipInfo);
                    if (GpioV2Native.Ioctl(chipFd, GpioV2Native.GetChipInfo, &info) < 0) continue;

                    for (uint offset = 0; offset < info.Lines; offset++)
                    {
                        var line = default(GpioV2LineInfo);
                        line.Offset = offset;
                        if (GpioV2Native.Ioctl(chipFd, GpioV2Native.GetLineInfo, &line) < 0) continue;
                        if (GpioV2Native.Text(line.Name, 32) != lineName) continue;

                        var location = $"{Path.GetFileName(chip)} line {offset} ({lineName})";
                        if ((line.Flags & GpioV2Native.FlagUsed) != 0)
                            throw new GpioException(
                                $"GPIO {location} is already held by '{GpioV2Native.Text(line.Consumer, 32)}'. " +
                                "Only one device may drive a digital output.");

                        return Request(chipFd, offset, consumer, location);
                    }
                }
                finally
                {
                    GpioV2Native.Close(chipFd);
                }
            }

            throw new GpioException(
                $"No GPIO line named '{lineName}' on {string.Join(", ", chips.Select(Path.GetFileName))}. " +
                "This is not a Seeed reComputer/reServer Industrial on JetPack 6, or the pin is named differently.");
        }

        private static GpioV2Output Request(int chipFd, uint offset, string consumer, string location)
        {
            var req = default(GpioV2LineRequest);
            req.Offsets[0] = offset;
            req.NumLines = 1;
            var name = System.Text.Encoding.UTF8.GetBytes(consumer);
            for (var i = 0; i < Math.Min(name.Length, 31); i++) req.Consumer[i] = name[i];
            req.Config.Flags = GpioV2Native.FlagOutput;
            req.Config.SetInitialOutput(high: false);

            if (GpioV2Native.Ioctl(chipFd, GpioV2Native.GetLine, &req) < 0)
            {
                var errno = Marshal.GetLastPInvokeError();
                throw new GpioException(
                    $"Requesting {location} as output failed: errno {errno} ({Marshal.GetPInvokeErrorMessage(errno)}).", errno);
            }
            return new GpioV2Output(req.Fd, location);
        }
    }
}
