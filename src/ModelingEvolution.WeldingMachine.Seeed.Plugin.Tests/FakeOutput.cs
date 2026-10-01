using ModelingEvolution.WeldingMachine.Seeed.Plugin.Gpio;

namespace ModelingEvolution.WeldingMachine.Seeed.Plugin.Tests;

/// <summary>A GPIO line in memory. <see cref="StuckAt"/> makes the read-back disagree with the write.</summary>
internal sealed class FakeOutput : IDigitalOutput
{
    public List<bool> Writes { get; } = [];
    public bool Value { get; private set; }
    public bool? StuckAt { get; set; }
    public int FailNextWrites { get; set; }
    public bool Disposed { get; private set; }
    public string Location => "fakechip line 51 (PI.00)";

    public void Write(bool high)
    {
        if (Disposed) throw new ObjectDisposedException(Location);
        if (FailNextWrites > 0) { FailNextWrites--; throw new GpioException("injected write failure", 5); }
        Writes.Add(high);
        Value = high;
    }

    public bool Read() => StuckAt ?? Value;

    public void Dispose()
    {
        if (!Disposed) Writes.Add(false);   // the real line drives LOW before release
        Value = false;
        Disposed = true;
    }
}

internal sealed class FakeFactory : IDigitalOutputFactory
{
    public List<FakeOutput> Opened { get; } = [];
    public List<(string Line, string Consumer)> Requests { get; } = [];
    public Exception? FailWith { get; set; }
    public Action<FakeOutput>? Configure { get; set; }

    public IDigitalOutput OpenLowOutput(string lineName, string consumer)
    {
        Requests.Add((lineName, consumer));
        if (FailWith is not null) throw FailWith;
        var output = new FakeOutput();
        Configure?.Invoke(output);
        Opened.Add(output);
        return output;
    }
}
