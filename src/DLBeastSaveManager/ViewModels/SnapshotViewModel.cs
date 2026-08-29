using DLBeastSaveManager.Models;
using DLBeastSaveManager.Services;

namespace DLBeastSaveManager.ViewModels;

public sealed class SnapshotViewModel : ObservableObject
{
    public SnapshotViewModel(Snapshot snapshot, SnapshotIndex.RunChange? change = null)
    {
        Snapshot = snapshot;
        Change = change ?? SnapshotIndex.RunChange.None;
    }

    public Snapshot Snapshot { get; }

    public SnapshotIndex.RunChange Change { get; }

    public string Id => Snapshot.Id;

    public string TimeText => Snapshot.CreatedLocal.ToString("HH:mm:ss");

    public string WhenText => Snapshot.CreatedLocal.ToString("ddd d MMM, HH:mm:ss");

    public string AgeText => FormatAge(DateTime.UtcNow - Snapshot.CreatedUtc);

    public string Label
    {
        get => Snapshot.Label ?? string.Empty;
        set
        {
            Snapshot.Label = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            OnPropertyChanged();
        }
    }

    public bool Pinned
    {
        get => Snapshot.Pinned;
        set
        {
            if (Snapshot.Pinned == value) return;
            Snapshot.Pinned = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PinGlyph));
        }
    }

    public string PinGlyph => Snapshot.Pinned ? "★" : string.Empty;

    public string RunText
    {
        get
        {
            if (Change.Removed.Count > 0 && Change.Changed.Count == 0)
                return string.Join(", ", Change.Removed.Select(SaveRuns.NameFor)) + " deleted";

            if (Change.Changed.Count == 0) return string.Empty;
            if (Change.Changed.Count > 1)
                return string.Join(", ", Change.Changed.Select(SaveRuns.NameFor));

            var key = Change.Changed[0];
            var run = Snapshot.Runs.FirstOrDefault(r => r.Key == key);

            var detail = Describe(run, withCheckpoint: false);

            return detail.Length == 0 ? SaveRuns.NameFor(key) : $"{SaveRuns.NameFor(key)} - {detail}";
        }
    }

    public string RunTooltip
    {
        get
        {
            var lines = Snapshot.Runs
                .Select(run =>
                {
                    var detail = Describe(run, withCheckpoint: true);
                    var files = Snapshot.Files.Count(f => SaveRuns.KeyFor(f.Path) == run.Key);
                    var suffix = detail.Length == 0 ? string.Empty : $" - {detail}";
                    return $"{SaveRuns.NameFor(run.Key)}{suffix} ({files} file{(files == 1 ? "" : "s")})";
                })
                .ToList();

            if (Change.Removed.Count > 0)
                lines.Add("Gone since the previous snapshot: " +
                          string.Join(", ", Change.Removed.Select(SaveRuns.NameFor)));

            return lines.Count == 0
                ? "No file details recorded."
                : string.Join(Environment.NewLine, lines);
        }
    }

    private static string Describe(RunInfo? run, bool withCheckpoint)
    {
        if (run is null) return string.Empty;

        var parts = withCheckpoint
            ? new[] { run.Difficulty, run.Area, run.Checkpoint }
            : new[] { run.Difficulty, run.Area };

        return string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    public string SizeText => BackupService.FormatSize(Snapshot.SizeBytes);

    public string FilesText => Snapshot.FileCount.ToString();

    public void RefreshAge() => OnPropertyChanged(nameof(AgeText));

    public static string FormatAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;
        if (age.TotalSeconds < 60) return $"{(int)age.TotalSeconds}s ago";
        if (age.TotalMinutes < 60) return $"{(int)age.TotalMinutes}m ago";
        if (age.TotalHours < 24) return $"{(int)age.TotalHours}h {age.Minutes}m ago";
        return $"{(int)age.TotalDays}d ago";
    }
}
