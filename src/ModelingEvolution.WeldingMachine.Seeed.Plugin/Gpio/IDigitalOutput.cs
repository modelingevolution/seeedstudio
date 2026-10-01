namespace ModelingEvolution.WeldingMachine.Seeed.Plugin.Gpio;

/// <summary>One digital output line, held for the life of the instance.</summary>
public interface IDigitalOutput : IDisposable
{
    /// <summary>Where the line lives, for logs: e.g. "gpiochip0 line 51 (PI.00)".</summary>
    string Location { get; }

    /// <summary>Drives the line. Throws <see cref="GpioException"/> when the kernel refuses.</summary>
    void Write(bool high);

    /// <summary>
    /// Reads the line's value back from the kernel. For a line requested as output this is the value the
    /// GPIO controller is DRIVING (its output register) — the SoC pin, not the isolated terminal behind
    /// the optocoupler, and not the relay.
    /// </summary>
    bool Read();
}

/// <summary>Opens <see cref="IDigitalOutput"/>s. A seam so the welder is testable without hardware.</summary>
public interface IDigitalOutputFactory
{
    /// <summary>
    /// Finds the GPIO line named <paramref name="lineName"/> on any <c>/dev/gpiochip*</c>, requests it as an
    /// OUTPUT with initial value LOW, and holds it. Throws <see cref="GpioException"/> if the line does not
    /// exist or is held by another consumer.
    /// </summary>
    IDigitalOutput OpenLowOutput(string lineName, string consumer);
}

/// <summary>A GPIO operation the kernel refused, with the errno it gave.</summary>
public sealed class GpioException(string message, int errno = 0) : Exception(message)
{
    /// <summary>The errno from the failing call; 0 when the failure was not a syscall.</summary>
    public int Errno { get; } = errno;
}
