using DLBeastSaveManager.Models;

namespace DLBeastSaveManager.Services;

public static class RetentionPolicy
{
    public static IReadOnlyList<Snapshot> SelectForDeletion(
        IEnumerable<Snapshot> snapshots,
        AppSettings settings,
        DateTime nowUtc)
    {
        var ordered = snapshots.OrderByDescending(s => s.CreatedUtc).ToList();
        if (settings.KeepEverything) return Array.Empty<Snapshot>();

        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var s in ordered.Where(s => s.Pinned))
            keep.Add(s.Id);

        if (settings.KeepLastCount > 0)
            foreach (var s in ordered.Take(settings.KeepLastCount))
                keep.Add(s.Id);

        KeepNewestPerBucket(
            ordered, keep,
            cutoff: nowUtc.AddHours(-Math.Max(0, settings.KeepHourlyForHours)),
            enabled: settings.KeepHourlyForHours > 0,
            bucketOf: t => new DateTime(t.Year, t.Month, t.Day, t.Hour, 0, 0, DateTimeKind.Utc));

        KeepNewestPerBucket(
            ordered, keep,
            cutoff: nowUtc.AddDays(-Math.Max(0, settings.KeepDailyForDays)),
            enabled: settings.KeepDailyForDays > 0,
            bucketOf: t => t.Date);

        return ordered
            .Where(s => !keep.Contains(s.Id))
            .OrderBy(s => s.CreatedUtc)
            .ToList();
    }

    private static void KeepNewestPerBucket(
        List<Snapshot> ordered,
        HashSet<string> keep,
        DateTime cutoff,
        bool enabled,
        Func<DateTime, DateTime> bucketOf)
    {
        if (!enabled) return;

        var seen = new HashSet<DateTime>();
        foreach (var s in ordered)
        {
            if (s.CreatedUtc < cutoff) break;
            if (seen.Add(bucketOf(s.CreatedUtc))) keep.Add(s.Id);
        }
    }

    public static string Describe(AppSettings s)
    {
        if (s.KeepEverything) return "Keeping every snapshot (no pruning).";

        var parts = new List<string>();
        if (s.KeepLastCount > 0) parts.Add($"the last {s.KeepLastCount}");
        if (s.KeepHourlyForHours > 0) parts.Add($"one per hour for {s.KeepHourlyForHours}h");
        if (s.KeepDailyForDays > 0) parts.Add($"one per day for {s.KeepDailyForDays}d");
        if (parts.Count == 0) return "Nothing is kept automatically - only pinned snapshots survive.";
        return "Keeping " + string.Join(", ", parts) + ", plus every pinned snapshot.";
    }
}
