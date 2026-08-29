using System.Timers;
using DLBeastSaveManager.Models;
using Timer = System.Timers.Timer;

namespace DLBeastSaveManager.Services;

public sealed class BackupRequestedEventArgs : EventArgs
{
    public required SnapshotTrigger Trigger { get; init; }
}

public sealed class SaveWatcher : IDisposable
{
    private readonly object _sync = new();

    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private Timer? _intervalTimer;

    private string _savePath = string.Empty;
    private int _debounceSeconds = 3;
    private int _intervalMinutes = 5;

    public event EventHandler<BackupRequestedEventArgs>? BackupRequested;

    public event EventHandler<string>? WatchError;

    public bool IsWatching { get; private set; }

    public string SavePath => _savePath;

    public void Start(string savePath, AppSettings settings)
    {
        lock (_sync)
        {
            StopCore();

            _savePath = savePath;
            _debounceSeconds = Math.Max(1, settings.DebounceSeconds);
            _intervalMinutes = Math.Max(0, settings.IntervalMinutes);

            if (!Directory.Exists(savePath))
            {
                WatchError?.Invoke(this, $"Save folder not found: {savePath}");
                return;
            }

            _debounceTimer = new Timer(_debounceSeconds * 1000) { AutoReset = false };
            _debounceTimer.Elapsed += OnDebounceElapsed;

            _watcher = new FileSystemWatcher(savePath)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024
            };

            _watcher.Changed += OnFileEvent;
            _watcher.Created += OnFileEvent;
            _watcher.Deleted += OnFileEvent;
            _watcher.Renamed += OnFileEvent;
            _watcher.Error += OnWatcherError;
            _watcher.EnableRaisingEvents = true;

            if (_intervalMinutes > 0)
            {
                _intervalTimer = new Timer(_intervalMinutes * 60_000) { AutoReset = true };
                _intervalTimer.Elapsed += OnIntervalElapsed;
                _intervalTimer.Start();
            }

            IsWatching = true;
        }
    }

    public void Stop()
    {
        lock (_sync) StopCore();
    }

    private void StopCore()
    {
        IsWatching = false;

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnFileEvent;
            _watcher.Created -= OnFileEvent;
            _watcher.Deleted -= OnFileEvent;
            _watcher.Renamed -= OnFileEvent;
            _watcher.Error -= OnWatcherError;
            _watcher.Dispose();
            _watcher = null;
        }

        if (_debounceTimer is not null)
        {
            _debounceTimer.Elapsed -= OnDebounceElapsed;
            _debounceTimer.Dispose();
            _debounceTimer = null;
        }

        if (_intervalTimer is not null)
        {
            _intervalTimer.Elapsed -= OnIntervalElapsed;
            _intervalTimer.Dispose();
            _intervalTimer = null;
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (e.Name is not null && e.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) return;

        lock (_sync)
        {
            if (_debounceTimer is null) return;
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }

    private void OnDebounceElapsed(object? sender, ElapsedEventArgs e) =>
        BackupRequested?.Invoke(this, new BackupRequestedEventArgs { Trigger = SnapshotTrigger.Auto });

    private void OnIntervalElapsed(object? sender, ElapsedEventArgs e) =>
        BackupRequested?.Invoke(this, new BackupRequestedEventArgs { Trigger = SnapshotTrigger.Interval });

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        WatchError?.Invoke(this, e.GetException().Message);

        lock (_sync)
        {
            if (!IsWatching || _savePath.Length == 0) return;
            var settings = new AppSettings
            {
                DebounceSeconds = _debounceSeconds,
                IntervalMinutes = _intervalMinutes
            };
            var path = _savePath;
            Task.Run(() => Start(path, settings));
        }
    }

    public void Dispose() => Stop();
}
