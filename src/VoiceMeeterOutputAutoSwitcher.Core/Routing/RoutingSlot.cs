namespace VoiceMeeterOutputAutoSwitcher.Core.Routing;

/// <summary>
/// One hardware output bus assignment candidate (A2 or A3).
/// </summary>
public sealed record RoutingSlot(
    string EndpointId,
    string FriendlyName,
    int Priority);
