using DLBeastSaveManager.Services;

namespace DLBeastSaveManager.Tests;

public class SaveSetHasherTests
{
    [Fact]
    public async Task Hash_is_stable_across_repeated_reads()
    {
        using var ws = new TempWorkspace();
        ws.SeedTypicalSaveSet();

        var first = await SaveSetHasher.HashAsync(ws.SavePath);
        var second = await SaveSetHasher.HashAsync(ws.SavePath);

        Assert.Equal(first.SetHash, second.SetHash);
        Assert.Equal(3, first.Count);
    }

    [Fact]
    public async Task Hash_changes_when_a_file_changes()
    {
        using var ws = new TempWorkspace();
        ws.SeedTypicalSaveSet();
        var before = await SaveSetHasher.HashAsync(ws.SavePath);

        ws.WriteSave("save_ft_0.sav", "main slot, one checkpoint later");
        var after = await SaveSetHasher.HashAsync(ws.SavePath);

        Assert.NotEqual(before.SetHash, after.SetHash);
    }

    [Fact]
    public async Task Hash_changes_when_a_file_is_added_or_removed()
    {
        using var ws = new TempWorkspace();
        ws.SeedTypicalSaveSet();
        var baseline = await SaveSetHasher.HashAsync(ws.SavePath);

        ws.WriteSave("save_ft_rl_0.sav", "restored land");
        var added = await SaveSetHasher.HashAsync(ws.SavePath);
        Assert.NotEqual(baseline.SetHash, added.SetHash);

        File.Delete(Path.Combine(ws.SavePath, "save_ft_rl_0.sav"));
        var removed = await SaveSetHasher.HashAsync(ws.SavePath);
        Assert.Equal(baseline.SetHash, removed.SetHash);
    }

    [Fact]
    public async Task Touching_a_file_without_changing_content_keeps_the_hash()
    {
        using var ws = new TempWorkspace();
        ws.SeedTypicalSaveSet();
        var before = await SaveSetHasher.HashAsync(ws.SavePath);

        File.SetLastWriteTimeUtc(Path.Combine(ws.SavePath, "save_ft_0.sav"), DateTime.UtcNow.AddMinutes(5));
        var after = await SaveSetHasher.HashAsync(ws.SavePath);

        Assert.Equal(before.SetHash, after.SetHash);
    }

    [Fact]
    public async Task Empty_or_missing_folder_yields_an_empty_set()
    {
        using var ws = new TempWorkspace();

        var empty = await SaveSetHasher.HashAsync(ws.SavePath);
        Assert.True(empty.IsEmpty);

        var missing = await SaveSetHasher.HashAsync(Path.Combine(ws.Root, "nope"));
        Assert.True(missing.IsEmpty);
    }

    [Fact]
    public async Task A_file_held_open_by_another_process_can_still_be_hashed()
    {
        using var ws = new TempWorkspace();
        var path = ws.WriteSave("save_ft_0.sav", "locked while the game runs");

        await using var holder = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        var set = await SaveSetHasher.HashAsync(ws.SavePath);
        Assert.Equal(1, set.Count);
    }
}
