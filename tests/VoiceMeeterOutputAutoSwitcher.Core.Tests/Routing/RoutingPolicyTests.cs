using VoiceMeeterOutputAutoSwitcher.Core.Audio;
using VoiceMeeterOutputAutoSwitcher.Core.Routing;

namespace VoiceMeeterOutputAutoSwitcher.Core.Tests.Routing;

public sealed class RoutingPolicyTests
{
    private static AudioDevice Active(string id, string name) =>
        new(id, name, AudioDeviceState.Active);

    private static AudioDevice Inactive(string id, string name, AudioDeviceState state) =>
        new(id, name, state);

    private static ManagedDevice Managed(string id, string name, int priority, bool enabled = true) =>
        new(id, name, enabled, priority);

    [Fact]
    public void Resolve_NoManagedDevices_ReturnsEmpty()
    {
        var result = RoutingPolicy.Resolve(
            Array.Empty<ManagedDevice>(),
            [Active("a", "Speakers")]);

        Assert.Null(result.A2);
        Assert.Null(result.A3);
    }

    [Fact]
    public void Resolve_SingleActiveManaged_AssignsA2Only()
    {
        var managed = new[]
        {
            Managed("xm5", "WF-1000XM5", priority: 1),
        };
        var playback = new[]
        {
            Active("speakers", "Speakers"),
            Active("xm5", "WF-1000XM5"),
        };

        var result = RoutingPolicy.Resolve(managed, playback);

        Assert.Equal("xm5", result.A2EndpointId);
        Assert.Equal("WF-1000XM5", result.A2!.FriendlyName);
        Assert.Null(result.A3);
    }

    [Fact]
    public void Resolve_TwoActiveManaged_AssignsA2AndA3ByPriority()
    {
        var managed = new[]
        {
            Managed("airpods", "AirPods Pro", priority: 2),
            Managed("xm5", "WF-1000XM5", priority: 1),
        };
        var playback = new[]
        {
            Active("airpods", "AirPods Pro"),
            Active("xm5", "WF-1000XM5"),
        };

        var result = RoutingPolicy.Resolve(managed, playback);

        Assert.Equal("xm5", result.A2EndpointId);
        Assert.Equal("airpods", result.A3EndpointId);
    }

    [Fact]
    public void Resolve_ThreeActiveManaged_UsesTopTwoByPriority()
    {
        var managed = new[]
        {
            Managed("xm5", "WF-1000XM5", priority: 1),
            Managed("airpods", "AirPods Pro", priority: 2),
            Managed("earfun", "EarFun Air Pro 4", priority: 3),
        };
        var playback = new[]
        {
            Active("xm5", "WF-1000XM5"),
            Active("airpods", "AirPods Pro"),
            Active("earfun", "EarFun Air Pro 4"),
        };

        var result = RoutingPolicy.Resolve(managed, playback);

        Assert.Equal("xm5", result.A2EndpointId);
        Assert.Equal("airpods", result.A3EndpointId);
    }

    [Fact]
    public void Resolve_HighestPriorityDisconnected_PromotesWaitingDevice()
    {
        var managed = new[]
        {
            Managed("xm5", "WF-1000XM5", priority: 1),
            Managed("airpods", "AirPods Pro", priority: 2),
            Managed("earfun", "EarFun Air Pro 4", priority: 3),
        };
        var playback = new[]
        {
            Inactive("xm5", "WF-1000XM5", AudioDeviceState.Unplugged),
            Active("airpods", "AirPods Pro"),
            Active("earfun", "EarFun Air Pro 4"),
        };

        var result = RoutingPolicy.Resolve(managed, playback);

        Assert.Equal("airpods", result.A2EndpointId);
        Assert.Equal("earfun", result.A3EndpointId);
    }

    [Fact]
    public void Resolve_OnlyLowerPriorityRemains_AssignsToA2()
    {
        var managed = new[]
        {
            Managed("xm5", "WF-1000XM5", priority: 1),
            Managed("airpods", "AirPods Pro", priority: 2),
        };
        var playback = new[]
        {
            Inactive("xm5", "WF-1000XM5", AudioDeviceState.NotPresent),
            Active("airpods", "AirPods Pro"),
        };

        var result = RoutingPolicy.Resolve(managed, playback);

        Assert.Equal("airpods", result.A2EndpointId);
        Assert.Null(result.A3);
    }

    [Fact]
    public void Resolve_DisabledManaged_IsIgnoredEvenIfActive()
    {
        var managed = new[]
        {
            Managed("xm5", "WF-1000XM5", priority: 1, enabled: false),
            Managed("airpods", "AirPods Pro", priority: 2),
        };
        var playback = new[]
        {
            Active("xm5", "WF-1000XM5"),
            Active("airpods", "AirPods Pro"),
        };

        var result = RoutingPolicy.Resolve(managed, playback);

        Assert.Equal("airpods", result.A2EndpointId);
        Assert.Null(result.A3);
    }

    [Fact]
    public void Resolve_IgnoresNonManagedActiveDevices()
    {
        var managed = new[]
        {
            Managed("xm5", "WF-1000XM5", priority: 1),
        };
        var playback = new[]
        {
            Active("hdmi", "HDMI Audio"),
            Active("xm5", "WF-1000XM5"),
            Active("speakers", "Speakers"),
        };

        var result = RoutingPolicy.Resolve(managed, playback);

        Assert.Equal("xm5", result.A2EndpointId);
        Assert.Null(result.A3);
    }

    [Fact]
    public void Resolve_SamePriority_UsesDeviceIdAsTieBreaker()
    {
        var managed = new[]
        {
            Managed("b-device", "B", priority: 1),
            Managed("a-device", "A", priority: 1),
        };
        var playback = new[]
        {
            Active("b-device", "B"),
            Active("a-device", "A"),
        };

        var result = RoutingPolicy.Resolve(managed, playback);

        Assert.Equal("a-device", result.A2EndpointId);
        Assert.Equal("b-device", result.A3EndpointId);
    }

    [Fact]
    public void HasSameEndpoints_TrueWhenIdsMatchRegardlessOfNames()
    {
        var left = new RoutingState(
            new RoutingSlot("xm5", "Old Name", 1),
            null);
        var right = new RoutingState(
            new RoutingSlot("xm5", "New Name", 1),
            null);

        Assert.True(left.HasSameEndpoints(right));
    }

    [Fact]
    public void HasSameEndpoints_FalseWhenAssignmentChanges()
    {
        var before = new RoutingState(
            new RoutingSlot("xm5", "WF-1000XM5", 1),
            new RoutingSlot("airpods", "AirPods Pro", 2));
        var after = new RoutingState(
            new RoutingSlot("airpods", "AirPods Pro", 2),
            null);

        Assert.False(before.HasSameEndpoints(after));
    }
}
