namespace VoiceMeeterOutputAutoSwitcher.Core.Routing;

/// <summary>
/// Desired A2/A3 assignment. A1 is intentionally absent.
/// </summary>
public sealed record RoutingState(RoutingSlot? A2, RoutingSlot? A3)
{
    public static RoutingState Empty { get; } = new(null, null);

    public string? A2EndpointId => A2?.EndpointId;

    public string? A3EndpointId => A3?.EndpointId;

    /// <summary>
    /// True when A2/A3 endpoint IDs match (names/priority differences ignored).
    /// Used to skip unnecessary VoiceMeeter writes / engine restarts.
    /// </summary>
    public bool HasSameEndpoints(RoutingState other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return string.Equals(A2EndpointId, other.A2EndpointId, StringComparison.Ordinal)
               && string.Equals(A3EndpointId, other.A3EndpointId, StringComparison.Ordinal);
    }
}
