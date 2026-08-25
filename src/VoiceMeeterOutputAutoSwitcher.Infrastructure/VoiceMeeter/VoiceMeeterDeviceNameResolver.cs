namespace VoiceMeeterOutputAutoSwitcher.Infrastructure.VoiceMeeter;

internal static class VoiceMeeterDeviceNameResolver
{
    public static string? ResolveWdmName(
        string friendlyName,
        IReadOnlyList<VoiceMeeterOutputDevice> outputDevices)
    {
        if (string.IsNullOrWhiteSpace(friendlyName))
        {
            return null;
        }

        var wdmDevices = outputDevices
            .Where(d => d.DeviceType == VoiceMeeterDeviceType.Wdm)
            .ToList();

        var exact = wdmDevices.FirstOrDefault(d =>
            string.Equals(d.Name, friendlyName, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact.Name;
        }

        // Fallback: VoiceMeeter often accepts the Windows friendly name as-is.
        return friendlyName;
    }
}
