using VoiceMeeterOutputAutoSwitcher.Core.Audio;
using VoiceMeeterOutputAutoSwitcher.Infrastructure.Audio;

static int PrintUsage()
{
    Console.WriteLine(
        """
        Windows Audio Device Spike (Phase 2)

        Usage:
          WindowsAudioDeviceSpike                 List playback endpoints
          WindowsAudioDeviceSpike --list          Same as default
          WindowsAudioDeviceSpike --active        List Active playback endpoints only
          WindowsAudioDeviceSpike --watch [sec]   Watch device events (default 60 seconds)
                                                  Press Ctrl+C to stop early

        Connect/disconnect a Bluetooth headset while --watch is running to verify events.
        """);
    return 1;
}

var argsList = args.ToList();
if (argsList.Contains("-h") || argsList.Contains("--help"))
{
    return PrintUsage();
}

var watch = argsList.Contains("--watch");
var activeOnly = argsList.Contains("--active");
var list = argsList.Count == 0 || argsList.Contains("--list") || activeOnly || watch;

if (!list)
{
    return PrintUsage();
}

var watchSeconds = 60;
var watchIndex = argsList.IndexOf("--watch");
if (watchIndex >= 0
    && watchIndex + 1 < argsList.Count
    && int.TryParse(argsList[watchIndex + 1], out var parsed)
    && parsed > 0)
{
    watchSeconds = parsed;
}

try
{
    using var watcher = new WindowsAudioDeviceWatcher();

    Console.WriteLine(activeOnly ? "Active playback devices:" : "Playback devices (all states):");
    PrintDevices(watcher.GetPlaybackDevices(activeOnly));

    if (!watch)
    {
        return 0;
    }

    Console.WriteLine();
    Console.WriteLine($"Watching for {watchSeconds}s... (Ctrl+C to stop)");
    Console.WriteLine("Connect or disconnect a Bluetooth playback device to test events.");

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(watchSeconds));
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    watcher.DeviceChanged += (_, e) =>
    {
        var name = e.Device?.FriendlyName ?? "(unknown)";
        var state = e.Device?.State.ToString() ?? "-";
        Console.WriteLine(
            $"{DateTime.Now:HH:mm:ss.fff} {e.ChangeKind,-16} state={state,-11} name={name}");
        Console.WriteLine($"                 id={e.EndpointId}");
    };

    watcher.Start();

    try
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
    }
    catch (OperationCanceledException)
    {
        // Expected on timeout / Ctrl+C.
    }

    watcher.Stop();
    Console.WriteLine("Watch stopped.");
    Console.WriteLine();
    Console.WriteLine("Playback devices after watch:");
    PrintDevices(watcher.GetPlaybackDevices(activeOnly: false));
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 1;
}

static void PrintDevices(IReadOnlyList<AudioDevice> devices)
{
    Console.WriteLine($"Count: {devices.Count}");
    foreach (var device in devices)
    {
        Console.WriteLine(
            $"  [{device.State,-11}] {device.FriendlyName}");
        Console.WriteLine($"               id={device.EndpointId}");
    }
}
