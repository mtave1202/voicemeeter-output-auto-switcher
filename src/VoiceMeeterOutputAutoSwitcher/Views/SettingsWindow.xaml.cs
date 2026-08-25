using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Microsoft.Extensions.Logging;
using VoiceMeeterOutputAutoSwitcher.Application.Abstractions;
using VoiceMeeterOutputAutoSwitcher.Application.Routing;
using VoiceMeeterOutputAutoSwitcher.Core.Routing;

namespace VoiceMeeterOutputAutoSwitcher.Views;

public partial class SettingsWindow : Window
{
    private readonly IAppSettingsRepository _settingsRepository;
    private readonly IAudioDeviceWatcher _deviceWatcher;
    private readonly IWindowsStartupService _startupService;
    private readonly OutputRoutingService _routingService;
    private readonly ILogger<SettingsWindow> _logger;
    private readonly ObservableCollection<DeviceRow> _rows = [];

    public SettingsWindow(
        IAppSettingsRepository settingsRepository,
        IAudioDeviceWatcher deviceWatcher,
        IWindowsStartupService startupService,
        OutputRoutingService routingService,
        ILogger<SettingsWindow> logger)
    {
        InitializeComponent();
        _settingsRepository = settingsRepository;
        _deviceWatcher = deviceWatcher;
        _startupService = startupService;
        _routingService = routingService;
        _logger = logger;
        DevicesGrid.ItemsSource = _rows;
        LoadRows();
    }

    private void LoadRows()
    {
        var settings = _settingsRepository.Load();
        var managedById = settings.ManagedDevices
            .ToDictionary(d => d.DeviceId, d => d, StringComparer.Ordinal);

        StartWithWindowsCheck.IsChecked = settings.StartWithWindows;
        DebounceBox.Text = settings.DebounceMilliseconds.ToString();

        var devices = _deviceWatcher.GetPlaybackDevices(activeOnly: false)
            .Where(d => !d.FriendlyName.Contains("Voicemeeter", StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d.FriendlyName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (devices.Count == 0)
        {
            _logger.LogWarning("Settings: playback enumeration returned 0 devices.");
            System.Windows.MessageBox.Show(
                "再生デバイスを取得できませんでした。\nしばらく待って「再読み込み」を押すか、アプリを再起動してください。",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        _rows.Clear();

        // Managed first (by priority), then unmanaged.
        foreach (var managed in settings.ManagedDevices.OrderBy(d => d.Priority))
        {
            var live = devices.FirstOrDefault(d => d.EndpointId == managed.DeviceId);
            _rows.Add(new DeviceRow
            {
                EndpointId = managed.DeviceId,
                FriendlyName = live?.FriendlyName ?? managed.Name,
                State = live?.State.ToString() ?? "Missing",
                IsManaged = true,
                IsEnabled = managed.Enabled,
                Priority = managed.Priority,
            });
        }

        foreach (var device in devices)
        {
            if (managedById.ContainsKey(device.EndpointId))
            {
                continue;
            }

            _rows.Add(new DeviceRow
            {
                EndpointId = device.EndpointId,
                FriendlyName = device.FriendlyName,
                State = device.State.ToString(),
                IsManaged = false,
                IsEnabled = true,
                Priority = 0,
            });
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(DebounceBox.Text, out var debounce))
        {
            System.Windows.MessageBox.Show("Debounce は数値で入力してください。", Title);
            return;
        }

        debounce = Math.Clamp(debounce, 500, 1500);

        var managed = _rows
            .Where(r => r.IsManaged)
            .OrderBy(r => r.Priority <= 0 ? int.MaxValue : r.Priority)
            .ThenBy(r => r.FriendlyName, StringComparer.OrdinalIgnoreCase)
            .Select((r, index) => new ManagedDevice(
                r.EndpointId,
                r.FriendlyName,
                r.IsEnabled,
                Priority: index + 1))
            .ToList();

        var settings = _settingsRepository.Load();
        settings.DebounceMilliseconds = debounce;
        settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
        settings.ManagedDevices = managed;
        _settingsRepository.Save(settings);

        try
        {
            _startupService.SetEnabled(settings.StartWithWindows);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update Windows startup registration.");
            System.Windows.MessageBox.Show(
                $"自動起動の登録に失敗しました。\n{ex.Message}",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        try
        {
            await _routingService.RequestSyncAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sync after settings save.");
        }

        LoadRows();
        System.Windows.MessageBox.Show("設定を保存しました。", Title);
    }

    private void Reload_Click(object sender, RoutedEventArgs e) => LoadRows();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);

    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveSelected(1);

    private void MoveSelected(int delta)
    {
        if (DevicesGrid.SelectedItem is not DeviceRow selected || !selected.IsManaged)
        {
            return;
        }

        var managed = _rows.Where(r => r.IsManaged).OrderBy(r => r.Priority).ToList();
        var index = managed.IndexOf(selected);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= managed.Count)
        {
            return;
        }

        (managed[index].Priority, managed[target].Priority) = (managed[target].Priority, managed[index].Priority);
        // Normalize 1..n
        foreach (var (row, i) in managed.OrderBy(r => r.Priority).Select((r, i) => (r, i)))
        {
            row.Priority = i + 1;
        }

        DevicesGrid.Items.Refresh();
    }

    private sealed class DeviceRow : INotifyPropertyChanged
    {
        private bool _isManaged;
        private bool _isEnabled = true;
        private int _priority;

        public required string EndpointId { get; init; }

        public required string FriendlyName { get; init; }

        public required string State { get; init; }

        public bool IsManaged
        {
            get => _isManaged;
            set
            {
                if (_isManaged == value)
                {
                    return;
                }

                _isManaged = value;
                if (_isManaged && _priority <= 0)
                {
                    Priority = 99;
                }

                OnPropertyChanged();
            }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled == value)
                {
                    return;
                }

                _isEnabled = value;
                OnPropertyChanged();
            }
        }

        public int Priority
        {
            get => _priority;
            set
            {
                if (_priority == value)
                {
                    return;
                }

                _priority = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
