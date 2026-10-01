using ModelingEvolution.Signals;

namespace ModelingEvolution.WeldingMachine.Seeed.Plugin;

/// <summary>
/// Catalog-agnostic <c>float</c> projection used by
/// <see cref="GenericSeeedWelder.FloatSignals"/>.
///
/// <para>Inlined ON PURPOSE, copied from the fairino repo's FairinoDo plugin, which took it from <c>megmeet</c>'s
/// <c>ModelingEvolution.WeldingMachine.Megmeet/SignalProjection.cs</c> (itself a copy from
/// <c>fronius</c>): the plugin must depend ONLY on the <c>ModelingEvolution.Signals</c> kernel and stay
/// catalog-agnostic. The HOST owns catalog wiring through the <c>IDeviceSignalPublisher</c> port, so
/// the plugin's staged closure must NOT drag <c>ModelingEvolution.SignalProcessing</c> in just for one
/// operator. It is a copy, and a fix belongs in all three places.</para>
///
/// <para>Behaviour mirrors <c>SignalProcessing.SignalExtensions.Select</c>: the projected signal shares
/// the SOURCE <see cref="SignalMetadata"/> (same Uri, so catalog identity is unchanged),
/// <see cref="ISignal{T}.HasValue"/> tracks the source, <see cref="ISignal{T}.Value"/> is
/// <c>selector(source.Value)</c> when the source has one and <c>default</c> otherwise (the selector is
/// NOT invoked on an absent value), and <c>Subscribe</c> forwards each <see cref="Sample{T}"/>
/// preserving its timestamp.</para>
///
/// <para>That <c>HasValue</c> passthrough is load-bearing here: the four telemetry signals are created
/// and never <c>Set</c> (FR-2.3.8), and this plugin has nothing to measure them with. Forwarding
/// <c>HasValue</c> keeps them empty in the catalog too, rather than publishing a fabricated 0 A that an
/// oscilloscope would draw as a real reading.</para>
/// </summary>
internal static class SignalProjection
{
    /// <summary>Projects an <see cref="ISignal{TIn}"/> onto a derived <see cref="ISignal{TOut}"/> via
    /// <paramref name="selector"/>, sharing the source's metadata and timestamps.</summary>
    public static ISignal<TOut> Select<TIn, TOut>(this ISignal<TIn> source, Func<TIn, TOut> selector)
        => new ProjectedSignal<TIn, TOut>(source, selector);

    private sealed class ProjectedSignal<TIn, TOut>(ISignal<TIn> source, Func<TIn, TOut> selector)
        : ISignal<TOut>
    {
        public SignalMetadata Metadata => source.Metadata;
        public bool HasValue => source.HasValue;
        public TOut? Value => source.HasValue ? selector(source.Value!) : default;

        public IDisposable Subscribe(Action<Sample<TOut>> onSample)
            => source.Subscribe(s => onSample(new Sample<TOut>(s.TimestampUs, selector(s.Value!))));
    }
}
