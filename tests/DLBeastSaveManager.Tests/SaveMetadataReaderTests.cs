using System.IO.Compression;
using System.Text;
using DLBeastSaveManager.Services;

namespace DLBeastSaveManager.Tests;

public class SaveMetadataReaderTests
{
    private static byte[] MakeSave(params string[] values)
    {
        var body = new MemoryStream();
        body.Write(new byte[16]);

        foreach (var value in values)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            body.WriteByte((byte)(bytes.Length & 0xFF));
            body.WriteByte((byte)(bytes.Length >> 8));
            body.Write(bytes);
            body.Write(new byte[4]);
        }

        var zipped = new MemoryStream();
        using (var gzip = new GZipStream(zipped, CompressionLevel.Fastest, leaveOpen: true))
            gzip.Write(body.ToArray());

        return zipped.ToArray();
    }

    [Fact]
    public void Reads_difficulty_area_and_checkpoint_from_a_prologue_save()
    {
        var save = MakeSave(
            "EVersion::DLTB_Patch06_Hotfix_v219",
            "Frontier",
            "Nightmare",
            "Nightmare",
            "PrologueCheckpoint",
            "dlc_ft_prologue/^dlc_ft_bunker_a/Geo/Lab/Logic/SP_Lab_Start");

        var described = SaveMetadataReader.Read(new MemoryStream(save));

        Assert.Equal("Nightmare", described.Difficulty);
        Assert.Equal("Prologue", described.Area);
        Assert.Equal("Prologue checkpoint", described.Checkpoint);
        Assert.Equal("DLTB_Patch06_Hotfix_v219", described.GameVersion);
    }

    [Fact]
    public void A_zone_in_the_spawn_path_beats_the_map_name()
    {
        var save = MakeSave(
            "Normal",
            "dlc_frontier/^dlc_ft_hub_townhall_logic_interior/Zones/Sanctuary/Sanctuary");

        Assert.Equal("Sanctuary", SaveMetadataReader.Read(new MemoryStream(save)).Area);
    }

    [Fact]
    public void Ids_that_merely_look_like_places_are_ignored()
    {
        var save = MakeSave(
            "Normal",
            "dlc_ft_load_scr_txt_000_a",
            "dlc_ft_prologue/^dlc_ft_bunker_a/Logic/SP_Lab_Start");

        Assert.Equal("Prologue", SaveMetadataReader.Read(new MemoryStream(save)).Area);
    }

    [Fact]
    public void A_chapter_id_stands_in_when_there_is_no_checkpoint_name()
    {
        var save = MakeSave("Normal", "chp003");

        Assert.Equal("Chapter 3", SaveMetadataReader.Read(new MemoryStream(save)).Checkpoint);
    }

    [Fact]
    public void An_uncompressed_save_reads_the_same_way()
    {
        var body = new MemoryStream();
        body.Write(new byte[16]);
        var bytes = Encoding.ASCII.GetBytes("Nightmare");
        body.WriteByte((byte)bytes.Length);
        body.WriteByte(0);
        body.Write(bytes);

        Assert.Equal("Nightmare", SaveMetadataReader.Read(new MemoryStream(body.ToArray())).Difficulty);
    }

    [Fact]
    public void Unreadable_content_describes_nothing_instead_of_throwing()
    {
        Assert.True(SaveMetadataReader.Read(new MemoryStream(new byte[] { 1, 2, 3, 4, 5 })).IsEmpty);
        Assert.True(SaveMetadataReader.Read(new MemoryStream(Array.Empty<byte>())).IsEmpty);

        var lying = new byte[] { 0x1f, 0x8b, 0x08, 0xff, 0xff, 0xff, 0xff };
        Assert.True(SaveMetadataReader.Read(new MemoryStream(lying)).IsEmpty);
    }

    [Fact]
    public void A_missing_file_describes_nothing()
    {
        var path = Path.Combine(Path.GetTempPath(), "dlbsm-tests", Guid.NewGuid().ToString("N") + ".sav");
        Assert.True(SaveMetadataReader.ReadFile(path).IsEmpty);
    }
}
