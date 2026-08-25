using VoiceMeeterOutputAutoSwitcher.Core.Audio;

namespace VoiceMeeterOutputAutoSwitcher.Core.Routing;

/// <summary>
/// Pure routing policy: pick up to two enabled, Active managed devices by priority.
/// Independent of Windows / VoiceMeeter APIs.
/// </summary>
public static class RoutingPolicy
{
    public const int MaxAssignableBuses = 2;

    public static RoutingState Resolve(
        IEnumerable<ManagedDevice> managedDevices,
        IEnumerable<AudioDevice> playbackDevices)
    {
        ArgumentNullException.ThrowIfNull(managedDevices);
        ArgumentNullException.ThrowIfNull(playbackDevices);

        var activeById = playbackDevices
            .Where(d => d.IsActive)
            .GroupBy(d => d.EndpointId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var selected = managedDevices
            .Where(m => m.Enabled)
            .Where(m => !string.IsNullOrWhiteSpace(m.DeviceId))
            .Where(m => activeById.ContainsKey(m.DeviceId))
            .OrderBy(m => m.Priority)
            .ThenBy(m => m.DeviceId, StringComparer.Ordinal)
            .Take(MaxAssignableBuses)
            .Select(m =>
            {
                var device = activeById[m.DeviceId];
                var displayName = string.IsNullOrWhiteSpace(device.FriendlyName)
                    ? m.Name
                    : device.FriendlyName;
                return new RoutingSlot(m.DeviceId, displayName, m.Priority);
            })
            .ToList();

        return selected.Count switch
        {
            0 => RoutingState.Empty,
            1 => new RoutingState(selected[0], null),
            _ => new RoutingState(selected[0], selected[1]),
        };
    }
}
