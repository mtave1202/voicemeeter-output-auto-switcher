namespace VoiceMeeterOutputAutoSwitcher.Core.Routing;

/// <summary>
/// User-configured playback endpoint that may be auto-assigned to A2/A3.
/// Lower <see cref="Priority"/> value means higher preference (1 before 2).
/// </summary>
public sealed record ManagedDevice(
    string DeviceId,
    string Name,
    bool Enabled,
    int Priority);
