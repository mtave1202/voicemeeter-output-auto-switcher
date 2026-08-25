using VoiceMeeterOutputAutoSwitcher.Core.Routing;

namespace VoiceMeeterOutputAutoSwitcher.Application.Settings;

public sealed class AppSettings
{
    public int DebounceMilliseconds { get; set; } = 500;

    public bool StartWithWindows { get; set; }

    public List<ManagedDevice> ManagedDevices { get; set; } = [];
}
