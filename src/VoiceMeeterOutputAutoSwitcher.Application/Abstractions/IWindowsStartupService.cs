namespace VoiceMeeterOutputAutoSwitcher.Application.Abstractions;

public interface IWindowsStartupService
{
    bool IsEnabled();

    void SetEnabled(bool enabled);
}
