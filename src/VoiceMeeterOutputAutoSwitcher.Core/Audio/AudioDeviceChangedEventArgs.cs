namespace VoiceMeeterOutputAutoSwitcher.Core.Audio;

public sealed class AudioDeviceChangedEventArgs : EventArgs
{
    public AudioDeviceChangedEventArgs(
        AudioDeviceChangeKind changeKind,
        string endpointId,
        AudioDevice? device)
    {
        ChangeKind = changeKind;
        EndpointId = endpointId;
        Device = device;
    }

    public AudioDeviceChangeKind ChangeKind { get; }

    public string EndpointId { get; }

    /// <summary>
    /// Current snapshot when available (null for Removed, or if lookup fails).
    /// </summary>
    public AudioDevice? Device { get; }
}
