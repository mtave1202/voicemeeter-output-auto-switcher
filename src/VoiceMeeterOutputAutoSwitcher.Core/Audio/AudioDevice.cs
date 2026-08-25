namespace VoiceMeeterOutputAutoSwitcher.Core.Audio;

/// <summary>
/// Windows playback endpoint snapshot.
/// </summary>
public sealed record AudioDevice(
    string EndpointId,
    string FriendlyName,
    AudioDeviceState State)
{
    public bool IsActive => State == AudioDeviceState.Active;
}
