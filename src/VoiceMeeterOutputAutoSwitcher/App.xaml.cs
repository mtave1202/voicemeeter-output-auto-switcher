using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VoiceMeeterOutputAutoSwitcher.Application.Abstractions;
using VoiceMeeterOutputAutoSwitcher.Application.Routing;
using VoiceMeeterOutputAutoSwitcher.Infrastructure.Audio;
using VoiceMeeterOutputAutoSwitcher.Infrastructure.Logging;
using VoiceMeeterOutputAutoSwitcher.Infrastructure.Settings;
using VoiceMeeterOutputAutoSwitcher.Infrastructure.Startup;
using VoiceMeeterOutputAutoSwitcher.Infrastructure.VoiceMeeter;
using VoiceMeeterOutputAutoSwitcher.Tray;
using VoiceMeeterOutputAutoSwitcher.Views;

namespace VoiceMeeterOutputAutoSwitcher;

public partial class App : System.Windows.Application
{
    private const string MutexName = @"Local\VoiceMeeterOutputAutoSwitcher";

    private Mutex? _mutex;
    private ServiceProvider? _services;
    private TrayIconController? _tray;
    private OutputRoutingService? _routingService;
    private bool _isShuttingDown;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show(
                "VoiceMeeter Output Auto Switcher is already running.",
                "VoiceMeeter Output Auto Switcher",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var services = new ServiceCollection();
        ConfigureServices(services);
        _services = services.BuildServiceProvider();

        var logger = _services.GetRequiredService<ILogger<App>>();
        logger.LogInformation("Application starting.");

        var settingsRepo = _services.GetRequiredService<IAppSettingsRepository>();
        var startup = _services.GetRequiredService<IWindowsStartupService>();
        var settings = settingsRepo.Load();
        try
        {
            startup.SetEnabled(settings.StartWithWindows);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to apply Windows startup setting.");
        }

        _routingService = _services.GetRequiredService<OutputRoutingService>();
        _tray = _services.GetRequiredService<TrayIconController>();
        _tray.ExitRequested += (_, _) => _ = BeginShutdownAsync();
        _tray.OpenSettingsRequested += (_, _) => ShowSettings();
        _tray.Initialize();

        try
        {
            await _routingService.StartAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start routing service.");
        }

        _tray.RefreshStatus();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!_isShuttingDown)
        {
            _ = BeginShutdownAsync(invokeShutdown: false);
        }

        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddConsole();
            builder.AddProvider(new SimpleFileLoggerProvider());
        });

        services.AddSingleton<IAppSettingsRepository, JsonAppSettingsRepository>();
        services.AddSingleton<IAudioDeviceWatcher, WindowsAudioDeviceWatcher>();
        services.AddSingleton<IVoiceMeeterOutputController, VoiceMeeterOutputController>();
        services.AddSingleton<IWindowsStartupService, WindowsStartupService>();
        services.AddSingleton<OutputRoutingService>();
        services.AddSingleton<TrayIconController>();
    }

    private void ShowSettings()
    {
        if (_services is null)
        {
            return;
        }

        foreach (Window window in Windows)
        {
            if (window is SettingsWindow existing)
            {
                existing.Activate();
                return;
            }
        }

        var windowToShow = new SettingsWindow(
            _services.GetRequiredService<IAppSettingsRepository>(),
            _services.GetRequiredService<IAudioDeviceWatcher>(),
            _services.GetRequiredService<IWindowsStartupService>(),
            _services.GetRequiredService<OutputRoutingService>(),
            _services.GetRequiredService<ILogger<SettingsWindow>>());
        windowToShow.Show();
    }

    private async Task BeginShutdownAsync(bool invokeShutdown = true)
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;

        try
        {
            if (_routingService is not null)
            {
                await _routingService.StopAsync().ConfigureAwait(true);
                await _routingService.DisposeAsync().ConfigureAwait(true);
                _routingService = null;
            }
        }
        catch
        {
            // Best-effort shutdown.
        }

        _tray?.Dispose();
        _tray = null;
        _services?.Dispose();
        _services = null;

        if (_mutex is not null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch
            {
                // ignored
            }

            _mutex.Dispose();
            _mutex = null;
        }

        if (invokeShutdown)
        {
            Shutdown();
        }
    }
}
