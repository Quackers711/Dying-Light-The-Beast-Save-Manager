using DLBeastSaveManager.Models;
using DLBeastSaveManager.Services;

namespace DLBeastSaveManager.Tests;

public class SaveWatcherTests
{
    private static AppSettings FastWatch() => new()
    {
        DebounceSeconds = 1,
        IntervalMinutes = 0,
        KeepEverything = true
    };

    [Fact]
    public async Task A_save_write_triggers_exactly_one_debounced_backup()
    {
        using var ws = new TempWorkspace();
        ws.SeedTypicalSaveSet();

        var service = new BackupService(ws.SavePath, ws.BackupRoot);
        using var watcher = new SaveWatcher();

        var fired = new List<SnapshotTrigger>();
        var signal = new SemaphoreSlim(0);
        watcher.BackupRequested += (_, e) =>
        {
            fired.Add(e.Trigger);
            signal.Release();
        };

        watcher.Start(ws.SavePath, FastWatch());
        Assert.True(watcher.IsWatching);

        ws.WriteSave("save_ft_0.sav", "checkpoint 1");
        ws.WriteSave("save_ft_pw_0.sav", "profile 1");
        ws.WriteSave("save_ft_0_chp000.sbk", "chapter 1");

        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(10)), "The watcher never asked for a backup.");

        var result = await service.CreateSnapshotAsync(fired[0], FastWatch());
        Assert.Equal(BackupOutcome.Created, result.Outcome);

        await Task.Delay(2000);
        Assert.Single(fired);
    }

    [Fact]
    public async Task Rewriting_identical_content_produces_no_second_snapshot()
    {
        using var ws = new TempWorkspace();
        ws.WriteSave("save_ft_0.sav", "same bytes every time");

        var service = new BackupService(ws.SavePath, ws.BackupRoot);
        var settings = FastWatch();

        var first = await service.CreateSnapshotAsync(SnapshotTrigger.Auto, settings);
        Assert.Equal(BackupOutcome.Created, first.Outcome);

        ws.WriteSave("save_ft_0.sav", "same bytes every time");
        var second = await service.CreateSnapshotAsync(SnapshotTrigger.Auto, settings);

        Assert.Equal(BackupOutcome.SkippedUnchanged, second.Outcome);
        Assert.Single(Directory.GetFiles(ws.BackupRoot, "*.zip"));
    }

    [Fact]
    public void Watching_a_missing_folder_reports_an_error_instead_of_pretending_to_work()
    {
        using var ws = new TempWorkspace();
        using var watcher = new SaveWatcher();

        string? reported = null;
        watcher.WatchError += (_, message) => reported = message;

        watcher.Start(Path.Combine(ws.Root, "not-there"), FastWatch());

        Assert.False(watcher.IsWatching);
        Assert.NotNull(reported);
    }

    [Fact]
    public async Task Stopping_the_watcher_stops_the_requests()
    {
        using var ws = new TempWorkspace();
        ws.SeedTypicalSaveSet();

        using var watcher = new SaveWatcher();
        var fired = 0;
        watcher.BackupRequested += (_, _) => Interlocked.Increment(ref fired);

        watcher.Start(ws.SavePath, FastWatch());
        watcher.Stop();

        ws.WriteSave("save_ft_0.sav", "written after stopping");
        await Task.Delay(2500);

        Assert.Equal(0, fired);
    }
}
