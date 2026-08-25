using VoiceMeeterOutputAutoSwitcher.Core.Audio;

namespace VoiceMeeterOutputAutoSwitcher.Application.Abstractions;

public interface IAudioDeviceWatcher : IDisposable
{
    event EventHandler<AudioDeviceChangedEventArgs>? DeviceChanged;

    IReadOnlyList<AudioDevice> GetPlaybackDevices(bool activeOnly = false);

    void Start();

    void Stop();
}
