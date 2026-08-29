using DLBeastSaveManager.Models;
using DLBeastSaveManager.Services;

namespace DLBeastSaveManager.Tests;

public class SnapshotIndexTests
{
    private static AppSettings KeepAll() => new() { KeepEverything = true };

    private static async Task<BackupService> SeedAsync(TempWorkspace ws, int count)
    {
        var service = new BackupService(ws.SavePath, ws.BackupRoot);
        for (var i = 0; i < count; i++)
        {
            ws.WriteSave("save_ft_0.sav", $"checkpoint {i}");
            await service.CreateSnapshotAsync(SnapshotTrigger.Auto, KeepAll());
        }

        return service;
    }

    [Fact]
    public async Task Index_can_be_rebuilt_from_the_zips_alone()
    {
        using var ws = new TempWorkspace();
        var service = await SeedAsync(ws, 3);
        service.SetLabel(service.Index.Snapshots[0], "keep me");
        service.SetPinned(service.Index.Snapshots[0], true);

        File.Delete(SnapshotIndex.IndexPath(ws.BackupRoot));

        var rebuilt = SnapshotIndex.Rebuild(ws.BackupRoot);

        Assert.Equal(3, rebuilt.Snapshots.Count);
        Assert.Equal("keep me", rebuilt.Newest!.Label);
        Assert.True(rebuilt.Newest.Pinned);
    }

    [Fact]
    public async Task A_corrupt_index_does_not_lose_the_snapshots()
    {
        using var ws = new TempWorkspace();
        await SeedAsync(ws, 2);

        File.WriteAllText(SnapshotIndex.IndexPath(ws.BackupRoot), "{ this is not json");

        var loaded = SnapshotIndex.Load(ws.BackupRoot);

        Assert.Equal(2, loaded.Snapshots.Count);
    }

    [Fact]
    public async Task A_zip_deleted_by_hand_disappears_from_the_index()
    {
        using var ws = new TempWorkspace();
        var service = await SeedAsync(ws, 3);
        var victim = service.Index.Snapshots[1];

        File.Delete(Path.Combine(ws.BackupRoot, victim.ZipFileName));

        var loaded = SnapshotIndex.Load(ws.BackupRoot);

        Assert.Equal(2, loaded.Snapshots.Count);
        Assert.Null(loaded.ById(victim.Id));
    }

    [Fact]
    public async Task An_unreadable_zip_is_skipped_rather_than_breaking_the_load()
    {
        using var ws = new TempWorkspace();
        await SeedAsync(ws, 2);
        File.WriteAllText(Path.Combine(ws.BackupRoot, "not-really-a-zip.zip"), "garbage");

        var loaded = SnapshotIndex.Load(ws.BackupRoot);

        Assert.Equal(2, loaded.Snapshots.Count);
    }

    [Fact]
    public async Task Snapshots_are_ordered_newest_first()
    {
        using var ws = new TempWorkspace();
        var service = await SeedAsync(ws, 4);

        var times = service.Index.Snapshots.Select(s => s.CreatedUtc).ToList();

        Assert.Equal(times.OrderByDescending(t => t), times);
    }
}
