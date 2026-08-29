using DLBeastSaveManager.Services;

namespace DLBeastSaveManager.Tests;

public class SaveRunsTests
{
    [Theory]
    [InlineData("save_ft_0.sav", "0")]
    [InlineData("save_ft_1.sav", "1")]
    [InlineData("save_ft_12.sav", "12")]
    [InlineData("save_ft_0_chp000.sbk", "0")]
    [InlineData("save_ft_1_chp007.sbk", "1")]
    [InlineData("save_ft_pw_0.sav", "0")]
    [InlineData("save_ft_pw_1.sav", "1")]
    public void Files_are_keyed_by_slot(string name, string expected) =>
        Assert.Equal(expected, SaveRuns.KeyFor(name));

    [Theory]
    [InlineData("settings.cfg")]
    [InlineData("save_ft_x.sav")]
    [InlineData("something_else")]
    public void Unrecognised_names_land_in_the_catch_all(string name) =>
        Assert.Equal(SaveRuns.OtherKey, SaveRuns.KeyFor(name));

    [Fact]
    public void Only_the_campaign_save_describes_a_run()
    {
        Assert.True(SaveRuns.IsCampaignSave("save_ft_0.sav"));
        Assert.False(SaveRuns.IsCampaignSave("save_ft_pw_0.sav"));
        Assert.False(SaveRuns.IsCampaignSave("save_ft_0_chp000.sbk"));
    }

    [Fact]
    public void A_run_holds_its_campaign_world_and_chapter_backups()
    {
        var runs = SaveRuns.Group(new[]
        {
            "save_ft_1.sav",
            "save_ft_0.sav",
            "save_ft_pw_0.sav",
            "save_ft_0_chp000.sbk",
            "stray.txt"
        });

        Assert.Equal(new[] { "0", "1", SaveRuns.OtherKey }, runs.Select(r => r.Key));

        var slot0 = runs.First(r => r.Key == "0");
        Assert.Equal(
            new[] { "save_ft_0.sav", "save_ft_0_chp000.sbk", "save_ft_pw_0.sav" },
            slot0.Files);

        Assert.Equal("Slot 0", slot0.DisplayName);
        Assert.Equal(new[] { "save_ft_1.sav" }, runs.First(r => r.Key == "1").Files);
    }

    [Fact]
    public void Grouping_survives_a_folder_it_does_not_recognise()
    {
        var runs = SaveRuns.Group(new[] { "a.dat", "b.dat" });

        Assert.Single(runs);
        Assert.Equal(SaveRuns.OtherKey, runs[0].Key);
        Assert.Equal("Other files", runs[0].DisplayName);
    }
}
