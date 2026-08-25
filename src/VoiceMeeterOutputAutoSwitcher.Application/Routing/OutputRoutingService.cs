using Microsoft.Extensions.Logging;
using VoiceMeeterOutputAutoSwitcher.Application.Abstractions;
using VoiceMeeterOutputAutoSwitcher.Core.Audio;
using VoiceMeeterOutputAutoSwitcher.Core.Routing;

namespace VoiceMeeterOutputAutoSwitcher.Application.Routing;

/// <summary>
/// Orchestrates device-change debounce, routing resolution, and VoiceMeeter A2/A3 sync.
/// </summary>
public sealed class OutputRoutingService : IAsyncDisposable
{
    private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromMinutes(1);

    private readonly IAudioDeviceWatcher _deviceWatcher;
    private readonly IVoiceMeeterOutputController _voiceMeeter;
    private readonly IAppSettingsRepository _settingsRepository;
    private readonly ILogger<OutputRoutingService> _logger;
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    private CancellationTokenSource? _debounceCts;
    private PeriodicTimer? _healthTimer;
    private Task? _healthLoop;
    private CancellationTokenSource? _healthCts;
    private RoutingState _lastApplied = RoutingState.Empty;
    private bool _started;
    private bool _disposed;

    public OutputRoutingService(
        IAudioDeviceWatcher deviceWatcher,
        IVoiceMeeterOutputController voiceMeeter,
        IAppSettingsRepository settingsRepository,
        ILogger<OutputRoutingService> logger)
    {
        _deviceWatcher = deviceWatcher;
        _voiceMeeter = voiceMeeter;
        _settingsRepository = settingsRepository;
        _logger = logger;
    }

    public RoutingState LastApplied => _lastApplied;

    public Task RequestSyncAsync(CancellationToken cancellationToken = default) =>
        SyncAsync(cancellationToken);

    public void ManualRestartAudioEngine()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _voiceMeeter.RestartAudioEngine();
        _logger.LogInformation("Manual VoiceMeeter Audio Engine restart requested.");
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        _logger.LogInformation("Output routing service starting.");
        _deviceWatcher.DeviceChanged += OnDeviceChanged;
        _deviceWatcher.Start();
        _started = true;

        _healthCts = new CancellationTokenSource();
        _healthTimer = new PeriodicTimer(HealthCheckInterval);
        _healthLoop = RunHealthLoopAsync(_healthCts.Token);

        // Startup sync (no debounce wait beyond a short settle is still OK; run immediately).
        await SyncAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        if (!_started)
        {
            return;
        }

        _deviceWatcher.DeviceChanged -= OnDeviceChanged;
        _deviceWatcher.Stop();
        CancelDebounce();
        await StopHealthLoopAsync().ConfigureAwait(false);
        _started = false;
        _logger.LogInformation("Output routing service stopped.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        CancelDebounce();
        _syncLock.Dispose();
        _deviceWatcher.Dispose();
        _voiceMeeter.Dispose();
        _disposed = true;
    }

    private async Task RunHealthLoopAsync(CancellationToken cancellationToken)
    {
        if (_healthTimer is null)
        {
            return;
        }

        try
        {
            while (await _healthTimer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    var devices = _deviceWatcher.GetPlaybackDevices(activeOnly: false);
                    if (devices.Count == 0)
                    {
                        _logger.LogWarning(
                            "Health check: playback enumeration returned 0 devices (enumerator may have been reset).");
                    }

                    // Catch missed notifications after long idle / sleep.
                    await SyncAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Health check failed.");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // shutdown
        }
    }

    private async Task StopHealthLoopAsync()
    {
        if (_healthCts is not null)
        {
            await _healthCts.CancelAsync().ConfigureAwait(false);
        }

        _healthTimer?.Dispose();
        _healthTimer = null;

        if (_healthLoop is not null)
        {
            try
            {
                await _healthLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected
            }

            _healthLoop = null;
        }

        _healthCts?.Dispose();
        _healthCts = null;
    }

    private void OnDeviceChanged(object? sender, AudioDeviceChangedEventArgs e)
    {
        // PropertyChanged fires in bursts during Bluetooth connect and was resetting
        // debounce too often. Only Added / Removed / StateChanged schedule a sync.
        if (e.ChangeKind == AudioDeviceChangeKind.PropertyChanged)
        {
            _logger.LogDebug(
                "Ignoring PropertyChanged for debounce: {EndpointId}",
                e.EndpointId);
            return;
        }

        _logger.LogInformation(
            "Device {ChangeKind}: {Name} ({EndpointId})",
            e.ChangeKind,
            e.Device?.FriendlyName ?? "(unknown)",
            e.EndpointId);

        ScheduleDebouncedSync();
    }

    private void ScheduleDebouncedSync()
    {
        var settings = _settingsRepository.Load();
        var delayMs = Math.Clamp(settings.DebounceMilliseconds, 500, 1500);

        var next = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _debounceCts, next);
        try
        {
            previous?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // ignored
        }

        previous?.Dispose();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMs, next.Token).ConfigureAwait(false);
                await SyncAsync(next.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Debounce reset or shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Debounced sync failed.");
            }
        });
    }

    private async Task SyncAsync(CancellationToken cancellationToken)
    {
        await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = _settingsRepository.Load();
            var playbackDevices = _deviceWatcher.GetPlaybackDevices(activeOnly: false);
            if (playbackDevices.Count == 0)
            {
                _logger.LogWarning(
                    "Playback device enumeration returned 0 devices; skipping VoiceMeeter update.");
                return;
            }

            var desired = RoutingPolicy.Resolve(settings.ManagedDevices, playbackDevices);

            _logger.LogInformation(
                "Desired routing A2={A2} A3={A3}",
                desired.A2?.FriendlyName ?? "-",
                desired.A3?.FriendlyName ?? "-");

            if (desired.HasSameEndpoints(_lastApplied))
            {
                _logger.LogInformation("Routing unchanged; skipping VoiceMeeter update.");
                return;
            }

            if (!_voiceMeeter.TryEnsureConnected())
            {
                _logger.LogWarning("VoiceMeeter unavailable; will retry on next device event or restart.");
                return;
            }

            _voiceMeeter.ApplyRouting(desired);
            _lastApplied = desired;

            _logger.LogInformation(
                "Output changed. A2 -> {A2}; A3 -> {A3}; VoiceMeeter Audio Engine restarted",
                desired.A2?.FriendlyName ?? "Empty",
                desired.A3?.FriendlyName ?? "Empty");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sync failed; continuing to watch devices.");
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private void CancelDebounce()
    {
        var cts = Interlocked.Exchange(ref _debounceCts, null);
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // ignored
        }

        cts.Dispose();
    }
}
