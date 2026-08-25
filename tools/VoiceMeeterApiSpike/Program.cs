using VoiceMeeterOutputAutoSwitcher.Infrastructure.VoiceMeeter;

static int PrintUsage()
{
    Console.WriteLine(
        """
        VoiceMeeter Remote API Spike (Phase 1)

        Usage:
          VoiceMeeterApiSpike                 Read-only: connect, status, list WDM outputs
          VoiceMeeterApiSpike --status        Same as default
          VoiceMeeterApiSpike --list-outputs  List all VoiceMeeter output devices
          VoiceMeeterApiSpike --set-a2 NAME   Assign WDM device to A2 (does not touch A1)
          VoiceMeeterApiSpike --set-a3 NAME   Assign WDM device to A3 (does not touch A1)
          VoiceMeeterApiSpike --clear-a2      Clear A2 device
          VoiceMeeterApiSpike --clear-a3      Clear A3 device
          VoiceMeeterApiSpike --restart       Restart VoiceMeeter Audio Engine

        Notes:
          - VoiceMeeter (Banana) must be running for status / set / restart.
          - Device NAME must match VoiceMeeter's device list (use --list-outputs).
        """);
    return 1;
}

static string DisplayName(string? value) =>
    string.IsNullOrWhiteSpace(value) ? "-" : value;

var argsList = args.ToList();
if (argsList.Contains("-h") || argsList.Contains("--help"))
{
    return PrintUsage();
}

var doStatus = argsList.Count == 0
               || argsList.Contains("--status")
               || argsList.Contains("--list-outputs")
               || argsList.Contains("--set-a2")
               || argsList.Contains("--set-a3")
               || argsList.Contains("--clear-a2")
               || argsList.Contains("--clear-a3")
               || argsList.Contains("--restart");

if (!doStatus)
{
    return PrintUsage();
}

try
{
    using var client = new VoiceMeeterRemoteClient();
    Console.WriteLine("Connecting to VoiceMeeter Remote API...");
    client.Connect();
    Console.WriteLine("Login OK.");

    if (!client.IsServerAvailable())
    {
        Console.WriteLine(
            "VoiceMeeter is installed but not running. Launch Banana and retry for full checks.");
        if (argsList.Contains("--set-a2")
            || argsList.Contains("--set-a3")
            || argsList.Contains("--clear-a2")
            || argsList.Contains("--clear-a3")
            || argsList.Contains("--restart"))
        {
            return 2;
        }

        // Enumeration may still work without the engine in some versions; try best-effort.
        try
        {
            PrintOutputs(client);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Output enumeration skipped: {ex.Message}");
        }

        return 0;
    }

    var product = client.GetProductType();
    var version = client.GetVersion();
    Console.WriteLine($"Product : {product}");
    Console.WriteLine($"Version : {version}");

    PrintBusState(client);

    if (argsList.Count == 0 || argsList.Contains("--status") || argsList.Contains("--list-outputs"))
    {
        PrintOutputs(client);
    }

    ApplyOptionalMutation(client, argsList, "--set-a2", client.SetA2WdmDevice);
    ApplyOptionalMutation(client, argsList, "--set-a3", client.SetA3WdmDevice);

    if (argsList.Contains("--clear-a2"))
    {
        Console.WriteLine("Clearing A2...");
        client.ClearA2Device();
        Thread.Sleep(200);
        PrintBusState(client);
    }

    if (argsList.Contains("--clear-a3"))
    {
        Console.WriteLine("Clearing A3...");
        client.ClearA3Device();
        Thread.Sleep(200);
        PrintBusState(client);
    }

    if (argsList.Contains("--restart"))
    {
        Console.WriteLine("Restarting Audio Engine (Command.Restart = 1)...");
        client.RestartAudioEngine();
        Thread.Sleep(500);
        Console.WriteLine("Restart requested.");
        PrintBusState(client);
    }

    Console.WriteLine("Done.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 1;
}

static void PrintBusState(VoiceMeeterRemoteClient client)
{
    Console.WriteLine("Current hardware outputs:");
    Console.WriteLine($"  A1: {DisplayName(client.GetBusDeviceName(VoiceMeeterRemoteClient.BusA1))} (read-only)");
    Console.WriteLine($"  A2: {DisplayName(client.GetBusDeviceName(VoiceMeeterRemoteClient.BusA2))}");
    Console.WriteLine($"  A3: {DisplayName(client.GetBusDeviceName(VoiceMeeterRemoteClient.BusA3))}");
}

static void PrintOutputs(VoiceMeeterRemoteClient client)
{
    var devices = client.GetOutputDevices();
    Console.WriteLine($"Output devices ({devices.Count}):");
    foreach (var device in devices)
    {
        Console.WriteLine(
            $"  [{device.Index}] {device.DeviceType,-4} | {device.Name} | hw={device.HardwareId}");
    }

    var wdm = devices.Where(d => d.DeviceType == VoiceMeeterDeviceType.Wdm).ToList();
    Console.WriteLine($"WDM outputs ({wdm.Count}):");
    foreach (var device in wdm)
    {
        Console.WriteLine($"  - {device.Name}");
    }
}

static void ApplyOptionalMutation(
    VoiceMeeterRemoteClient client,
    List<string> argsList,
    string flag,
    Action<string> setter)
{
    var index = argsList.IndexOf(flag);
    if (index < 0)
    {
        return;
    }

    if (index + 1 >= argsList.Count || argsList[index + 1].StartsWith("--", StringComparison.Ordinal))
    {
        throw new ArgumentException($"{flag} requires a device name argument.");
    }

    var name = argsList[index + 1];
    Console.WriteLine($"Setting {(flag == "--set-a2" ? "A2" : "A3")} WDM device to '{name}'...");
    setter(name);
    Thread.Sleep(200);
    PrintBusState(client);
}
