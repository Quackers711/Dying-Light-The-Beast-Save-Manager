using System.Collections.ObjectModel;
using System.Windows.Threading;
using DLBeastSaveManager.Models;
using DLBeastSaveManager.Services;

namespace DLBeastSaveManager.ViewModels;

public enum ProtectionState
{
    Protected,

    Warning,

    Idle
}

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _ageTimer;
    private readonly SaveWatcher _watcher = new();
    private readonly GameProcessMonitor _gameMonitor = new();

    private AppSettings _settings;
    private SaveLocation? _location;
    private BackupService? _backups;
    private CloudReport _cloud = new(CloudStatus.NotApplicable, string.Empty);

    private SnapshotViewModel? _selected;
    private string _statusMessage = "Starting up...";
    private bool _isBusy;

    public MainViewModel(AppSettings settings, Dispatcher dispatcher)
    {
        _settings = settings;
        _dispatcher = dispatcher;

        BackupNowCommand = new AsyncRelayCommand(() => TakeBackupAsync(SnapshotTrigger.Manual, force: true));
        RestoreCommand = new AsyncRelayCommand(RestoreSelectedAsync, () => Selected is not null);
        TogglePinCommand = new RelayCommand(TogglePinSelected, () => Selected is not null);
        RenameCommand = new RelayCommand(RenameSelected, () => Selected is not null);
        DeleteCommand = new RelayCommand(DeleteSelected, () => Selected is not null);
        ToggleWatchCommand = new RelayCommand(ToggleWatching);

        _watcher.BackupRequested += OnBackupRequested;
        _watcher.WatchError += OnWatchError;
        _gameMonitor.GameStarted += OnGameStarted;
        _gameMonitor.GameExited += OnGameExited;

        _ageTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _ageTimer.Tick += (_, _) => RefreshAges();
    }

    public Func<SnapshotViewModel, Task<RestoreOptions?>>? ConfirmRestore { get; set; }

    public Func<string, string, string?, string?>? PromptForText { get; set; }

    public Func<string, string, bool>? Confirm { get; set; }

    public Action<string, string, bool>? Notify { get; set; }

    public ObservableCollection<SnapshotViewModel> Snapshots { get; } = new();

    public AppSettings Settings => _settings;

    public SaveLocation? Location => _location;

    public SnapshotViewModel? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            RestoreCommand.RaiseCanExecuteChanged();
            TogglePinCommand.RaiseCanExecuteChanged();
            RenameCommand.RaiseCanExecuteChanged();
            DeleteCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(PinButtonText));
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsWatching => _watcher.IsWatching;

    public string WatchButtonText => IsWatching ? "Stop watching" : "Start watching";

    public string PinButtonText => Selected?.Pinned == true ? "Unpin" : "Pin";

    public string SavePathText => _location?.SavePath ?? "No save folder found - set one in Settings";

    public bool IsGameRunning => _gameMonitor.IsGameRunning;

    public string StatusText
    {
        get
        {
            if (_location is null || !_location.Exists) return "No save folder";
            if (!IsWatching) return "Not watching";

            var newest = Snapshots.FirstOrDefault();
            var last = newest is null ? "no backups yet" : $"last backup {newest.AgeText}";
            return IsGameRunning ? $"Watching, game running - {last}" : $"Watching - {last}";
        }
    }

    public string CloudWarningText => _cloud.NeedsWarning
        ? "Steam Cloud is on for this game - turn it off before restoring."
        : string.Empty;

    public string CountsText
    {
        get
        {
            if (_backups is null) return string.Empty;
            var size = BackupService.FormatSize(_backups.TotalBackupBytes());
            return $"{Snapshots.Count} snapshot{(Snapshots.Count == 1 ? "" : "s")}, {size}";
        }
    }

    public ProtectionState State
    {
        get
        {
            if (_location is null || !_location.Exists) return ProtectionState.Warning;
            if (!IsWatching) return ProtectionState.Idle;

            var newest = Snapshots.FirstOrDefault();
            if (newest is null) return ProtectionState.Warning;

            var limit = TimeSpan.FromMinutes(Math.Max(_settings.IntervalMinutes, 1) * 2 + 1);
            var stale = DateTime.UtcNow - newest.Snapshot.CreatedUtc > limit;
            return stale && IsGameRunning ? ProtectionState.Warning : ProtectionState.Protected;
        }
    }

    public AsyncRelayCommand BackupNowCommand { get; }
    public AsyncRelayCommand RestoreCommand { get; }
    public RelayCommand TogglePinCommand { get; }
    public RelayCommand RenameCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand ToggleWatchCommand { get; }

    public void Initialize()
    {
        ApplyLocationAndBackups();
        _gameMonitor.Start();
        _ageTimer.Start();

        if (_settings.AutoBackupEnabled && _location is { Exists: true }) StartWatching();
        else if (_location is null) StatusMessage = "Could not find a save folder. Set one in Settings.";

        RefreshHeader();
    }

    private void ApplyLocationAndBackups()
    {
        _location = SteamLocator.Resolve(_settings);
        _cloud = SteamCloudInspector.Inspect(_location);

        var backupRoot = SettingsStore.ResolveBackupRoot(_settings);
        var savePath = _location?.SavePath ?? string.Empty;

        if (_backups is null) _backups = new BackupService(savePath, backupRoot);
        else _backups.Retarget(savePath, backupRoot);

        ReloadSnapshots();
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        SettingsStore.Save(settings);

        StopWatching();
        ApplyLocationAndBackups();

        if (settings.AutoBackupEnabled) StartWatching();

        RefreshHeader();
    }

    public void ReloadSnapshots()
    {
        if (_backups is null) return;

        _backups.ReloadIndex();
        var selectedId = Selected?.Id;

        var changes = _backups.Index.RunChanges();

        Snapshots.Clear();
        foreach (var snapshot in _backups.Index.Snapshots)
            Snapshots.Add(new SnapshotViewModel(
                snapshot, changes.GetValueOrDefault(snapshot.Id, SnapshotIndex.RunChange.None)));

        Selected = Snapshots.FirstOrDefault(s => s.Id == selectedId) ?? Snapshots.FirstOrDefault();
        RefreshHeader();
    }

    public void StartWatching()
    {
        if (_location is null || !_location.Exists)
        {
            StatusMessage = "Cannot watch - no save folder.";
            return;
        }

        _watcher.Start(_location.SavePath, _settings);
        StatusMessage = _watcher.IsWatching ? "Watching for save changes." : "Could not start watching.";
        RefreshHeader();
    }

    public void StopWatching()
    {
        _watcher.Stop();
        RefreshHeader();
    }

    private void ToggleWatching()
    {
        if (IsWatching) { StopWatching(); StatusMessage = "Watching stopped."; }
        else StartWatching();
    }

    private void OnBackupRequested(object? sender, BackupRequestedEventArgs e) =>
        _dispatcher.InvokeAsync(async () => await TakeBackupAsync(e.Trigger, force: false));

    private void OnWatchError(object? sender, string message) =>
        _dispatcher.InvokeAsync(() =>
        {
            StatusMessage = $"Watch problem: {message}";
            RefreshHeader();
        });

    private void OnGameStarted(object? sender, EventArgs e) =>
        _dispatcher.InvokeAsync(() =>
        {
            StatusMessage = "Game started.";
            if (_settings.AutoBackupEnabled && !IsWatching) StartWatching();
            RefreshHeader();
        });

    private void OnGameExited(object? sender, EventArgs e) =>
        _dispatcher.InvokeAsync(async () =>
        {
            StatusMessage = "Game exited.";
            await TakeBackupAsync(SnapshotTrigger.GameExit, force: false);
            RefreshHeader();
        });

    public async Task TakeBackupAsync(
        SnapshotTrigger trigger, bool force, string? label = null, bool pinned = false)
    {
        if (_backups is null || _location is null) return;

        IsBusy = true;
        try
        {
            var result = await _backups.CreateSnapshotAsync(trigger, _settings, label, pinned, force);

            switch (result.Outcome)
            {
                case BackupOutcome.Created:
                    ReloadSnapshots();
                    Selected = Snapshots.FirstOrDefault();
                    StatusMessage = result.Message;
                    if (trigger is SnapshotTrigger.Hotkey)
                        Notify?.Invoke("Backup taken", result.Message, false);
                    break;

                case BackupOutcome.SkippedUnchanged:
                case BackupOutcome.SkippedNoSaveFiles:
                    StatusMessage = result.Message;
                    break;

                default:
                    StatusMessage = result.Message;
                    Notify?.Invoke("Backup failed", result.Message, true);
                    break;
            }
        }
        finally
        {
            IsBusy = false;
            RefreshHeader();
        }
    }

    private async Task RestoreSelectedAsync()
    {
        if (_backups is null || Selected is null || ConfirmRestore is null) return;

        var options = await ConfirmRestore(Selected);
        if (options is null) return;

        IsBusy = true;
        try
        {
            var result = await _backups.RestoreAsync(Selected.Snapshot, _settings, options);
            ReloadSnapshots();

            StatusMessage = result.Message;
            Notify?.Invoke(result.Success ? "Save restored" : "Restore failed", result.Message, !result.Success);
        }
        finally
        {
            IsBusy = false;
            RefreshHeader();
        }
    }

    private void TogglePinSelected()
    {
        if (_backups is null || Selected is null) return;

        var pinned = !Selected.Pinned;
        _backups.SetPinned(Selected.Snapshot, pinned);
        Selected.Pinned = pinned;
        OnPropertyChanged(nameof(PinButtonText));
        StatusMessage = pinned ? "Pinned - this snapshot will never be auto-deleted." : "Unpinned.";
    }

    public void PinNewest()
    {
        if (_backups is null) return;

        var newest = Snapshots.FirstOrDefault();
        if (newest is null)
        {
            Notify?.Invoke("Nothing to pin", "There are no snapshots yet.", true);
            return;
        }

        _backups.SetPinned(newest.Snapshot, true);
        newest.Pinned = true;
        StatusMessage = $"Pinned the {newest.TimeText} snapshot.";
        Notify?.Invoke("Pinned", $"Safe point set at {newest.TimeText}.", false);
    }

    private void RenameSelected()
    {
        if (_backups is null || Selected is null || PromptForText is null) return;

        var name = PromptForText("Name this snapshot", "A label makes it easy to find later.", Selected.Label);
        if (name is null) return;

        _backups.SetLabel(Selected.Snapshot, name);
        Selected.Label = name;
        StatusMessage = "Snapshot renamed.";
    }

    private void DeleteSelected()
    {
        if (_backups is null || Selected is null || Confirm is null) return;

        var snapshot = Selected;
        if (!Confirm("Delete snapshot", $"Delete the snapshot from {snapshot.TimeText}?")) return;

        _backups.Delete(snapshot.Snapshot);
        ReloadSnapshots();
        StatusMessage = "Snapshot deleted.";
    }

    private void RefreshAges()
    {
        foreach (var snapshot in Snapshots) snapshot.RefreshAge();
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(State));
    }

    public void RefreshHeader()
    {
        OnPropertyChanged(nameof(IsWatching));
        OnPropertyChanged(nameof(WatchButtonText));
        OnPropertyChanged(nameof(SavePathText));
        OnPropertyChanged(nameof(IsGameRunning));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CloudWarningText));
        OnPropertyChanged(nameof(CountsText));
        OnPropertyChanged(nameof(State));
    }

    public bool CheckGameRunningNow()
    {
        var running = _gameMonitor.CheckNow();
        OnPropertyChanged(nameof(IsGameRunning));
        OnPropertyChanged(nameof(StatusText));
        return running;
    }

    public CloudReport CloudReport => _cloud;

    public void RefreshCloudReport()
    {
        _cloud = SteamCloudInspector.Inspect(_location);
        RefreshHeader();
    }

    public void Dispose()
    {
        _ageTimer.Stop();
        _watcher.BackupRequested -= OnBackupRequested;
        _watcher.WatchError -= OnWatchError;
        _gameMonitor.GameStarted -= OnGameStarted;
        _gameMonitor.GameExited -= OnGameExited;
        _watcher.Dispose();
        _gameMonitor.Dispose();
    }
}
