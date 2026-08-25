namespace VoiceMeeterOutputAutoSwitcher.Core.Audio;

/// <summary>
/// Playback endpoint availability as reported by Windows Core Audio.
/// </summary>
public enum AudioDeviceState
{
    Active = 1,
    Disabled = 2,
    NotPresent = 4,
    Unplugged = 8,
}
