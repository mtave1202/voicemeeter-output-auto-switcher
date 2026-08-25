using Microsoft.Win32;
using VoiceMeeterOutputAutoSwitcher.Application.Abstractions;

namespace VoiceMeeterOutputAutoSwitcher.Infrastructure.Startup;

public sealed class WindowsStartupService : IWindowsStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "VoiceMeeterOutputAutoSwitcher";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string;
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Process path is unavailable.");
        key.SetValue(ValueName, $"\"{exePath}\"");
    }
}
