using System.Drawing;
using System.IO;
using System.Windows.Forms;
using VoiceMeeterOutputAutoSwitcher.Application.Abstractions;
using VoiceMeeterOutputAutoSwitcher.Application.Routing;

namespace VoiceMeeterOutputAutoSwitcher.Tray;

public sealed class TrayIconController : IDisposable
{
    private readonly IVoiceMeeterOutputController _voiceMeeter;
    private readonly OutputRoutingService _routingService;
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _appIcon;
    private readonly ToolStripMenuItem _a1Item;
    private readonly ToolStripMenuItem _a2Item;
    private readonly ToolStripMenuItem _a3Item;
    private readonly ToolStripMenuItem _connectionItem;
    private bool _disposed;

    public TrayIconController(
        IVoiceMeeterOutputController voiceMeeter,
        OutputRoutingService routingService)
    {
        _voiceMeeter = voiceMeeter;
        _routingService = routingService;
        _appIcon = LoadAppIcon();

        _a1Item = new ToolStripMenuItem("A1: -") { Enabled = false };
        _a2Item = new ToolStripMenuItem("A2: -") { Enabled = false };
        _a3Item = new ToolStripMenuItem("A3: -") { Enabled = false };
        _connectionItem = new ToolStripMenuItem("VoiceMeeter: -") { Enabled = false };

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripLabel("VoiceMeeter Auto Switcher"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripLabel("出力状態"));
        menu.Items.Add(_connectionItem);
        menu.Items.Add(_a1Item);
        menu.Items.Add(_a2Item);
        menu.Items.Add(_a3Item);
        menu.Items.Add(new ToolStripSeparator());

        var settingsItem = new ToolStripMenuItem("設定", null, (_, _) => OpenSettingsRequested?.Invoke(this, EventArgs.Empty));
        var restartItem = new ToolStripMenuItem("Audio Engineを再起動", null, (_, _) => OnRestart());
        var openVmItem = new ToolStripMenuItem("VoiceMeeterを開く", null, (_, _) => OnOpenVoiceMeeter());
        var exitItem = new ToolStripMenuItem("終了", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        menu.Items.Add(settingsItem);
        menu.Items.Add(restartItem);
        menu.Items.Add(openVmItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        menu.Opening += (_, _) => RefreshStatus();

        _notifyIcon = new NotifyIcon
        {
            Text = "VoiceMeeter Output Auto Switcher",
            Icon = _appIcon,
            Visible = false,
            ContextMenuStrip = menu,
        };
        _notifyIcon.DoubleClick += (_, _) => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? OpenSettingsRequested;

    public event EventHandler? ExitRequested;

    public void Initialize()
    {
        _notifyIcon.Visible = true;
        RefreshStatus();
    }

    public void RefreshStatus()
    {
        try
        {
            var status = _voiceMeeter.GetBusStatus();
            _connectionItem.Text = status.IsConnected ? "VoiceMeeter: Connected" : "VoiceMeeter: Disconnected";
            _a1Item.Text = $"A1: {Display(status.A1DeviceName)}";
            _a2Item.Text = $"A2: {Display(status.A2DeviceName ?? _routingService.LastApplied.A2?.FriendlyName)}";
            _a3Item.Text = $"A3: {Display(status.A3DeviceName ?? _routingService.LastApplied.A3?.FriendlyName)}";
            _notifyIcon.Text = status.IsConnected
                ? $"A2: {Display(status.A2DeviceName)}\nA3: {Display(status.A3DeviceName)}"
                : "VoiceMeeter Disconnected";
        }
        catch
        {
            _connectionItem.Text = "VoiceMeeter: Error";
            _a1Item.Text = "A1: -";
            _a2Item.Text = "A2: -";
            _a3Item.Text = "A3: -";
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _appIcon.Dispose();
        _disposed = true;
    }

    private static Icon LoadAppIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.ico");
        if (File.Exists(path))
        {
            return new Icon(path);
        }

        return SystemIcons.Application;
    }

    private void OnRestart()
    {
        try
        {
            _routingService.ManualRestartAudioEngine();
            RefreshStatus();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Audio Engine の再起動に失敗しました。\n{ex.Message}",
                "VoiceMeeter Output Auto Switcher",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    private void OnOpenVoiceMeeter()
    {
        try
        {
            _voiceMeeter.OpenVoiceMeeter();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"VoiceMeeter を開けませんでした。\n{ex.Message}",
                "VoiceMeeter Output Auto Switcher",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    private static string Display(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value;
}
