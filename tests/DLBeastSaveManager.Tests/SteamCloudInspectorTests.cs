using DLBeastSaveManager.Models;
using DLBeastSaveManager.Services;

namespace DLBeastSaveManager.Tests;

public class SteamCloudInspectorTests
{
    private const string SampleVdf = """
        "UserLocalConfigStore"
        {
        	"Software"
        	{
        		"Valve"
        		{
        			"Steam"
        			{
        				"apps"
        				{
        					"570"
        					{
        						"cloudenabled"		"0"
        					}
        					"3008130"
        					{
        						"LastPlayed"		"1775254262"
        						"cloudenabled"		"1"
        					}
        				}
        			}
        		}
        	}
        }
        """;

    [Fact]
    public void Reads_the_flag_from_the_right_app_block()
    {
        Assert.Equal(1, SteamCloudInspector.ReadCloudEnabledFlag(SampleVdf, "3008130"));
        Assert.Equal(0, SteamCloudInspector.ReadCloudEnabledFlag(SampleVdf, "570"));
    }

    [Fact]
    public void An_app_with_no_block_returns_null_meaning_steam_default()
    {
        Assert.Null(SteamCloudInspector.ReadCloudEnabledFlag(SampleVdf, "999999"));
    }

    [Fact]
    public void An_app_block_without_the_flag_returns_null()
    {
        const string vdf = """
            "apps"
            {
            	"3008130"
            	{
            		"LastPlayed"		"1775254262"
            	}
            }
            """;

        Assert.Null(SteamCloudInspector.ReadCloudEnabledFlag(vdf, "3008130"));
    }

    [Fact]
    public void Non_steam_locations_do_not_raise_a_cloud_warning()
    {
        var location = new SaveLocation
        {
            Platform = SavePlatform.Epic,
            SavePath = Path.GetTempPath()
        };

        var report = SteamCloudInspector.Inspect(location);

        Assert.Equal(CloudStatus.NotApplicable, report.Status);
        Assert.False(report.NeedsWarning);
    }

    [Fact]
    public void A_missing_steam_config_is_treated_as_cloud_on()
    {
        var location = new SaveLocation
        {
            Platform = SavePlatform.Steam,
            SavePath = Path.GetTempPath(),
            SteamUserId = "123456",
            SteamRoot = Path.Combine(Path.GetTempPath(), "no-steam-here")
        };

        var report = SteamCloudInspector.Inspect(location);

        Assert.True(report.NeedsWarning);
    }
}
