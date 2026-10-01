using Microsoft.Extensions.Logging;

namespace ModelingEvolution.WeldingMachine.Seeed.Plugin;

/// <summary>Stable event ids, so the arc path can be filtered out of a log by id.</summary>
internal static class LogEvents
{
    public static readonly EventId Write = new(2001, "SeeedDoWrite");
    public static readonly EventId Attached = new(2002, "SeeedDoAttached");
    public static readonly EventId AttachFailed = new(2003, "SeeedDoAttachFailed");
    public static readonly EventId ReadBackMismatch = new(2004, "SeeedDoReadBackMismatch");
    public static readonly EventId WriteFailed = new(2005, "SeeedDoWriteFailed");
    public static readonly EventId ArcOffFailed = new(2006, "SeeedDoArcOffFailed");
    public static readonly EventId ParameterIgnored = new(2007, "SeeedDoParameterIgnored");
    public static readonly EventId Released = new(2008, "SeeedDoReleased");
}
