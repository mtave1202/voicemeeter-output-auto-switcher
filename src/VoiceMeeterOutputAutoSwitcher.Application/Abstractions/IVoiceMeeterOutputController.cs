using VoiceMeeterOutputAutoSwitcher.Core.Routing;

namespace VoiceMeeterOutputAutoSwitcher.Application.Abstractions;

public interface IVoiceMeeterOutputController : IDisposable
{
    /// <summary>
    /// Ensures Remote API login. Returns false when VoiceMeeter is not running.
    /// </summary>
    bool TryEnsureConnected();

    /// <summary>
    /// Applies A2/A3 WDM devices from the desired routing state and restarts the engine when changed.
    /// Must not modify A1.
    /// </summary>
    void ApplyRouting(RoutingState desired);

    VoiceMeeterBusStatus GetBusStatus();

    void RestartAudioEngine();

    void OpenVoiceMeeter();
}
