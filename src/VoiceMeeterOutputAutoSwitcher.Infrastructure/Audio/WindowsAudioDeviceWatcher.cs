using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using VoiceMeeterOutputAutoSwitcher.Application.Abstractions;
using VoiceMeeterOutputAutoSwitcher.Core.Audio;

namespace VoiceMeeterOutputAutoSwitcher.Infrastructure.Audio;

/// <summary>
/// Enumerates and watches Windows playback endpoints via Core Audio (NAudio).
/// </summary>
public sealed class WindowsAudioDeviceWatcher : IAudioDeviceWatcher
{
    private readonly object _gate = new();
    private readonly NotificationClient _notificationClient;
    private MMDeviceEnumerator _enumerator;
    private bool _watching;
    private bool _disposed;

    public WindowsAudioDeviceWatcher()
    {
        _enumerator = new MMDeviceEnumerator();
        _notificationClient = new NotificationClient(this);
    }

    public event EventHandler<AudioDeviceChangedEventArgs>? DeviceChanged;

    public IReadOnlyList<AudioDevice> GetPlaybackDevices(bool activeOnly = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            try
            {
                var devices = EnumerateUnlocked(activeOnly);
                // An empty render list is effectively impossible on a normal desktop; treat as stale COM.
                if (devices.Count == 0)
                {
                    RecreateEnumeratorUnlocked();
                    devices = EnumerateUnlocked(activeOnly);
                }

                return devices;
            }
            catch (Exception)
            {
                RecreateEnumeratorUnlocked();
                return EnumerateUnlocked(activeOnly);
            }
        }
    }

    public AudioDevice? TryGetPlaybackDevice(string endpointId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);

        lock (_gate)
        {
            try
            {
                return TryGetUnlocked(endpointId);
            }
            catch (Exception)
            {
                try
                {
                    RecreateEnumeratorUnlocked();
                    return TryGetUnlocked(endpointId);
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            if (_watching)
            {
                return;
            }

            _enumerator.RegisterEndpointNotificationCallback(_notificationClient);
            _watching = true;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_watching || _disposed)
            {
                return;
            }

            try
            {
                _enumerator.UnregisterEndpointNotificationCallback(_notificationClient);
            }
            catch (Exception)
            {
                // Best-effort unregister.
            }

            _watching = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        lock (_gate)
        {
            _enumerator.Dispose();
            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }

    private List<AudioDevice> EnumerateUnlocked(bool activeOnly)
    {
        var stateMask = activeOnly ? DeviceState.Active : DeviceState.All;
        var collection = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, stateMask);

        var devices = new List<AudioDevice>(collection.Count);
        for (var i = 0; i < collection.Count; i++)
        {
            var device = collection[i];
            try
            {
                devices.Add(MapDevice(device));
            }
            finally
            {
                device.Dispose();
            }
        }

        return devices
            .OrderBy(d => d.FriendlyName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private AudioDevice? TryGetUnlocked(string endpointId)
    {
        using var device = _enumerator.GetDevice(endpointId);
        if (device.DataFlow != DataFlow.Render)
        {
            return null;
        }

        return MapDevice(device);
    }

    private void RecreateEnumeratorUnlocked()
    {
        var wasWatching = _watching;
        if (_watching)
        {
            try
            {
                _enumerator.UnregisterEndpointNotificationCallback(_notificationClient);
            }
            catch (Exception)
            {
                // ignored
            }

            _watching = false;
        }

        try
        {
            _enumerator.Dispose();
        }
        catch (Exception)
        {
            // ignored
        }

        _enumerator = new MMDeviceEnumerator();

        if (wasWatching)
        {
            _enumerator.RegisterEndpointNotificationCallback(_notificationClient);
            _watching = true;
        }
    }

    private void OnNativeNotification(AudioDeviceChangeKind kind, string endpointId)
    {
        // Core Audio callbacks arrive on a non-UI COM thread. Do not touch MMDeviceEnumerator here;
        // only queue a lightweight event so SyncAsync can re-enumerate on a safer path.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                DeviceChanged?.Invoke(
                    this,
                    new AudioDeviceChangedEventArgs(kind, endpointId, device: null));
            }
            catch (Exception)
            {
                // Never let callback-path exceptions tear down the process.
            }
        });
    }

    private static AudioDevice MapDevice(MMDevice device) =>
        new(
            device.ID,
            device.FriendlyName,
            MapState(device.State));

    private static AudioDeviceState MapState(DeviceState state) =>
        state switch
        {
            DeviceState.Active => AudioDeviceState.Active,
            DeviceState.Disabled => AudioDeviceState.Disabled,
            DeviceState.NotPresent => AudioDeviceState.NotPresent,
            DeviceState.Unplugged => AudioDeviceState.Unplugged,
            _ => AudioDeviceState.NotPresent,
        };

    private sealed class NotificationClient : IMMNotificationClient
    {
        private readonly WindowsAudioDeviceWatcher _owner;

        public NotificationClient(WindowsAudioDeviceWatcher owner)
        {
            _owner = owner;
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) =>
            _owner.OnNativeNotification(AudioDeviceChangeKind.StateChanged, deviceId);

        public void OnDeviceAdded(string pwstrDeviceId) =>
            _owner.OnNativeNotification(AudioDeviceChangeKind.Added, pwstrDeviceId);

        public void OnDeviceRemoved(string deviceId) =>
            _owner.OnNativeNotification(AudioDeviceChangeKind.Removed, deviceId);

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            // Out of MVP scope.
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) =>
            _owner.OnNativeNotification(AudioDeviceChangeKind.PropertyChanged, pwstrDeviceId);
    }
}
