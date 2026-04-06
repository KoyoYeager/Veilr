using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using Veilr.Helpers;
using Veilr.Services;
using Veilr.Views;

namespace Veilr;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private SheetWindow? _sheetWindow;
    private SettingsService? _settingsService;
    private HotkeyService? _hotkeyService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settingsService = new SettingsService();
        _settingsService.Load();

        Loc.SetLanguage(_settingsService.Settings.UiTheme.Language);

        _sheetWindow = new SheetWindow(_settingsService);
        _sheetWindow.Show();

        _hotkeyService = new HotkeyService(_settingsService);
        _hotkeyService.SheetToggleRequested += () =>
        {
            if (_sheetWindow.IsVisible)
                _sheetWindow.Hide();
            else
                _sheetWindow.Show();
        };

        InitializeTrayIcon();
    }

    private void InitializeTrayIcon()
    {
        _trayIcon = new TaskbarIcon
        {
            IconSource = new System.Windows.Media.Imaging.BitmapImage(
                new System.Uri("pack://application:,,,/Resources/veilr-icon.ico")),
            ToolTipText = "Veilr - Screen Color Eraser"
        };

        var menu = new System.Windows.Controls.ContextMenu();

        var toggleItem = new System.Windows.Controls.MenuItem { Header = Loc.ShowHideSheet };
        toggleItem.Click += (_, _) =>
        {
            if (_sheetWindow!.IsVisible) _sheetWindow.Hide();
            else _sheetWindow.Show();
        };

        var settingsItem = new System.Windows.Controls.MenuItem { Header = Loc.SettingsMenu };
        settingsItem.Click += (_, _) =>
        {
            var settingsWindow = new SettingsWindow(_settingsService!);
            settingsWindow.Topmost = true;
            settingsWindow.ShowDialog();
        };

        var exitItem = new System.Windows.Controls.MenuItem { Header = Loc.Exit };
        exitItem.Click += (_, _) =>
        {
            _settingsService!.Save();
            _trayIcon?.Dispose();
            Shutdown();
        };

        menu.Items.Add(toggleItem);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(settingsItem);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(exitItem);

        _trayIcon.ContextMenu = menu;
        _trayIcon.TrayMouseDoubleClick += (_, _) =>
        {
            var settingsWindow = new SettingsWindow(_settingsService!);
            settingsWindow.Topmost = true;
            settingsWindow.ShowDialog();
        };
        _trayIcon.TrayLeftMouseUp += (_, _) =>
        {
            if (_sheetWindow!.IsVisible) _sheetWindow.Hide();
            else _sheetWindow.Show();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyService?.Dispose();
        _trayIcon?.Dispose();
        _settingsService?.Save();
        base.OnExit(e);
    }
}
