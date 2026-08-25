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
    private readonly MMDeviceEnumerator _enumerator;
    private readonly NotificationClient _notificationClient;
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

    public AudioDevice? TryGetPlaybackDevice(string endpointId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);

        try
        {
            using var device = _enumerator.GetDevice(endpointId);
            if (device.DataFlow != DataFlow.Render)
            {
                return null;
            }

            return MapDevice(device);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_watching)
        {
            return;
        }

        _enumerator.RegisterEndpointNotificationCallback(_notificationClient);
        _watching = true;
    }

    public void Stop()
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _enumerator.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void RaiseDeviceChanged(AudioDeviceChangeKind kind, string endpointId)
    {
        AudioDevice? snapshot = null;
        if (kind != AudioDeviceChangeKind.Removed)
        {
            snapshot = TryGetPlaybackDevice(endpointId);
            // Capture endpoints also fire notifications; keep playback-only signal.
            if (snapshot is null)
            {
                return;
            }
        }

        DeviceChanged?.Invoke(
            this,
            new AudioDeviceChangedEventArgs(kind, endpointId, snapshot));
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
            _owner.RaiseDeviceChanged(AudioDeviceChangeKind.StateChanged, deviceId);

        public void OnDeviceAdded(string pwstrDeviceId) =>
            _owner.RaiseDeviceChanged(AudioDeviceChangeKind.Added, pwstrDeviceId);

        public void OnDeviceRemoved(string deviceId) =>
            _owner.RaiseDeviceChanged(AudioDeviceChangeKind.Removed, deviceId);

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            // Default device changes are out of MVP scope.
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) =>
            _owner.RaiseDeviceChanged(AudioDeviceChangeKind.PropertyChanged, pwstrDeviceId);
    }
}
