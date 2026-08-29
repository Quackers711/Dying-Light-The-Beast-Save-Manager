using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using DLBeastSaveManager.Services;
using DLBeastSaveManager.ViewModels;

using RadioButton = System.Windows.Controls.RadioButton;

namespace DLBeastSaveManager.Views;

public partial class RestoreConfirmDialog : ThemedWindow
{
    private readonly MainViewModel _vm;
    private bool _gameOverridden;

    public RestoreConfirmDialog(MainViewModel vm, SnapshotViewModel snapshot)
    {
        InitializeComponent();

        _vm = vm;

        Icon = AppIcons.CreateWindowIcon(AppIcons.Attention);

        var label = string.IsNullOrWhiteSpace(snapshot.Label) ? string.Empty : $" - \"{snapshot.Label}\"";
        SummaryText.Text =
            $"{snapshot.WhenText} ({snapshot.AgeText}){label}\n" +
            $"{snapshot.FilesText} files, {snapshot.SizeText}.";

        BuildScopeOptions(snapshot);
        RefreshGates();
    }

    public RestoreOptions? Options { get; private set; }

    private void BuildScopeOptions(SnapshotViewModel snapshot)
    {
        var runs = SaveRuns.Group(snapshot.Snapshot.Files.Select(f => f.Path));
        if (runs.Count < 2) return;

        var preferred = snapshot.Change.Changed.Count == 1 ? snapshot.Change.Changed[0] : null;

        foreach (var run in runs)
        {
            var info = snapshot.Snapshot.Runs.FirstOrDefault(r => r.Key == run.Key);
            var detail = string.Join(", ", new[] { info?.Difficulty, info?.Area }
                .Where(p => !string.IsNullOrWhiteSpace(p)));

            ScopeOptions.Children.Add(new RadioButton
            {
                Content = detail.Length == 0
                    ? $"{run.DisplayName} only"
                    : $"{run.DisplayName} only  ({detail})",
                Tag = run.Key,
                IsChecked = run.Key == preferred
            });
        }

        ScopeOptions.Children.Add(new RadioButton
        {
            Content = "The whole save folder  (every run)",
            Tag = null,
            IsChecked = preferred is null
        });

        ScopePanel.Visibility = Visibility.Visible;
    }

    private string? SelectedRunKey => ScopeOptions.Children
        .OfType<RadioButton>()
        .FirstOrDefault(r => r.IsChecked == true)?.Tag as string;

    private void RefreshGates()
    {
        var gameRunning = _vm.CheckGameRunningNow();
        GameWarning.Visibility = gameRunning ? Visibility.Visible : Visibility.Collapsed;

        var cloud = _vm.CloudReport;
        CloudWarning.Visibility = cloud.NeedsWarning ? Visibility.Visible : Visibility.Collapsed;
        CloudHeader.Text = cloud.Detail;
        CloudHowTo.Text = SteamCloudInspector.HowToDisable;

        RestoreButton.IsEnabled = !gameRunning || _gameOverridden;
        RestoreButton.Content = gameRunning && _gameOverridden ? "Restore anyway" : "Restore";
    }

    private void OnRecheckGame(object sender, RoutedEventArgs e)
    {
        _gameOverridden = false;
        RefreshGates();

        if (GameWarning.Visibility == Visibility.Collapsed) return;
        MessageWindow.Show(this, "Restore save", "The game is still running.");
    }

    private void OnOverrideGame(object sender, RoutedEventArgs e)
    {
        var proceed = MessageWindow.Ask(this, "Game is running",
            "Restoring while the game is running will almost certainly be undone the next time it saves.\n\n" +
            "Continue anyway?");

        if (!proceed) return;

        _gameOverridden = true;
        RefreshGates();
    }

    private void OnRecheckCloud(object sender, RoutedEventArgs e)
    {
        _vm.RefreshCloudReport();
        RefreshGates();

        if (!_vm.CloudReport.NeedsWarning) return;
        MessageWindow.Show(this, "Steam Cloud",
            "Steam still reports Cloud sync as on for this game. It may need a restart before the " +
            "change shows up here.");
    }

    private void OnOpenSteam(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo($"steam://nav/games/details/{SteamLocator.AppId}")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageWindow.Show(this, "Steam", $"Could not open Steam: {ex.Message}");
        }
    }

    private void OnRestore(object sender, RoutedEventArgs e)
    {
        Options = new RestoreOptions
        {
            TakeSafetySnapshot = true,
            KeepReplacedFilesInTrash = true,
            StampCurrentTimestamps = true,
            ResetSteamCloudCache = ResetCacheCheck.IsChecked == true,
            RemoteCachePath = _vm.Location?.RemoteCachePath,
            RunKey = SelectedRunKey
        };

        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
