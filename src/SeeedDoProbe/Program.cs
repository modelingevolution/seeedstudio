// Bench probe for the Seeed DO welder. Usage:
//   SeeedDoProbe cycle <terminal 1-4> [onMs=500] [count=3]   ArcOn/ArcOff through GenericSeeedWelder
//   SeeedDoProbe hold  <terminal 1-4> <on|off>               ArcOn (or attach LOW) and hold until SIGTERM/Ctrl+C
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelingEvolution.WeldingMachine.Seeed.Plugin;
using NSubstituteFree;
using RocketWelder.SDK.Abstractions;
using RocketWelder.SDK.Automation;

if (args.Length < 2) { Console.Error.WriteLine("usage: SeeedDoProbe cycle|hold <1-4> ..."); return 2; }

var services = new ServiceCollection()
    .AddLogging(b => b.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss.fff "; }))
    .AddSingleton<IDeviceQuery, NoDevices>()
    .BuildServiceProvider();

var terminal = int.Parse(args[1]);
using var welder = SeeedPlugin.Build(new ConfigSet(new SeeedDoOutputProperty(terminal)),
    DeviceId.New(SeeedPlugin.DeviceTypeKey), services);

Console.WriteLine($"IsConnected = {welder.IsConnected} on {welder.Location}");

switch (args[0])
{
    case "cycle":
        var onMs = args.Length > 2 ? int.Parse(args[2]) : 500;
        var count = args.Length > 3 ? int.Parse(args[3]) : 3;
        for (var i = 0; i < count; i++)
        {
            await welder.ArcOn();
            await Task.Delay(onMs);
            await welder.ArcOff();
            await Task.Delay(onMs);
        }
        break;

    case "hold":
        if (args.Length > 2 && args[2] == "on") await welder.ArcOn();
        Console.WriteLine($"holding, read-back {welder.LastReadBack}; pid {Environment.ProcessId}");
        var done = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.TrySetResult(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => done.TrySetResult();
        await done.Task;
        break;
}

Console.WriteLine("disposing (drives LOW)");
return 0;

namespace NSubstituteFree
{
    internal sealed class NoDevices : IDeviceQuery
    {
        public event Action<DeviceId>? DeviceConfigChanged { add { } remove { } }
        public DeviceSnapshot? GetById(DeviceId id) => null;
        public IEnumerable<DeviceSnapshot> GetByInterface(string interfaceType) => [];
        public int GetNextNumber(string interfaceType) => 1;
    }
}
