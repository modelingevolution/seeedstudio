using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using ModelingEvolution.Drawing;
using ModelingEvolution.Signals;
using ModelingEvolution.WeldingMachine.Seeed.Plugin.Gpio;
using RocketWelder.SDK.Abstractions;
using RocketWelder.SDK.Automation;
using RocketWelder.SDK.Devices.Welding;

namespace ModelingEvolution.WeldingMachine.Seeed.Plugin;

/// <summary>What the detail view shows. Cached by <see cref="GenericSeeedWelder.IsConnected"/>.</summary>
public enum SeeedDoConnectionState
{
    /// <summary>Not attached yet, or released.</summary>
    Disconnected,
    /// <summary>The line is held as an output.</summary>
    Connected,
    /// <summary>The line could not be opened; see the problem text.</summary>
    Error,
}

/// <summary>
/// A welding machine whose only control input is a start contact (e.g. a plasma cutter), driven from one
/// isolated digital output of the station computer itself — a Seeed reComputer/reServer Industrial.
///
/// <para><b>Arc on = output ON.</b> No robot, no network and no controller sit between rw2 and the
/// output: the write is a local ioctl, followed by a read-back of the GPIO controller's output
/// register. That read-back proves the SoC pin is driven; it does NOT see the optocoupler, the
/// terminal, the relay or an arc.</para>
///
/// <para><b>Safe state.</b> The line is requested LOW at attach and driven LOW on ArcOff and on
/// Dispose. Nothing resets it if the process dies while it is HIGH (measured: the kernel releases the
/// line but leaves the pin as it was). A hardware interlock — the start contact wired through the
/// E-stop chain — is what covers a crash.</para>
/// </summary>
public sealed class GenericSeeedWelder : IWeldingMachine, IDisposable
{
    private static readonly TimeSpan ArcOffRetryDelay = TimeSpan.FromMilliseconds(20);

    private readonly DeviceId _id;
    private readonly IDigitalOutputFactory _outputs;
    private readonly IDeviceQuery _query;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _callGate = new(1, 1);
    private readonly object _attachGate = new();

    private readonly WritableSignal<Amps<float>> _current;
    private readonly WritableSignal<Amps<float>> _targetCurrent;
    private readonly WritableSignal<Speed<float>> _wireFeed;
    private readonly WritableSignal<Volts<float>> _voltage;
    private readonly WritableSignal<WeldingMode> _mode;
    private readonly WritableSignal<bool> _weldingStart;
    private readonly WritableSignal<bool> _arcStable;
    private readonly WritableSignal<bool> _gas;
    private readonly WritableSignal<int> _jobNumber;

    private IDigitalOutput? _line;
    private volatile SeeedDoConnectionState _state = SeeedDoConnectionState.Disconnected;
    private volatile string? _problem;
    private volatile int _lastReadBack = -1;
    private bool _disposed;

    internal GenericSeeedWelder(DeviceId id, SeeedDigitalOutput output, IDigitalOutputFactory outputs,
        IDeviceQuery query, ILogger logger)
    {
        _id = id;
        Output = output ?? throw new ArgumentNullException(nameof(output));
        _outputs = outputs ?? throw new ArgumentNullException(nameof(outputs));
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _current = Sig<Amps<float>>("welding-current");
        _targetCurrent = Sig<Amps<float>>("target-current");
        _wireFeed = Sig<Speed<float>>("wire-feed-speed");
        _voltage = Sig<Volts<float>>("welding-voltage");
        _mode = Sig<WeldingMode>("welding-mode");
        _weldingStart = Sig<bool>("welding-start");
        _arcStable = Sig<bool>("arc-stable");
        _gas = Sig<bool>("gas");
        _jobNumber = Sig<int>("job-number");
    }

    private WritableSignal<T> Sig<T>(string channel) =>
        new(new SignalMetadata(channel, new Uri($"signal://seeed-do/{_id.Value}/{channel}"), null, null));

    // ── identity / status ────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public DeviceId Id => _id;

    /// <summary>The configured terminal.</summary>
    public SeeedDigitalOutput Output { get; }

    /// <summary>The operator-facing name, from the device registry.</summary>
    public string Name => _query.GetById(_id)?.Name ?? _id.ToString();

    /// <summary>Cached state for the detail view. No I/O.</summary>
    public SeeedDoConnectionState LastKnownState => _state;

    /// <summary>Why the device is not connected, if it is not.</summary>
    public string? LastKnownProblem => _problem;

    /// <summary>Where the held line lives, once attached.</summary>
    public string? Location => Volatile.Read(ref _line)?.Location;

    /// <summary>The last value read back from the output register; null before the first write.</summary>
    public bool? LastReadBack => _lastReadBack switch { 1 => true, 0 => false, _ => null };

    /// <summary>
    /// Attaches on first call (opens the line LOW), then reports whether it is held. Polled by the host
    /// every few seconds. THROWS when the line cannot be opened — that throw is how the reason reaches
    /// the /devices row.
    /// </summary>
    public bool IsConnected => EnsureAttached() is not null;

    private IDigitalOutput? EnsureAttached()
    {
        if (_disposed) return null;
        var line = Volatile.Read(ref _line);
        if (line is not null) return line;

        lock (_attachGate)
        {
            if (_line is not null) return _line;
            try
            {
                var consumer = $"rw2 {Output.Terminal}";
                _line = _outputs.OpenLowOutput(Output.LineName, consumer);
                // SIGTERM (docker stop, app restart) can end the process WITHOUT disposing devices, and the
                // kernel leaves a released line as it was — measured on the bench: held HIGH, SIGTERM,
                // still HIGH, and ProcessExit did NOT run. So the termination signals are hooked directly;
                // the handler does not cancel them, so the host's own graceful shutdown still proceeds.
                // ProcessExit stays as the hook for a normal exit. SIGKILL and a crash remain hardware's job.
                if (Interlocked.Exchange(ref _exitHooked, 1) == 0)
                {
                    AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
                    _signals =
                    [
                        .. new[] { PosixSignal.SIGTERM, PosixSignal.SIGINT, PosixSignal.SIGQUIT, PosixSignal.SIGHUP }
                            .Select(s => PosixSignalRegistration.Create(s, _ => OnProcessExit(null, EventArgs.Empty))),
                    ];
                }
                _state = SeeedDoConnectionState.Connected;
                _problem = null;
                _lastReadBack = 0;
                _logger.LogInformation(LogEvents.Attached,
                    "Welding machine '{Device}': holding {Terminal} on {Location}, driven LOW (safe state on attach).",
                    Name, Output.Terminal, _line.Location);
                return _line;
            }
            catch (Exception ex)
            {
                if (_problem != ex.Message)
                    _logger.LogError(LogEvents.AttachFailed, ex,
                        "Welding machine '{Device}': cannot open {Terminal} ({Line}): {Reason}",
                        Name, Output.Terminal, Output.LineName, ex.Message);
                _state = SeeedDoConnectionState.Error;
                _problem = ex.Message;
                throw;
            }
        }
    }

    // ── the arc path ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Drives the output ON and confirms it through the read-back. Throws on any failure.</summary>
    public async ValueTask ArcOn()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _callGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var line = EnsureAttached() ?? throw new ObjectDisposedException(nameof(GenericSeeedWelder));
            Write(line, on: true);
            _weldingStart.Set(true);
            _arcStable.Set(true);
        }
        finally
        {
            _callGate.Release();
        }
    }

    /// <summary>
    /// Drives the output OFF, retrying once. Never throws: it runs from finally blocks and from STOP.
    /// A failure is logged as Critical because the start contact may still be closed.
    /// </summary>
    public async ValueTask ArcOff()
    {
        if (_disposed) return;
        await _callGate.WaitAsync().ConfigureAwait(false);
        try
        {
            IDigitalOutput? line;
            try { line = EnsureAttached(); }
            catch (Exception ex)
            {
                _logger.LogCritical(LogEvents.ArcOffFailed, ex,
                    "Welding machine '{Device}': ArcOff cannot reach {Terminal} — the line is not held. Its state " +
                    "is whatever it was last driven to.", Name, Output.Terminal);
                return;
            }
            if (line is null) return;

            for (var attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    Write(line, on: false);
                    return;
                }
                catch (Exception ex) when (attempt == 1)
                {
                    _logger.LogError(LogEvents.ArcOffFailed, ex,
                        "Welding machine '{Device}': ArcOff on {Terminal} failed; retrying once.", Name, Output.Terminal);
                    await Task.Delay(ArcOffRetryDelay).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogCritical(LogEvents.ArcOffFailed, ex,
                        "Welding machine '{Device}': ArcOff on {Terminal} failed twice. THE START CONTACT MAY STILL " +
                        "BE CLOSED.", Name, Output.Terminal);
                }
            }
        }
        finally
        {
            _weldingStart.Set(false);
            _arcStable.Set(false);
            _callGate.Release();
        }
    }

    private void Write(IDigitalOutput line, bool on)
    {
        var started = Stopwatch.GetTimestamp();
        bool readBack;
        try
        {
            line.Write(on);
            readBack = line.Read();
        }
        catch (Exception ex)
        {
            _logger.LogError(LogEvents.WriteFailed, ex,
                "Welding machine '{Device}': writing {Terminal} ({Location}) = {Status} failed.",
                Name, Output.Terminal, line.Location, on ? 1 : 0);
            Release(line, ex.Message);
            throw;
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        _lastReadBack = readBack ? 1 : 0;
        _logger.LogInformation(LogEvents.Write,
            "Welding machine '{Device}': wrote {Terminal} ({Location}) = {Status}, read-back {ReadBack} ({ElapsedMs:F2} ms).",
            Name, Output.Terminal, line.Location, on ? 1 : 0, readBack ? 1 : 0, elapsed.TotalMilliseconds);

        if (readBack != on)
        {
            var message = $"{Output.Terminal} was written {(on ? 1 : 0)} but reads back {(readBack ? 1 : 0)}.";
            _logger.LogCritical(LogEvents.ReadBackMismatch,
                "Welding machine '{Device}': {Message} The output is NOT in the commanded state.", Name, message);
            throw new GpioException(message);
        }
    }

    /// <summary>Drops a line that failed, so the next poll re-opens it from scratch.</summary>
    private void Release(IDigitalOutput line, string reason)
    {
        lock (_attachGate)
        {
            if (!ReferenceEquals(_line, line)) return;
            _line = null;
            _state = SeeedDoConnectionState.Error;
            _problem = reason;
        }
        try { line.Dispose(); } catch { /* the line is already broken */ }
    }

    // ── signals and the parameters a start contact does not have ─────────────────────────────────

    /// <inheritdoc/>
    public ISignal<Amps<float>> CurrentSignal => _current;
    /// <inheritdoc/>
    public ISignal<Amps<float>> TargetCurrentSignal => _targetCurrent;
    /// <inheritdoc/>
    public ISignal<Speed<float>> WireFeedSpeedSignal => _wireFeed;
    /// <inheritdoc/>
    public ISignal<Volts<float>> WeldingVoltageSignal => _voltage;
    /// <inheritdoc/>
    public WritableSignal<WeldingMode> ModeSignal => _mode;
    /// <inheritdoc/>
    public WritableSignal<bool> WeldingStartSignal => _weldingStart;
    /// <summary>Set with the output: the write was confirmed, NOT that an arc exists.</summary>
    public WritableSignal<bool> ArcStableSignal => _arcStable;
    /// <inheritdoc/>
    public WritableSignal<bool> GasSignal => _gas;
    /// <inheritdoc/>
    public WritableSignal<int> JobNumberSignal => _jobNumber;

    /// <summary>The signals the host charts.</summary>
    public IEnumerable<ISignal<float>> FloatSignals =>
    [
        _weldingStart.Select<bool, float>(b => b ? 1f : 0f),
        _arcStable.Select<bool, float>(b => b ? 1f : 0f),
    ];

    /// <inheritdoc/>
    public WeldingMode Mode
    {
        get => _mode.HasValue ? _mode.Value : WeldingMode.Unknown;
        set { Ignored(nameof(Mode), value); _mode.Set(value); }
    }

    /// <inheritdoc/>
    public int JobNumber
    {
        get => _jobNumber.HasValue ? _jobNumber.Value : 0;
        set { Ignored(nameof(JobNumber), value); _jobNumber.Set(value); }
    }

    /// <summary>A start contact has no gas valve.</summary>
    public bool Gas { get => false; set { } }

    /// <inheritdoc/>
    public ValueTask GasOn() => ValueTask.CompletedTask;

    /// <inheritdoc/>
    public ValueTask GasOff() => ValueTask.CompletedTask;

    /// <inheritdoc/>
    public bool WeldingStart
    {
        get => _weldingStart is { HasValue: true, Value: true };
        set
        {
            if (value) ArcOn().AsTask().GetAwaiter().GetResult();
            else ArcOff().AsTask().GetAwaiter().GetResult();
        }
    }

    private int _ignoredWarned;

    private void Ignored(string what, object? value)
    {
        if (Interlocked.Exchange(ref _ignoredWarned, 1) == 0)
            _logger.LogWarning(LogEvents.ParameterIgnored,
                "Welding machine '{Device}' is a start contact with no parameter surface — {What} = {Value} was ignored.",
                Name, what, value);
    }

    private int _exitHooked;
    private PosixSignalRegistration[] _signals = [];

    /// <summary>
    /// Last-chance safe state on process exit. Writes the line directly — not through the call gate,
    /// which an in-flight ArcOn may hold — and never throws.
    /// </summary>
    internal void OnProcessExit(object? sender, EventArgs e)
    {
        var line = Volatile.Read(ref _line);
        if (line is null) return;
        try
        {
            line.Write(false);
            _logger.LogWarning(LogEvents.Released,
                "Welding machine '{Device}': process exiting — {Terminal} driven LOW.", Name, Output.Terminal);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(LogEvents.ArcOffFailed, ex,
                "Welding machine '{Device}': process exiting and {Terminal} could not be driven LOW. THE START " +
                "CONTACT MAY STILL BE CLOSED.", Name, Output.Terminal);
        }
    }

    /// <summary>Drives the output LOW and releases the line.</summary>
    public void Dispose()
    {
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        foreach (var registration in _signals) registration.Dispose();
        lock (_attachGate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_line is { } line)
            {
                try
                {
                    line.Dispose();
                    _logger.LogInformation(LogEvents.Released,
                        "Welding machine '{Device}': {Terminal} driven LOW and released.", Name, Output.Terminal);
                }
                catch (Exception ex)
                {
                    _logger.LogCritical(LogEvents.ArcOffFailed, ex,
                        "Welding machine '{Device}': releasing {Terminal} failed. THE START CONTACT MAY STILL BE CLOSED.",
                        Name, Output.Terminal);
                }
                _line = null;
            }
            _state = SeeedDoConnectionState.Disconnected;
        }
        _callGate.Dispose();
    }
}
