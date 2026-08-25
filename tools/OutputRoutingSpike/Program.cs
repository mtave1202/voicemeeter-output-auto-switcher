using Microsoft.Extensions.Logging;
using VoiceMeeterOutputAutoSwitcher.Application.Routing;
using VoiceMeeterOutputAutoSwitcher.Core.Routing;
using VoiceMeeterOutputAutoSwitcher.Infrastructure.Audio;
using VoiceMeeterOutputAutoSwitcher.Infrastructure.Settings;
using VoiceMeeterOutputAutoSwitcher.Infrastructure.VoiceMeeter;

static void PrintUsage()
{
    Console.WriteLine(
        """
        Output Routing Spike (Phase 4)

        Usage:
          OutputRoutingSpike                        Run auto-sync (Ctrl+C to stop)
          OutputRoutingSpike --settings PATH        Use a specific settings.json
          OutputRoutingSpike --init-headphones      Seed managedDevices from Active
                                                    Headphones/Headset endpoints, then run
          OutputRoutingSpike --list-settings        Print settings and exit
          OutputRoutingSpike --duration SEC         Auto-stop after SEC seconds

        Flow: device change -> debounce -> RoutingPolicy -> A2/A3 apply -> Engine Restart
        """);
}

var argsList = args.ToList();
if (argsList.Contains("-h") || argsList.Contains("--help"))
{
    PrintUsage();
    return 0;
}

var settingsIndex = argsList.IndexOf("--settings");
string? settingsPath = null;
if (settingsIndex >= 0)
{
    if (settingsIndex + 1 >= argsList.Count)
    {
        Console.Error.WriteLine("--settings requires a file path.");
        return 1;
    }

    settingsPath = argsList[settingsIndex + 1];
}

int? durationSeconds = null;
var durationIndex = argsList.IndexOf("--duration");
if (durationIndex >= 0)
{
    if (durationIndex + 1 >= argsList.Count
        || !int.TryParse(argsList[durationIndex + 1], out var sec)
        || sec <= 0)
    {
        Console.Error.WriteLine("--duration requires a positive integer (seconds).");
        return 1;
    }

    durationSeconds = sec;
}

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder
        .SetMinimumLevel(LogLevel.Information)
        .AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });
});

var settingsRepo = new JsonAppSettingsRepository(settingsPath);
Console.WriteLine($"Settings: {settingsRepo.FilePath}");

if (argsList.Contains("--list-settings"))
{
    var settings = settingsRepo.Load();
    Console.WriteLine($"Debounce: {settings.DebounceMilliseconds}ms");
    Console.WriteLine($"Managed devices: {settings.ManagedDevices.Count}");
    foreach (var device in settings.ManagedDevices.OrderBy(d => d.Priority))
    {
        Console.WriteLine(
            $"  [{(device.Enabled ? "ON" : "off")}] p={device.Priority} {device.Name} id={device.DeviceId}");
    }

    return 0;
}

if (argsList.Contains("--init-headphones"))
{
    using var probe = new WindowsAudioDeviceWatcher();
    var candidates = probe.GetPlaybackDevices(activeOnly: true)
        .Where(IsHeadphoneLike)
        .OrderBy(d => d.FriendlyName, StringComparer.OrdinalIgnoreCase)
        .ToList();

    var settings = settingsRepo.Load();
    settings.ManagedDevices = candidates
        .Select((d, index) => new ManagedDevice(
            d.EndpointId,
            d.FriendlyName,
            Enabled: true,
            Priority: index + 1))
        .ToList();
    settingsRepo.Save(settings);

    Console.WriteLine($"Seeded {settings.ManagedDevices.Count} headphone-like managed device(s).");
    foreach (var device in settings.ManagedDevices)
    {
        Console.WriteLine($"  p={device.Priority} {device.Name}");
    }
}

var loaded = settingsRepo.Load();
if (loaded.ManagedDevices.Count == 0)
{
    Console.WriteLine(
        "No managedDevices configured. Re-run with --init-headphones, or edit settings.json.");
    Console.WriteLine("Continuing anyway (routing stays empty until devices are configured).");
}

await using var service = new OutputRoutingService(
    new WindowsAudioDeviceWatcher(),
    new VoiceMeeterOutputController(loggerFactory.CreateLogger<VoiceMeeterOutputController>()),
    settingsRepo,
    loggerFactory.CreateLogger<OutputRoutingService>());

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

if (durationSeconds is int seconds)
{
    cts.CancelAfter(TimeSpan.FromSeconds(seconds));
}

Console.WriteLine("Starting output routing. Connect/disconnect managed devices to test. Ctrl+C to stop.");
await service.StartAsync(CancellationToken.None);

try
{
    await Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
}
catch (OperationCanceledException)
{
    // expected
}

await service.StopAsync();
Console.WriteLine(
    $"Stopped. LastApplied A2={service.LastApplied.A2?.FriendlyName ?? "-"} A3={service.LastApplied.A3?.FriendlyName ?? "-"}");
return 0;

static bool IsHeadphoneLike(VoiceMeeterOutputAutoSwitcher.Core.Audio.AudioDevice device)
{
    var name = device.FriendlyName;
    if (name.Contains("Voicemeeter", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    // Prefer A2DP "Headphones"; still allow Headset endpoints if that is all Windows exposes.
    return name.Contains("Headphone", StringComparison.OrdinalIgnoreCase)
           || name.Contains("Headset", StringComparison.OrdinalIgnoreCase);
}
