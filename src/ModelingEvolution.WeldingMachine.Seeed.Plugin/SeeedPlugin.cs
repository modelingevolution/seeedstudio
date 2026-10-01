using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelingEvolution.WeldingMachine.Seeed.Plugin.Gpio;
using RocketWelder.SDK.Abstractions;
using RocketWelder.SDK.Automation;
using RocketWelder.SDK.Automation.Plugins;
using RocketWelder.SDK.Devices.Welding;

namespace ModelingEvolution.WeldingMachine.Seeed.Plugin;

/// <summary>Registers the <see cref="GenericSeeedWelder"/> device type with the rw2 host.</summary>
[RocketWelderPlugin("SeeedDo")]
public sealed class SeeedPlugin : IPlugin
{
    /// <summary>The DeviceType key persisted in PeripheralDeviceCreated.</summary>
    public const string DeviceTypeKey = "GenericSeeedWelder";

    /// <summary>The InterfaceType the host lists welders under.</summary>
    public const string InterfaceTypeKey = "IWeldingMachine";

    /// <inheritdoc/>
    public void Configure(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Devices.Register(new DeviceTypeInfo(
            DeviceType: DeviceTypeKey,
            InterfaceType: InterfaceTypeKey,
            DisplayName: "Generic welder (Seeed digital output)",
            InterfaceClrType: typeof(IWeldingMachine),
            PropertySchemas: Schemas,
            Factory: (config, id) => Build(config, id, context.Services),
            GetSignals: device => ((GenericSeeedWelder)device).FloatSignals,
            DetailView: typeof(GenericSeeedWelderView)));
    }

    /// <summary>
    /// One property, required and WITHOUT A DEFAULT: the operator reads the terminal off the cabinet
    /// wiring. A default would be a guess at which output starts the welder, and a wrong output is a
    /// wrong arc — the same rule as the FairinoDo index.
    /// </summary>
    public static ConfigPropertySchema[] Schemas { get; } =
    [
        new(SeeedDoOutputProperty.Name, "Digital output (1-4 = DO1-DO4)", "int",
            Required: true, Default: null, Group: "Connection"),
    ];

    /// <summary>Builds the device. Validates the configuration; performs NO I/O.</summary>
    public static GenericSeeedWelder Build(ConfigSet config, DeviceId id, IServiceProvider services,
        IDigitalOutputFactory? outputs = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(services);

        // ABSENT IS NOT A NUMBER: ConfigSet.Get returns default(int) for a missing property.
        if (!config.Any(e => string.Equals(e.Name, SeeedDoOutputProperty.Name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException(
                $"'{SeeedDoOutputProperty.Name}' is required: pick the terminal (1-4) the welder's start contact is wired to.",
                SeeedDoOutputProperty.Name);

        var number = config.Get<SeeedDoOutputProperty, int>();
        if (number is < SeeedDigitalOutput.Min or > SeeedDigitalOutput.Max)
            throw new ArgumentException(
                $"'{SeeedDoOutputProperty.Name}' = {number} is out of range: the terminal block has DO{SeeedDigitalOutput.Min}..DO{SeeedDigitalOutput.Max}.",
                SeeedDoOutputProperty.Name);

        var logger = (services.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance)
            .CreateLogger($"GenericSeeedWelder[{id}]");

        return new GenericSeeedWelder(id, SeeedDigitalOutput.ByNumber(number),
            outputs ?? new GpioV2Output.Factory(),
            services.GetRequiredService<IDeviceQuery>(),
            logger);
    }
}
