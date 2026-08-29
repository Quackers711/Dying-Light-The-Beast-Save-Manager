using DLBeastSaveManager.Models;
using DLBeastSaveManager.Services;

namespace DLBeastSaveManager.Tests;

public class RetentionPolicyTests
{
    private static readonly DateTime Now = new(2026, 7, 24, 12, 0, 0, DateTimeKind.Utc);

    private static Snapshot At(TimeSpan ago, bool pinned = false) => new()
    {
        Id = $"s{ago.TotalSeconds:0}",
        CreatedUtc = Now - ago,
        Pinned = pinned,
        SetHash = Guid.NewGuid().ToString("N")
    };

    private static AppSettings Policy(int last = 30, int hours = 24, int days = 7) => new()
    {
        KeepEverything = false,
        KeepLastCount = last,
        KeepHourlyForHours = hours,
        KeepDailyForDays = days
    };

    [Fact]
    public void Keep_everything_deletes_nothing()
    {
        var snapshots = Enumerable.Range(0, 500).Select(i => At(TimeSpan.FromMinutes(i))).ToList();
        var settings = Policy();
        settings.KeepEverything = true;

        Assert.Empty(RetentionPolicy.SelectForDeletion(snapshots, settings, Now));
    }

    [Fact]
    public void The_newest_snapshots_always_survive()
    {
        var snapshots = Enumerable.Range(0, 240).Select(i => At(TimeSpan.FromMinutes(i))).ToList();

        var doomed = RetentionPolicy.SelectForDeletion(snapshots, Policy(last: 30), Now)
            .Select(s => s.Id).ToHashSet();

        foreach (var recent in snapshots.Take(30))
            Assert.DoesNotContain(recent.Id, doomed);
    }

    [Fact]
    public void Pinned_snapshots_are_never_deleted()
    {
        var pinned = At(TimeSpan.FromDays(400), pinned: true);
        var snapshots = Enumerable.Range(0, 100).Select(i => At(TimeSpan.FromMinutes(i))).Append(pinned).ToList();

        var doomed = RetentionPolicy.SelectForDeletion(snapshots, Policy(last: 5, hours: 1, days: 1), Now);

        Assert.DoesNotContain(doomed, s => s.Id == pinned.Id);
    }

    [Fact]
    public void One_snapshot_per_hour_survives_inside_the_hourly_window()
    {
        var snapshots = Enumerable.Range(0, 36).Select(i => At(TimeSpan.FromMinutes(i * 10))).ToList();

        var settings = Policy(last: 1, hours: 6, days: 0);
        var doomed = RetentionPolicy.SelectForDeletion(snapshots, settings, Now).Select(s => s.Id).ToHashSet();
        var kept = snapshots.Where(s => !doomed.Contains(s.Id)).ToList();

        var hours = snapshots
            .Where(s => s.CreatedUtc >= Now.AddHours(-6))
            .Select(s => new DateTime(s.CreatedUtc.Year, s.CreatedUtc.Month, s.CreatedUtc.Day, s.CreatedUtc.Hour, 0, 0))
            .Distinct().Count();

        Assert.Equal(hours, kept.Count);
    }

    [Fact]
    public void Snapshots_older_than_every_tier_are_pruned()
    {
        var ancient = At(TimeSpan.FromDays(30));
        var snapshots = new List<Snapshot> { At(TimeSpan.FromMinutes(1)), ancient };

        var doomed = RetentionPolicy.SelectForDeletion(snapshots, Policy(last: 1, hours: 24, days: 7), Now);

        Assert.Single(doomed);
        Assert.Equal(ancient.Id, doomed[0].Id);
    }

    [Fact]
    public void Deletions_are_returned_oldest_first()
    {
        var snapshots = Enumerable.Range(0, 50).Select(i => At(TimeSpan.FromDays(i + 10))).ToList();

        var doomed = RetentionPolicy.SelectForDeletion(snapshots, Policy(last: 1, hours: 0, days: 0), Now);

        Assert.True(doomed.Count > 1);
        Assert.True(doomed.Zip(doomed.Skip(1)).All(pair => pair.First.CreatedUtc <= pair.Second.CreatedUtc));
    }
}
