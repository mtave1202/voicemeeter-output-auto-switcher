using VoiceMeeterOutputAutoSwitcher.Application.Settings;

namespace VoiceMeeterOutputAutoSwitcher.Application.Abstractions;

public interface IAppSettingsRepository
{
    AppSettings Load();

    void Save(AppSettings settings);
}
