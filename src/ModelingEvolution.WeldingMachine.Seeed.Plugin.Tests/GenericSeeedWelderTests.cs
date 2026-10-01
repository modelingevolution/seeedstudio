using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelingEvolution.WeldingMachine.Seeed.Plugin.Gpio;
using NSubstitute;
using RocketWelder.SDK.Abstractions;
using RocketWelder.SDK.Automation;
using Xunit;

namespace ModelingEvolution.WeldingMachine.Seeed.Plugin.Tests;

public class GenericSeeedWelderTests
{
    private readonly FakeFactory _factory = new();

    private GenericSeeedWelder Build(int terminal) =>
        SeeedPlugin.Build(new ConfigSet(new SeeedDoOutputProperty(terminal)),
            DeviceId.New(SeeedPlugin.DeviceTypeKey), Services(), _factory);

    private static IServiceProvider Services() =>
        new ServiceCollection().AddSingleton(Substitute.For<IDeviceQuery>()).BuildServiceProvider();

    [Theory]
    [InlineData(1, "PI.00")]
    [InlineData(2, "PI.01")]
    [InlineData(3, "PI.02")]
    [InlineData(4, "PH.07")]
    public void Each_terminal_opens_its_own_Jetson_line_as_a_LOW_output(int terminal, string line)
    {
        var welder = Build(terminal);

        welder.IsConnected.Should().BeTrue();

        _factory.Requests.Should().ContainSingle().Which.Should().Be((line, $"rw2 DO{terminal}"));
        welder.LastKnownState.Should().Be(SeeedDoConnectionState.Connected);
        welder.LastReadBack.Should().BeFalse("the line is requested LOW at attach");
    }

    [Fact]
    public void Construction_does_no_IO()
    {
        _ = Build(1);
        _factory.Requests.Should().BeEmpty();
    }

    [Fact]
    public void The_line_is_opened_once_however_often_the_host_polls()
    {
        var welder = Build(1);
        for (var i = 0; i < 5; i++) welder.IsConnected.Should().BeTrue();
        _factory.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task ArcOn_drives_the_output_HIGH_and_ArcOff_drives_it_LOW()
    {
        var welder = Build(1);

        await welder.ArcOn();
        _factory.Opened[0].Value.Should().BeTrue();
        welder.WeldingStart.Should().BeTrue();
        welder.ArcStableSignal.Value.Should().BeTrue();
        welder.LastReadBack.Should().BeTrue();

        await welder.ArcOff();
        _factory.Opened[0].Writes.Should().Equal(true, false);
        welder.WeldingStart.Should().BeFalse();
        welder.LastReadBack.Should().BeFalse();
    }

    [Fact]
    public async Task ArcOn_throws_when_the_read_back_disagrees()
    {
        _factory.Configure = o => o.StuckAt = false;
        var welder = Build(1);

        var act = async () => await welder.ArcOn();

        await act.Should().ThrowAsync<GpioException>().WithMessage("*written 1 but reads back 0*");
        welder.WeldingStart.Should().BeFalse("an unconfirmed arc-on must not be reported as on");
    }

    [Fact]
    public async Task A_failed_write_releases_the_line_and_the_next_poll_reopens_it()
    {
        var welder = Build(1);
        welder.IsConnected.Should().BeTrue();
        _factory.Opened[0].FailNextWrites = 1;

        var act = async () => await welder.ArcOn();

        await act.Should().ThrowAsync<GpioException>();
        welder.LastKnownState.Should().Be(SeeedDoConnectionState.Error);
        _factory.Opened[0].Disposed.Should().BeTrue();

        welder.IsConnected.Should().BeTrue();
        _factory.Opened.Should().HaveCount(2);
    }

    [Fact]
    public async Task ArcOff_retries_once_and_never_throws()
    {
        var welder = Build(1);
        await welder.ArcOn();
        _factory.Opened[0].FailNextWrites = 1;

        var act = async () => await welder.ArcOff();

        await act.Should().NotThrowAsync();
        welder.WeldingStart.Should().BeFalse();
    }

    [Fact]
    public async Task ArcOff_does_not_throw_when_the_line_cannot_be_opened()
    {
        _factory.FailWith = new GpioException("no gpiochip");
        var welder = Build(1);

        var act = async () => await welder.ArcOff();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void An_unopenable_line_throws_from_IsConnected_with_the_reason()
    {
        _factory.FailWith = new GpioException("GPIO gpiochip0 line 51 (PI.00) is already held by 'gpioset'.");
        var welder = Build(1);

        var act = () => welder.IsConnected;

        act.Should().Throw<GpioException>().WithMessage("*already held*");
        welder.LastKnownState.Should().Be(SeeedDoConnectionState.Error);
        welder.LastKnownProblem.Should().Contain("already held");
    }

    [Fact]
    public async Task Process_exit_drives_a_HIGH_output_LOW_even_when_nobody_disposes_the_device()
    {
        var welder = Build(1);
        await welder.ArcOn();

        welder.OnProcessExit(null, EventArgs.Empty);

        _factory.Opened[0].Value.Should().BeFalse();
        _factory.Opened[0].Disposed.Should().BeFalse("exit only drives LOW; the kernel releases the line");
    }

    [Fact]
    public async Task Dispose_drives_the_output_LOW_and_releases_it()
    {
        var welder = Build(1);
        await welder.ArcOn();

        welder.Dispose();

        _factory.Opened[0].Disposed.Should().BeTrue();
        _factory.Opened[0].Value.Should().BeFalse();
        welder.LastKnownState.Should().Be(SeeedDoConnectionState.Disconnected);
    }
}
