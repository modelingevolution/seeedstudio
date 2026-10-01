using System.Runtime.CompilerServices;
using FluentAssertions;
using ModelingEvolution.WeldingMachine.Seeed.Plugin.Gpio;
using Xunit;

namespace ModelingEvolution.WeldingMachine.Seeed.Plugin.Tests;

/// <summary>
/// The kernel reads these structures by size and offset. A wrong size changes the ioctl number (the size
/// is encoded in it), so the call fails with ENOTTY instead of corrupting memory — but only on a device.
/// These pin the sizes and the request numbers to linux/gpio.h here.
/// </summary>
public class NativeLayoutTests
{
    [Fact]
    public void Structure_sizes_match_linux_gpio_h()
    {
        Unsafe.SizeOf<GpioChipInfo>().Should().Be(68);
        Unsafe.SizeOf<GpioV2LineInfo>().Should().Be(256);
        Unsafe.SizeOf<GpioV2LineConfig>().Should().Be(272);
        Unsafe.SizeOf<GpioV2LineRequest>().Should().Be(592);
        Unsafe.SizeOf<GpioV2LineValues>().Should().Be(16);
    }

    [Fact]
    public void Ioctl_numbers_match_linux_gpio_h()
    {
        GpioV2Native.GetChipInfo.Should().Be(0x8044B401u);
        GpioV2Native.GetLineInfo.Should().Be(0xC100B405u);
        GpioV2Native.GetLine.Should().Be(0xC250B407u);
        GpioV2Native.GetValues.Should().Be(0xC010B40Eu);
        GpioV2Native.SetValues.Should().Be(0xC010B40Fu);
    }

    [Fact]
    public void The_initial_output_attribute_sits_where_the_kernel_reads_it()
    {
        var config = default(GpioV2LineConfig);
        config.SetInitialOutput(high: true);

        config.NumAttrs.Should().Be(1);
        unsafe
        {
            config.Attrs[0].Should().Be(2UL, "attr id GPIO_V2_LINE_ATTR_ID_OUTPUT_VALUES");
            config.Attrs[1].Should().Be(1UL, "values bitmap: line 0 high");
            config.Attrs[2].Should().Be(1UL, "mask: line 0");
        }
    }

    [Fact]
    public void A_machine_without_gpiochips_reports_why()
    {
        var empty = Directory.CreateTempSubdirectory().FullName;
        var act = () => new GpioV2Output.Factory(empty).OpenLowOutput("PI.00", "test");
        act.Should().Throw<GpioException>().WithMessage("*No GPIO character devices*");
    }
}
