using DLBeastSaveManager.Models;
using DLBeastSaveManager.Services;

namespace DLBeastSaveManager.Tests;

public class RunRestoreTests
{
    private static AppSettings KeepAll() => new() { KeepEverything = true };

    [Fact]
    public async Task A_snapshot_records_every_file_and_the_runs_it_holds()
    {
        using var ws = new TempWorkspace();
        ws.SeedTwoRuns();
        var service = new BackupService(ws.SavePath, ws.BackupRoot);

        var snapshot = (await service.CreateSnapshotAsync(SnapshotTrigger.Manual, KeepAll())).Snapshot!;

        Assert.Equal(3, snapshot.Files.Count);
        Assert.All(snapshot.Files, f => Assert.NotEmpty(f.Sha256));
        Assert.Equal(new[] { "0", "1" }, snapshot.Runs.Select(r => r.Key));
    }

    [Fact]
    public async Task The_run_that_changed_is_the_run_the_snapshot_belongs_to()
    {
        using var ws = new TempWorkspace();
        ws.SeedTwoRuns();
        var service = new BackupService(ws.SavePath, ws.BackupRoot);

        await service.CreateSnapshotAsync(SnapshotTrigger.Manual, KeepAll());

        ws.WriteSave("save_ft_1.sav", "slot one, further in");
        var second = (await service.CreateSnapshotAsync(SnapshotTrigger.Auto, KeepAll())).Snapshot!;

        ws.WriteSave("save_ft_0.sav", "slot zero, further in");
        var third = (await service.CreateSnapshotAsync(SnapshotTrigger.Auto, KeepAll())).Snapshot!;

        var changes = service.Index.RunChanges();

        Assert.Equal(new[] { "1" }, changes[second.Id].Changed);
        Assert.Equal(new[] { "0" }, changes[third.Id].Changed);
    }

    [Fact]
    public async Task A_forced_duplicate_keeps_the_previous_answer()
    {
        using var ws = new TempWorkspace();
        ws.SeedTwoRuns();
        var service = new BackupService(ws.SavePath, ws.BackupRoot);

        await service.CreateSnapshotAsync(SnapshotTrigger.Manual, KeepAll());
        ws.WriteSave("save_ft_1.sav", "slot one, further in");
        await service.CreateSnapshotAsync(SnapshotTrigger.Auto, KeepAll());

        var forced = (await service.CreateSnapshotAsync(
            SnapshotTrigger.Manual, KeepAll(), force: true)).Snapshot!;

        Assert.Equal(new[] { "1" }, service.Index.RunChanges()[forced.Id].Changed);
    }

    [Fact]
    public async Task A_run_disappearing_is_reported()
    {
        using var ws = new TempWorkspace();
        ws.SeedTwoRuns();
        var service = new BackupService(ws.SavePath, ws.BackupRoot);

        await service.CreateSnapshotAsync(SnapshotTrigger.Manual, KeepAll());

        File.Delete(Path.Combine(ws.SavePath, "save_ft_1.sav"));
        var after = (await service.CreateSnapshotAsync(SnapshotTrigger.Auto, KeepAll())).Snapshot!;

        var change = service.Index.RunChanges()[after.Id];
        Assert.Equal(new[] { "1" }, change.Removed);
        Assert.Empty(change.Changed);
    }

    [Fact]
    public async Task Restoring_one_run_leaves_the_other_untouched()
    {
        using var ws = new TempWorkspace();
        ws.SeedTwoRuns();
        var service = new BackupService(ws.SavePath, ws.BackupRoot);

        var early = (await service.CreateSnapshotAsync(SnapshotTrigger.Manual, KeepAll())).Snapshot!;

        ws.WriteSave("save_ft_1.sav", "slot one, died here");
        ws.WriteSave("save_ft_0.sav", "slot zero, much further in");
        await service.CreateSnapshotAsync(SnapshotTrigger.Auto, KeepAll());

        var result = await service.RestoreAsync(early, KeepAll(), new RestoreOptions
        {
            TakeSafetySnapshot = false,
            RunKey = "1"
        });

        Assert.True(result.Success);
        Assert.Equal(1, result.FilesRestored);
        Assert.Contains("Slot 1", result.Message);

        Assert.Equal("slot one", File.ReadAllText(Path.Combine(ws.SavePath, "save_ft_1.sav")));
        Assert.Equal("slot zero, much further in", File.ReadAllText(Path.Combine(ws.SavePath, "save_ft_0.sav")));
        Assert.Equal("slot zero world", File.ReadAllText(Path.Combine(ws.SavePath, "save_ft_pw_0.sav")));
    }

    [Fact]
    public async Task Restoring_a_run_takes_its_world_with_it()
    {
        using var ws = new TempWorkspace();
        ws.SeedTwoRuns();
        var service = new BackupService(ws.SavePath, ws.BackupRoot);

        var early = (await service.CreateSnapshotAsync(SnapshotTrigger.Manual, KeepAll())).Snapshot!;

        ws.WriteSave("save_ft_0.sav", "slot zero, later");
        ws.WriteSave("save_ft_pw_0.sav", "slot zero world, later");

        await service.RestoreAsync(early, KeepAll(), new RestoreOptions
        {
            TakeSafetySnapshot = false,
            RunKey = "0"
        });

        Assert.Equal("slot zero", File.ReadAllText(Path.Combine(ws.SavePath, "save_ft_0.sav")));
        Assert.Equal("slot zero world", File.ReadAllText(Path.Combine(ws.SavePath, "save_ft_pw_0.sav")));
    }

    [Fact]
    public async Task A_scoped_restore_puts_back_a_run_that_was_deleted()
    {
        using var ws = new TempWorkspace();
        ws.SeedTwoRuns();
        var service = new BackupService(ws.SavePath, ws.BackupRoot);

        var before = (await service.CreateSnapshotAsync(SnapshotTrigger.Manual, KeepAll())).Snapshot!;
        File.Delete(Path.Combine(ws.SavePath, "save_ft_1.sav"));

        var result = await service.RestoreAsync(before, KeepAll(), new RestoreOptions
        {
            TakeSafetySnapshot = false,
            RunKey = "1"
        });

        Assert.True(result.Success);
        Assert.Equal("slot one", File.ReadAllText(Path.Combine(ws.SavePath, "save_ft_1.sav")));
    }

    [Fact]
    public async Task Restoring_everything_still_replaces_the_whole_folder()
    {
        using var ws = new TempWorkspace();
        ws.SeedTwoRuns();
        var service = new BackupService(ws.SavePath, ws.BackupRoot);

        var early = (await service.CreateSnapshotAsync(SnapshotTrigger.Manual, KeepAll())).Snapshot!;
        ws.WriteSave("save_ft_0.sav", "changed");
        ws.WriteSave("save_ft_1.sav", "changed");

        var result = await service.RestoreAsync(early, KeepAll(), new RestoreOptions
        {
            TakeSafetySnapshot = false
        });

        Assert.Equal(3, result.FilesRestored);
        Assert.Equal("slot zero", File.ReadAllText(Path.Combine(ws.SavePath, "save_ft_0.sav")));
        Assert.Equal("slot one", File.ReadAllText(Path.Combine(ws.SavePath, "save_ft_1.sav")));
    }

    [Fact]
    public async Task Restoring_a_run_the_snapshot_does_not_hold_is_refused()
    {
        using var ws = new TempWorkspace();
        ws.SeedTypicalSaveSet();
        var service = new BackupService(ws.SavePath, ws.BackupRoot);

        var snapshot = (await service.CreateSnapshotAsync(SnapshotTrigger.Manual, KeepAll())).Snapshot!;

        var result = await service.RestoreAsync(snapshot, KeepAll(), new RestoreOptions
        {
            TakeSafetySnapshot = false,
            RunKey = "1"
        });

        Assert.False(result.Success);
        Assert.Contains("Slot 1", result.Message);
        Assert.Equal(0, result.FilesRestored);
    }

    [Fact]
    public async Task Older_snapshots_get_their_run_details_filled_in()
    {
        using var ws = new TempWorkspace();
        ws.SeedTwoRuns();
        var service = new BackupService(ws.SavePath, ws.BackupRoot);
        await service.CreateSnapshotAsync(SnapshotTrigger.Manual, KeepAll());

        var index = SnapshotIndex.Load(ws.BackupRoot);
        foreach (var snapshot in index.Snapshots)
        {
            snapshot.Files = new List<SnapshotFile>();
            snapshot.Runs = new List<RunInfo>();
        }
        index.Save(ws.BackupRoot);

        var reloaded = SnapshotIndex.Load(ws.BackupRoot);
        Assert.True(reloaded.BackfillRuns(ws.BackupRoot));
        Assert.Equal(3, reloaded.Snapshots[0].Files.Count);
        Assert.Equal(new[] { "0", "1" }, reloaded.Snapshots[0].Runs.Select(r => r.Key));
    }
}
