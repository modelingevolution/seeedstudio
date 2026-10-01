using System.Text.Json.Serialization;
using RocketWelder.SDK.Automation;

namespace ModelingEvolution.WeldingMachine.Seeed.Plugin;

// Every ConfigProperty type MUST live in this assembly: rw2 calls
// ConfigPropertyJsonConverter.ScanAssembly(plugin.GetType().Assembly) and scans nothing else.
// The name carries a "SeeedDo" qualifier because ScanAssembly registers names into ONE process-wide
// dictionary, last write wins — a bare "Output" could collide with another plugin.

/// <summary>Which terminal drives the welder's start contact: 1..4 for DO1..DO4.</summary>
[JsonConverter(typeof(ConfigPropertyJsonConverter))]
public record SeeedDoOutputProperty(int Value)
    : ConfigProperty<int, SeeedDoOutputProperty>(Value), IConfigProperty<SeeedDoOutputProperty>
{
    /// <summary>The persisted property name.</summary>
    public static string Name => "SeeedDoOutput";
}
