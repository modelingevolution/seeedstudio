namespace ModelingEvolution.WeldingMachine.Seeed.Plugin;

/// <summary>
/// One isolated digital output of the Seeed reComputer Industrial J40/J30 and reServer Industrial J4012
/// terminal block. Both carriers wire DO1–DO4 to the same Jetson pins (Seeed wiki, "Hardware and
/// Interfaces Usage" for each product), so one table serves both.
///
/// <para>The line is found by its BGA NAME, not by a number: GPIO numbers changed between JetPack 5
/// (sysfs 399…) and JetPack 6 (gpiochip0 offset 51…), the names did not.</para>
///
/// <para>Electrically each output is an optocoupled sink rated 40 V / 40 mA (load between +V and DOx,
/// return on GND_DO). Writing 1 turns the output ON (the load is energised).</para>
/// </summary>
public sealed record SeeedDigitalOutput(int Number, string LineName)
{
    /// <summary>The label printed on the terminal block, e.g. "DO1".</summary>
    public string Terminal => $"DO{Number}";

    /// <summary>The four outputs, DO1..DO4.</summary>
    public static IReadOnlyList<SeeedDigitalOutput> All { get; } =
    [
        new(1, "PI.00"),
        new(2, "PI.01"),
        new(3, "PI.02"),
        new(4, "PH.07"),
    ];

    /// <summary>Lowest terminal number.</summary>
    public const int Min = 1;

    /// <summary>Highest terminal number.</summary>
    public const int Max = 4;

    /// <summary>The output for terminal <paramref name="number"/> (1..4).</summary>
    public static SeeedDigitalOutput ByNumber(int number) =>
        All.FirstOrDefault(o => o.Number == number)
        ?? throw new ArgumentOutOfRangeException(nameof(number), number, $"Seeed digital outputs are DO{Min}..DO{Max}.");
}
