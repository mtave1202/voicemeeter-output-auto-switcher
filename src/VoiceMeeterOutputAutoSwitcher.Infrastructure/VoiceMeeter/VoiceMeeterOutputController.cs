using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VoiceMeeterOutputAutoSwitcher.Application.Abstractions;
using VoiceMeeterOutputAutoSwitcher.Core.Routing;

namespace VoiceMeeterOutputAutoSwitcher.Infrastructure.VoiceMeeter;

public sealed class VoiceMeeterOutputController : IVoiceMeeterOutputController
{
    private readonly VoiceMeeterRemoteClient _client;
    private readonly ILogger<VoiceMeeterOutputController> _logger;
    private readonly bool _ownsClient;

    public VoiceMeeterOutputController(
        ILogger<VoiceMeeterOutputController> logger,
        VoiceMeeterRemoteClient? client = null)
    {
        _logger = logger;
        _ownsClient = client is null;
        _client = client ?? new VoiceMeeterRemoteClient();
    }

    public bool TryEnsureConnected()
    {
        try
        {
            if (!_client.IsLoggedIn)
            {
                _client.Connect();
                _logger.LogInformation("VoiceMeeter Remote API login OK.");
            }

            if (!_client.IsServerAvailable())
            {
                _logger.LogWarning("VoiceMeeter is not running.");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to VoiceMeeter Remote API.");
            return false;
        }
    }

    public void ApplyRouting(RoutingState desired)
    {
        ArgumentNullException.ThrowIfNull(desired);

        if (!TryEnsureConnected())
        {
            throw new InvalidOperationException("VoiceMeeter is not available.");
        }

        var outputs = _client.GetOutputDevices();
        var a2Name = ResolveOptional(desired.A2, outputs, "A2");
        var a3Name = ResolveOptional(desired.A3, outputs, "A3");

        _client.SetA2WdmDevice(a2Name ?? string.Empty);
        _client.SetA3WdmDevice(a3Name ?? string.Empty);

        _logger.LogInformation("A2 -> {A2}", string.IsNullOrEmpty(a2Name) ? "Empty" : a2Name);
        _logger.LogInformation("A3 -> {A3}", string.IsNullOrEmpty(a3Name) ? "Empty" : a3Name);

        Thread.Sleep(100);
        _client.RestartAudioEngine();
        _logger.LogInformation("VoiceMeeter Audio Engine restarted.");
    }

    public VoiceMeeterBusStatus GetBusStatus()
    {
        if (!TryEnsureConnected())
        {
            return new VoiceMeeterBusStatus(false, null, null, null);
        }

        try
        {
            return new VoiceMeeterBusStatus(
                true,
                Normalize(GetBusName(VoiceMeeterRemoteClient.BusA1)),
                Normalize(GetBusName(VoiceMeeterRemoteClient.BusA2)),
                Normalize(GetBusName(VoiceMeeterRemoteClient.BusA3)));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read VoiceMeeter bus status.");
            return new VoiceMeeterBusStatus(false, null, null, null);
        }
    }

    public void RestartAudioEngine()
    {
        if (!TryEnsureConnected())
        {
            throw new InvalidOperationException("VoiceMeeter is not available.");
        }

        _client.RestartAudioEngine();
        _logger.LogInformation("VoiceMeeter Audio Engine restarted.");
    }

    public void OpenVoiceMeeter()
    {
        var installDir = VoiceMeeterInstallLocator.TryGetInstallDirectory()
            ?? throw new FileNotFoundException("VoiceMeeter install directory was not found.");

        var candidates = new[]
        {
            "voicemeeterpro_x64.exe",
            "voicemeeterpro.exe",
            "voicemeeter_x64.exe",
            "voicemeeter.exe",
        };

        foreach (var fileName in candidates)
        {
            var path = Path.Combine(installDir, fileName);
            if (!File.Exists(path))
            {
                continue;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
            _logger.LogInformation("Launched VoiceMeeter: {Path}", path);
            return;
        }

        throw new FileNotFoundException("VoiceMeeter executable was not found in the install directory.");
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }

    private string GetBusName(int busIndex) => _client.GetBusDeviceName(busIndex);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private string? ResolveOptional(
        RoutingSlot? slot,
        IReadOnlyList<VoiceMeeterOutputDevice> outputs,
        string busLabel)
    {
        if (slot is null)
        {
            return null;
        }

        var name = VoiceMeeterDeviceNameResolver.ResolveWdmName(slot.FriendlyName, outputs);
        if (string.IsNullOrWhiteSpace(name))
        {
            _logger.LogWarning(
                "Could not resolve VoiceMeeter WDM name for {Bus} endpoint {EndpointId} ({FriendlyName}).",
                busLabel,
                slot.EndpointId,
                slot.FriendlyName);
            return null;
        }

        return name;
    }
}
