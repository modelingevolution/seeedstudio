using System.Runtime.InteropServices;

namespace ModelingEvolution.WeldingMachine.Seeed.Plugin.Gpio;

/// <summary>
/// The Linux GPIO character-device uAPI v2 (<c>include/uapi/linux/gpio.h</c>), bound directly through libc
/// <c>open</c>/<c>ioctl</c>/<c>close</c>. No libgpiod: the rw2 image does not ship it, and the five
/// structures below are all this plugin needs. Layouts are asserted byte-for-byte by NativeLayoutTests;
/// the same calls were proven on a reServer J4012 (JetPack 6.2.1) with a Python prototype first.
/// </summary>
internal static unsafe class GpioV2Native
{
    public const int O_RDWR = 0x2;
    public const int O_CLOEXEC = 0x80000;

    public const ulong FlagUsed = 1UL << 0;
    public const ulong FlagOutput = 1UL << 3;

    public const uint AttrIdOutputValues = 2;

    private const uint IocWrite = 1, IocRead = 2;
    private static uint Ioc(uint dir, uint nr, int size) => (dir << 30) | ((uint)size << 16) | (0xB4u << 8) | nr;

    public static readonly uint GetChipInfo = Ioc(IocRead, 0x01, sizeof(GpioChipInfo));
    public static readonly uint GetLineInfo = Ioc(IocRead | IocWrite, 0x05, sizeof(GpioV2LineInfo));
    public static readonly uint GetLine = Ioc(IocRead | IocWrite, 0x07, sizeof(GpioV2LineRequest));
    public static readonly uint GetValues = Ioc(IocRead | IocWrite, 0x0E, sizeof(GpioV2LineValues));
    public static readonly uint SetValues = Ioc(IocRead | IocWrite, 0x0F, sizeof(GpioV2LineValues));

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    public static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    public static extern int Close(int fd);

    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    public static extern int Ioctl(int fd, nuint request, void* arg);

    public static string Text(byte* bytes, int max)
    {
        var len = 0;
        while (len < max && bytes[len] != 0) len++;
        return System.Text.Encoding.UTF8.GetString(bytes, len);
    }
}

/// <summary><c>struct gpiochip_info</c> — 68 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GpioChipInfo
{
    public fixed byte Name[32];
    public fixed byte Label[32];
    public uint Lines;
}

/// <summary><c>struct gpio_v2_line_info</c> — 256 bytes. Attributes are not read, only sized.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GpioV2LineInfo
{
    public fixed byte Name[32];
    public fixed byte Consumer[32];
    public uint Offset;
    public uint NumAttrs;
    public ulong Flags;
    public fixed ulong Attrs[20];      // 10 × struct gpio_v2_line_attribute (16 bytes)
    public fixed uint Padding[4];
}

/// <summary><c>struct gpio_v2_line_config</c> — 272 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GpioV2LineConfig
{
    public ulong Flags;
    public uint NumAttrs;
    public fixed uint Padding[5];
    // 10 × struct gpio_v2_line_config_attribute (24 bytes each): { u32 id; u32 pad; u64 value; u64 mask; }
    public fixed ulong Attrs[30];

    /// <summary>Sets attribute 0 to "output values" for line 0 of the request.</summary>
    public void SetInitialOutput(bool high)
    {
        NumAttrs = 1;
        Attrs[0] = GpioV2Native.AttrIdOutputValues;   // id (low 32 bits) + padding (high 32 bits)
        Attrs[1] = high ? 1UL : 0UL;                    // values bitmap
        Attrs[2] = 1UL;                                 // mask: line 0
    }
}

/// <summary><c>struct gpio_v2_line_request</c> — 592 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GpioV2LineRequest
{
    public fixed uint Offsets[64];
    public fixed byte Consumer[32];
    public GpioV2LineConfig Config;
    public uint NumLines;
    public uint EventBufferSize;
    public fixed uint Padding[5];
    public int Fd;
}

/// <summary><c>struct gpio_v2_line_values</c> — 16 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GpioV2LineValues
{
    public ulong Bits;
    public ulong Mask;
}
