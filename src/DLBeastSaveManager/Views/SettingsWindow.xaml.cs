using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DLBeastSaveManager.Models;
using DLBeastSaveManager.Services;
using Microsoft.Win32;

namespace DLBeastSaveManager.Views;

public partial class SettingsWindow : ThemedWindow
{
    private readonly AppSettings _settings;
    private IReadOnlyList<SaveLocation> _locations = Array.Empty<SaveLocation>();
    private bool _loading;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();

        _settings = settings;
        Icon = AppIcons.CreateWindowIcon(AppIcons.Idle);

        Load();
    }

    public AppSettings Result => _settings;

    private void Load()
    {
        _loading = true;

        _locations = SteamLocator.FindAll();
        LocationCombo.Items.Clear();
        foreach (var location in _locations)
            LocationCombo.Items.Add($"{location.DisplayName} - {location.SaveFileCount} files");
        LocationCombo.Items.Add("Custom folder...");

        var resolved = SteamLocator.Resolve(_settings);
        SavePathBox.Text = resolved?.SavePath ?? string.Empty;
        var index = resolved is null
            ? -1
            : _locations.ToList().FindIndex(l => SteamLocator.PathsEqual(l.SavePath, resolved.SavePath));
        LocationCombo.SelectedIndex = index >= 0 ? index : LocationCombo.Items.Count - 1;

        BackupPathBox.Text = SettingsStore.ResolveBackupRoot(_settings);

        AutoBackupCheck.IsChecked = _settings.AutoBackupEnabled;

        KeepEverythingCheck.IsChecked = _settings.KeepEverything;
        KeepLastBox.Text = _settings.KeepLastCount.ToString();
        KeepRow.IsEnabled = !_settings.KeepEverything;

        HotkeysCheck.IsChecked = _settings.HotkeysEnabled;
        BackupHotkeyBox.Text = _settings.BackupNowHotkey.Describe();
        PinHotkeyBox.Text = _settings.PinLatestHotkey.Describe();

        MinimizeToTrayCheck.IsChecked = _settings.MinimizeToTray;
        StartWithWindowsCheck.IsChecked = SettingsStore.IsStartWithWindowsEnabled();

        _loading = false;
        DescribeSavePath();
    }

    private void OnLocationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;

        var index = LocationCombo.SelectedIndex;
        if (index >= 0 && index < _locations.Count) SavePathBox.Text = _locations[index].SavePath;
    }

    private void OnSavePathChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading) DescribeSavePath();
    }

    private void OnBrowseSave(object sender, RoutedEventArgs e)
    {
        var picked = PickFolder("Choose the folder holding the .sav files", SavePathBox.Text);
        if (picked is null) return;

        SavePathBox.Text = picked;
        LocationCombo.SelectedIndex = LocationCombo.Items.Count - 1;
    }

    private void OnBrowseBackup(object sender, RoutedEventArgs e)
    {
        var picked = PickFolder("Choose where snapshots are stored", BackupPathBox.Text);
        if (picked is not null) BackupPathBox.Text = picked;
    }

    private void DescribeSavePath()
    {
        var path = SavePathBox.Text.Trim();
        if (path.Length == 0)
        {
            SavePathStatus.Text = "No folder set.";
            return;
        }

        if (!Directory.Exists(path))
        {
            SavePathStatus.Text = "This folder does not exist.";
            return;
        }

        var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
        var saves = files.Count(f => f.EndsWith(".sav", StringComparison.OrdinalIgnoreCase));

        SavePathStatus.Text = files.Length == 0
            ? "Folder is empty - is this the right one?"
            : $"{files.Length} files, {saves} of them .sav.";
    }

    private static string? PickFolder(string title, string? initial)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial))
            dialog.InitialDirectory = initial;

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private void OnRetentionChanged(object sender, RoutedEventArgs e)
    {
        if (KeepRow is not null) KeepRow.IsEnabled = KeepEverythingCheck.IsChecked != true;
    }

    private void OnHotkeyCapture(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (sender is not TextBox box) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Tab) { e.Handled = false; return; }
        if (key == Key.Escape) { SetBinding(box, new HotkeyBinding()); return; }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;

        SetBinding(box, new HotkeyBinding { Key = key, Modifiers = Keyboard.Modifiers });
    }

    private void OnClearHotkey(object sender, RoutedEventArgs e)
    {
        var tag = (sender as FrameworkElement)?.Tag as string;
        var box = tag switch
        {
            "Backup" => BackupHotkeyBox,
            "Pin" => PinHotkeyBox,
            _ => null
        };

        if (box is not null) SetBinding(box, new HotkeyBinding());
    }

    private void SetBinding(TextBox box, HotkeyBinding binding)
    {
        switch (box.Tag as string)
        {
            case "Backup": _settings.BackupNowHotkey = binding; break;
            case "Pin": _settings.PinLatestHotkey = binding; break;
        }

        box.Text = binding.Describe();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var savePath = SavePathBox.Text.Trim();
        if (savePath.Length > 0 && !Directory.Exists(savePath))
        {
            MessageWindow.Show(this, "Settings",
                "That save folder does not exist. Fix the path or pick another one.");
            return;
        }

        var backupPath = BackupPathBox.Text.Trim();
        if (backupPath.Length == 0) backupPath = BackupService.DefaultBackupRoot;

        if (SteamLocator.PathsEqual(savePath, backupPath) ||
            (savePath.Length > 0 && backupPath.StartsWith(savePath, StringComparison.OrdinalIgnoreCase)))
        {
            MessageWindow.Show(this, "Settings",
                "The backup folder cannot be inside the save folder - a restore would wipe your snapshots.");
            return;
        }

        try
        {
            Directory.CreateDirectory(backupPath);
        }
        catch (Exception ex)
        {
            MessageWindow.Show(this, "Settings", $"Cannot use that backup folder: {ex.Message}");
            return;
        }

        var detected = _locations.FirstOrDefault(l => SteamLocator.PathsEqual(l.SavePath, savePath));
        _settings.PreferredSteamUserId = detected?.SteamUserId;
        _settings.SavePathOverride = detected is null && savePath.Length > 0 ? savePath : null;

        _settings.BackupRootOverride =
            SteamLocator.PathsEqual(backupPath, BackupService.DefaultBackupRoot) ? null : backupPath;

        _settings.AutoBackupEnabled = AutoBackupCheck.IsChecked == true;

        _settings.KeepEverything = KeepEverythingCheck.IsChecked == true;
        _settings.KeepLastCount = ParseInt(KeepLastBox.Text, _settings.KeepLastCount, 1, 100000);

        _settings.HotkeysEnabled = HotkeysCheck.IsChecked == true;
        _settings.MinimizeToTray = MinimizeToTrayCheck.IsChecked == true;

        var wantStartup = StartWithWindowsCheck.IsChecked == true;
        if (wantStartup != SettingsStore.IsStartWithWindowsEnabled() &&
            !SettingsStore.SetStartWithWindows(wantStartup))
        {
            MessageWindow.Show(this, "Settings",
                "Could not change the Windows startup entry. Everything else was saved.");
        }

        _settings.StartWithWindows = SettingsStore.IsStartWithWindowsEnabled();

        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private static int ParseInt(string text, int fallback, int min, int max) =>
        int.TryParse(text.Trim(), out var value) ? Math.Clamp(value, min, max) : fallback;
}
