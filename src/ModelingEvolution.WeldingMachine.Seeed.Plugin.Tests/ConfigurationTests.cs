using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RocketWelder.SDK.Abstractions;
using RocketWelder.SDK.Automation;
using Xunit;

namespace ModelingEvolution.WeldingMachine.Seeed.Plugin.Tests;

public class ConfigurationTests
{
    private static IServiceProvider Services() =>
        new ServiceCollection().AddSingleton(Substitute.For<IDeviceQuery>()).BuildServiceProvider();

    private static DeviceId Id() => DeviceId.New(SeeedPlugin.DeviceTypeKey);

    [Fact]
    public void The_terminal_is_required_and_has_no_default()
    {
        var schema = SeeedPlugin.Schemas.Should().ContainSingle().Subject;
        schema.Name.Should().Be("SeeedDoOutput");
        schema.ValueType.Should().Be("int");
        schema.Required.Should().BeTrue();
        schema.Default.Should().BeNull("a default would be a guess at which output starts the welder");
    }

    [Fact]
    public void A_missing_terminal_is_rejected_rather_than_read_as_zero()
    {
        var act = () => SeeedPlugin.Build(new ConfigSet(), Id(), Services(), new FakeFactory());
        act.Should().Throw<ArgumentException>().WithMessage("*'SeeedDoOutput' is required*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-1)]
    public void A_terminal_outside_DO1_to_DO4_is_rejected(int terminal)
    {
        var act = () => SeeedPlugin.Build(new ConfigSet(new SeeedDoOutputProperty(terminal)), Id(), Services(), new FakeFactory());
        act.Should().Throw<ArgumentException>().WithMessage("*DO1..DO4*");
    }

    [Fact]
    public void The_four_outputs_are_the_Seeed_wiki_pins()
    {
        SeeedDigitalOutput.All.Select(o => (o.Terminal, o.LineName)).Should().Equal(
            ("DO1", "PI.00"), ("DO2", "PI.01"), ("DO3", "PI.02"), ("DO4", "PH.07"));
    }
}
