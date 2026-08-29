using System.Text.RegularExpressions;
using DLBeastSaveManager.Models;

namespace DLBeastSaveManager.Services;

public enum CloudStatus
{
    NotApplicable,

    Enabled,

    Disabled,

    Unknown
}

public sealed record CloudReport(CloudStatus Status, string Detail)
{
    public bool NeedsWarning => Status is CloudStatus.Enabled or CloudStatus.Unknown;
}

public static class SteamCloudInspector
{
    private const string SharedConfigRelativePath = @"7\remote\sharedconfig.vdf";

    public const string HowToDisable =
        "Steam \u2192 Library \u2192 right-click Dying Light: The Beast \u2192 Properties \u2192 General \u2192 " +
        "untick \"Keep game saves in the Steam Cloud\".";

    public static CloudReport Inspect(SaveLocation? location)
    {
        if (location is null || location.Platform != SavePlatform.Steam ||
            location.SteamRoot is null || location.SteamUserId is null)
        {
            return new CloudReport(CloudStatus.NotApplicable, "This save folder is not synced by Steam Cloud.");
        }

        var configPath = Path.Combine(location.SteamRoot, "userdata", location.SteamUserId, SharedConfigRelativePath);
        if (!File.Exists(configPath))
        {
            return new CloudReport(CloudStatus.Unknown,
                $"Could not read Steam's config ({configPath}). Assume Cloud sync is on.");
        }

        try
        {
            var text = File.ReadAllText(configPath);
            var flag = ReadCloudEnabledFlag(text, SteamLocator.AppId);

            return flag switch
            {
                0 => new CloudReport(CloudStatus.Disabled,
                    "Steam Cloud is turned off for this game - restores will stick."),
                _ when flag.HasValue => new CloudReport(CloudStatus.Enabled,
                    "Steam Cloud is turned on for this game."),
                _ => new CloudReport(CloudStatus.Enabled,
                    "Steam Cloud is at its default for this game, which means it is on.")
            };
        }
        catch (Exception ex)
        {
            return new CloudReport(CloudStatus.Unknown,
                $"Could not read Steam's config: {ex.Message}. Assume Cloud sync is on.");
        }
    }

    internal static int? ReadCloudEnabledFlag(string vdfText, string appId)
    {
        var appMatch = Regex.Match(vdfText, "\"" + Regex.Escape(appId) + "\"\\s*\\{", RegexOptions.IgnoreCase);
        if (!appMatch.Success) return null;

        var depth = 0;
        var start = appMatch.Index + appMatch.Length - 1;
        var end = -1;
        for (var i = start; i < vdfText.Length; i++)
        {
            if (vdfText[i] == '{') depth++;
            else if (vdfText[i] == '}')
            {
                depth--;
                if (depth == 0) { end = i; break; }
            }
        }

        if (end < 0) return null;

        var block = vdfText[start..end];
        var flagMatch = Regex.Match(block, "\"cloudenabled\"\\s*\"(\\d+)\"", RegexOptions.IgnoreCase);
        return flagMatch.Success && int.TryParse(flagMatch.Groups[1].Value, out var value) ? value : null;
    }

    public static bool HasRemoteCache(SaveLocation? location) =>
        location?.RemoteCachePath is not null && File.Exists(location.RemoteCachePath);
}
