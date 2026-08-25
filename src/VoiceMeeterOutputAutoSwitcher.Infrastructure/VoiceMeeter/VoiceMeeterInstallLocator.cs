using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace VoiceMeeterOutputAutoSwitcher.Infrastructure.VoiceMeeter;

/// <summary>
/// Locates VoicemeeterRemote64.dll from the VoiceMeeter install directory.
/// </summary>
internal static class VoiceMeeterInstallLocator
{
    private const string UninstallSubKeyPrefix = "VB:Voicemeeter {";

    public static string FindRemoteDllPath()
    {
        var installDir = FindInstallDirectory()
            ?? throw new FileNotFoundException(
                "VoiceMeeter install directory was not found in the registry.");

        var dllName = Environment.Is64BitProcess
            ? "VoicemeeterRemote64.dll"
            : "VoicemeeterRemote.dll";

        var dllPath = Path.Combine(installDir, dllName);
        if (!File.Exists(dllPath))
        {
            throw new FileNotFoundException(
                $"VoiceMeeter Remote DLL was not found at '{dllPath}'.");
        }

        return dllPath;
    }

    public static string? TryGetInstallDirectory() => FindInstallDirectory();

    private static string? FindInstallDirectory()
    {
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var uninstall = baseKey.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null)
            {
                continue;
            }

            foreach (var subKeyName in uninstall.GetSubKeyNames())
            {
                if (!subKeyName.StartsWith(UninstallSubKeyPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using var appKey = uninstall.OpenSubKey(subKeyName);
                var uninstallString = appKey?.GetValue("UninstallString") as string;
                if (string.IsNullOrWhiteSpace(uninstallString))
                {
                    continue;
                }

                var path = uninstallString.Trim().Trim('"');
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                {
                    return directory;
                }
            }
        }

        // Fallback for atypical installs observed on this machine.
        var fallback = @"C:\Program Files (x86)\VB\Voicemeeter";
        return Directory.Exists(fallback) ? fallback : null;
    }
}
