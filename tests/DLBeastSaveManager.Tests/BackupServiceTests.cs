using System.IO.Compression;
using DLBeastSaveManager.Models;
using DLBeastSaveManager.Services;

namespace DLBeastSaveManager.Tests;

public class BackupServiceTests
{
    private static AppSettings KeepAll() => new() { KeepEverything = true };

    [Fact]
    public async Task Creating_a_snapshot_captures_every_file()
    {
        using var ws = new TempWorkspace();
        ws.SeedTypicalSaveSet();
        var service = new BackupService(ws.SavePath, ws.BackupRoot);

        var result = await service.CreateSnapshotAsync(SnapshotTrigger.Manual, KeepAll());

        Assert.Equal(BackupOutcome.Created, result.Outcome);
        Assert.Equal(3, result.Snapshot!.FileCount);

        using var archive = ZipFile.OpenRead(Path.Combine(ws.BackupRoot, result.Snapshot.ZipFileName));
        Assert.NotNull(archive.GetEntry(SnapshotIndex.ManifestEntryName));
        Assert.NotNull(archive.GetEntry(SnapshotIndex.FilesPrefix + "save_ft_0.sav"));
        Assert.NotNull(archive.GetEntry(SnapshotIndex.FilesPrefix + "save_ft_pw_0.sav"));
        Assert.NotNull(archive.GetEntry(SnapshotIndex.FilesPrefix + "save_ft_0_chp000.sbk"));
    }

    [Fact]
    public async Task An_unchanged_save_is_not_snapshotted_twice()
    {
        using var ws = new TempWorkspace();
        ws.SeedTypicalSaveSet();
        var service = new BackupService(ws.SavePath, ws.BackupRoot);

        await service.CreateSnapshotAsync(SnapshotTrigger.Auto, KeepAll());
        var second = await service.CreateSnapshotAsync(SnapshotTrigger.Interval, KeepAll());

        Assert.Equal(BackupOutcome.SkippedUnchanged, second.Outcome);
        Assert.Single(service.Index.Snapshots);
    }

    [Fact]
    public async Task Force_snapshots_even_when_unchanged()
    {
        using var ws = new TempWorkspace();
        ws.SeedTypicalSaveSet();
        var service = new BackupService(ws.SavePath, ws.BackupRoot);

        await service.CreateSnapshotAsync(SnapshotTrigger.Auto, KeepAll());
        var forced = await service.CreateSnapshotAsync(SnapshotTrigger.Manual, KeepAll(), force: true);

        Assert.Equal(BackupOutcome.Created, forced.Outcome);
        Assert.Equal(2, service.Index.Snapshots.Count);
    }

    [Fact]
    public async Task An_empty_save_folder_is_skipped()
    {
        using var ws = new TempWorkspace();
        var service = new BackupService(ws.SavePath, ws.BackupRoot);

        var result = await service.CreateSnapshotAsync(SnapshotTrigger.Manual, KeepAll());

        Assert.Equal(BackupOutcome.SkippedNoSaveFiles, result.Outcome);
    }

    [Fact]
    public async Task Restore_reproduces_the_exact_bytes_of_an_earlier_save()
    {
        using var ws = new TempWorkspace();
        ws.SeedTypicalSaveSet();
        var service = new BackupService(ws.SavePath, ws.BackupRoot);

        var before = await service.CreateSnapshotAsync(SnapshotTrigger.Manual, KeepAll());
        var originalHash = (await SaveSetHasher.HashAsync(ws.SavePath)).SetHash;

        ws.WriteSave("save_ft_0.sav", "dead");
        ws.WriteSave("save_ft_new.sav", "junk written after the good state");

        var result = await service.RestoreAsync(before.Snapshot!, KeepAll(), new RestoreOptions());

        Assert.True(result.Success, result.Message);
        Assert.Equal(originalHash, (await SaveSetHasher.HashAsync(ws.SavePath)).SetHash);

        Assert.DoesNotContain("save_ft_new.sav", ws.SaveFileNames());
    }

    [Fact]
    public async Task Restore_takes_a_pinned_safety_snapshot_first()
    {
        using var ws = new TempWorkspace();
        ws.SeedTypicalSaveSet();
        var service = new BackupService(ws.SavePath, ws.BackupRoot);

        var target = await service.CreateSnapshotAsync(SnapshotTrigger.Manual, KeepAll());
        ws.WriteSave("save_ft_0.sav", "current state, about to be replaced");

        var result = await service.RestoreAsync(target.Snapshot!, KeepAll(), new RestoreOptions());

        Assert.NotNull(result.SafetySnapshot);
        Assert.Equal(SnapshotTrigger.PreRestore, result.SafetySnapshot!.Trigger);
        Assert.True(result.SafetySnapshot.Pinned);

        var undo = await service.RestoreAsync(result.SafetySnapshot, KeepAll(), new RestoreOptions());
        Assert.True(undo.Success, undo.Message);
        Assert.Equal("current state, about to be replaced",
            File.ReadAllText(Path.Combine(ws.SavePath, "save_ft_0.sav")));
    }

    [Fact]
    public async Task Replaced_files_are_kept_in_the_trash_folder()
    {
        using var ws = new TempWorkspace();
        ws.SeedTypicalSaveSet();
        var service = new BackupService(ws.SavePath, ws.BackupRoot);

        var target = await service.CreateSnapshotAsync(SnapshotTrigger.Manual, KeepAll());
        ws.WriteSave("save_ft_0.sav", "state that gets replaced");

        await service.RestoreAsync(target.Snapshot!, KeepAll(),
            new RestoreOptions { KeepReplacedFilesInTrash = true });

        var trashed = Directory.GetFiles(service.TrashRoot, "*", SearchOption.AllDirectories);
        Assert.Contains(trashed, p => File.ReadAllText(p) == "state that gets replaced");
    }

    [Fact]
    public async Task Restored_files_are_stamped_now_so_they_beat_the_cloud_copy()
    {
        using var ws = new TempWorkspace();
        ws.SeedTypicalSaveSet();
        var service = new BackupService(ws.SavePath, ws.BackupRoot);

        var target = await service.CreateSnapshotAsync(SnapshotTrigger.Manual, KeepAll());
        ws.WriteSave("save_ft_0.sav", "newer");

        var before = DateTime.Now.AddSeconds(-5);
        await service.RestoreAsync(target.Snapshot!, KeepAll(),
            new RestoreOptions { StampCurrentTimestamps = true });

        foreach (var file in Directory.GetFiles(ws.SavePath))
            Assert.True(File.GetLastWriteTime(file) >= before,
                $"{Path.GetFileName(file)} was not stamped with the current time.");
    }

    [Fact]
    public async Task Restoring_a_missing_zip_fails_without_touching_the_save()
    {
        using var ws = new TempWorkspace();
        ws.SeedTypicalSaveSet();
        var service = new BackupService(ws.SavePath, ws.BackupRoot);

        var snapshot = new Snapshot { Id = "does-not-exist", CreatedUtc = DateTime.UtcNow };
        var hashBefore = (await SaveSetHasher.HashAsync(ws.SavePath)).SetHash;

        var result = await service.RestoreAsync(snapshot, KeepAll(), new RestoreOptions());

        Assert.False(result.Success);
        Assert.Equal(hashBefore, (await SaveSetHasher.HashAsync(ws.SavePath)).SetHash);
    }

    [Fact]
    public async Task Pinning_and_labelling_survive_a_reload()
    {
        using var ws = new TempWorkspace();
        ws.SeedTypicalSaveSet();
        var service = new BackupService(ws.SavePath, ws.BackupRoot);

        var created = await service.CreateSnapshotAsync(SnapshotTrigger.Manual, KeepAll());
        service.SetPinned(created.Snapshot!, true);
        service.SetLabel(created.Snapshot!, "before Baron boss");

        var reopened = new BackupService(ws.SavePath, ws.BackupRoot);
        var snapshot = reopened.Index.ById(created.Snapshot!.Id);

        Assert.NotNull(snapshot);
        Assert.True(snapshot!.Pinned);
        Assert.Equal("before Baron boss", snapshot.Label);
    }

    [Fact]
    public async Task Pruning_respects_the_retention_policy_and_deletes_the_zips()
    {
        using var ws = new TempWorkspace();
        var service = new BackupService(ws.SavePath, ws.BackupRoot);

        for (var i = 0; i < 6; i++)
        {
            ws.WriteSave("save_ft_0.sav", $"checkpoint {i}");
            await service.CreateSnapshotAsync(SnapshotTrigger.Auto, new AppSettings { KeepEverything = true });
        }

        Assert.Equal(6, service.Index.Snapshots.Count);

        var removed = service.Prune(new AppSettings
        {
            KeepEverything = false,
            KeepLastCount = 2,
            KeepHourlyForHours = 0,
            KeepDailyForDays = 0
        });

        Assert.Equal(4, removed);
        Assert.Equal(2, service.Index.Snapshots.Count);
        Assert.Equal(2, Directory.GetFiles(ws.BackupRoot, "*.zip").Length);
    }
}
