using System.ComponentModel;
using System.Windows;
using DLBeastSaveManager.Models;
using DLBeastSaveManager.Services;
using DLBeastSaveManager.ViewModels;

namespace DLBeastSaveManager.Views;

public partial class MainWindow : ThemedWindow
{
    private readonly MainViewModel _vm;
    private readonly TrayService _tray = new();
    private readonly HotkeyService _hotkeys = new();
    private readonly bool _startMinimized;

    private bool _reallyExiting;

    public MainWindow(bool startMinimized = false)
    {
        InitializeComponent();

        _startMinimized = startMinimized;
        Icon = AppIcons.CreateWindowIcon(AppIcons.Watching);

        var settings = SettingsStore.Load();
        _vm = new MainViewModel(settings, Dispatcher);
        DataContext = _vm;

        _vm.ConfirmRestore = ShowRestoreDialogAsync;
        _vm.PromptForText = (title, prompt, initial) => TextPromptWindow.Prompt(this, title, prompt, initial);
        _vm.Confirm = (title, question) => MessageWindow.Ask(this, title, question);
        _vm.Notify = (title, message, warning) => _tray.Notify(title, message, warning);
        _vm.PropertyChanged += (_, _) => UpdateTray();

        _tray.ShowRequested += (_, _) => ShowFromTray();
        _tray.BackupRequested += async (_, _) => await _vm.TakeBackupAsync(SnapshotTrigger.Manual, force: true);
        _tray.ToggleWatchRequested += (_, _) => _vm.ToggleWatchCommand.Execute(null);
        _tray.ExitRequested += (_, _) => ExitApplication();

        _hotkeys.HotkeyPressed += OnHotkeyPressed;
    }

    public void Launch()
    {
        Show();
        _hotkeys.Attach(this);
        _vm.Initialize();
        ApplyHotkeys(_vm.Settings);
        UpdateTray();

        if (_startMinimized) HideToTray(announce: false);
    }

    private void ApplyHotkeys(AppSettings settings)
    {
        var problems = _hotkeys.Apply(settings);
        if (problems.Count == 0) return;

        var elevationHint = GameProcessMonitor.IsThisProcessElevated()
            ? string.Empty
            : "\n\nIf the game runs as administrator, run this tool as administrator too.";

        MessageWindow.Show(this, "Hotkeys",
            "Some hotkeys could not be registered - another program is probably using them:\n\n" +
            string.Join("\n", problems) + elevationHint);
    }

    private async void OnHotkeyPressed(object? sender, HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.BackupNow:
                await _vm.TakeBackupAsync(SnapshotTrigger.Hotkey, force: true);
                break;

            case HotkeyAction.PinLatest:
                _vm.PinNewest();
                break;
        }
    }

    private Task<RestoreOptions?> ShowRestoreDialogAsync(SnapshotViewModel snapshot)
    {
        var dialog = new RestoreConfirmDialog(_vm, snapshot) { Owner = this };
        var confirmed = dialog.ShowDialog() == true;
        return Task.FromResult(confirmed ? dialog.Options : null);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_vm.Settings.Clone()) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        _vm.ApplySettings(dialog.Result);
        ApplyHotkeys(dialog.Result);
        UpdateTray();
    }

    private void OnGridDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_vm.Selected is not null && _vm.RestoreCommand.CanExecute(null))
            _vm.RestoreCommand.Execute(null);
    }

    private void UpdateTray()
    {
        var state = _vm.State switch
        {
            ProtectionState.Protected => TrayState.Watching,
            ProtectionState.Warning => TrayState.Attention,
            _ => TrayState.Idle
        };

        var what = _vm.IsWatching
            ? (_vm.IsGameRunning ? "watching, game running" : "watching")
            : "not watching";

        _tray.SetState(state, $"DL:TB Save Manager - {what}");
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void HideToTray(bool announce)
    {
        Hide();
        if (announce)
            _tray.Notify("Still running",
                "Backups continue in the background. Double-click the tray icon to reopen.", false);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_reallyExiting && _vm.Settings.MinimizeToTray)
        {
            e.Cancel = true;
            HideToTray(announce: true);
            return;
        }

        base.OnClosing(e);
    }

    private void ExitApplication()
    {
        _reallyExiting = true;
        _hotkeys.Dispose();
        _vm.Dispose();
        _tray.Dispose();
        Close();
        Application.Current.Shutdown();
    }
}
