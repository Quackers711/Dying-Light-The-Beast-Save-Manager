using System.Text.RegularExpressions;

namespace DLBeastSaveManager.Services;

public sealed record SaveRun(string Key, IReadOnlyList<string> Files)
{
    public string DisplayName => SaveRuns.NameFor(Key);
}

public static class SaveRuns
{
    public const string OtherKey = "other";

    private static readonly Regex SlotPattern = new(
        @"^save_ft_(?:pw_)?(?<slot>\d+)(?:_chp\d+)?\.(?:sav|sbk)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CampaignPattern = new(
        @"^save_ft_(?<slot>\d+)\.sav$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string KeyFor(string relativePath)
    {
        var name = Path.GetFileName(relativePath.Replace('\\', '/'));
        if (string.IsNullOrEmpty(name)) return OtherKey;

        var match = SlotPattern.Match(name);
        if (!match.Success) return OtherKey;

        var slot = match.Groups["slot"].Value.TrimStart('0');
        return slot.Length > 0 ? slot : "0";
    }

    public static bool IsCampaignSave(string relativePath) =>
        CampaignPattern.IsMatch(Path.GetFileName(relativePath.Replace('\\', '/')));

    public static string NameFor(string key) =>
        key == OtherKey ? "Other files" : $"Slot {key}";

    public static IReadOnlyList<SaveRun> Group(IEnumerable<string> relativePaths)
    {
        var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var path in relativePaths)
        {
            var key = KeyFor(path);
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<string>();
            list.Add(path);
        }

        return groups
            .OrderBy(g => g.Key == OtherKey)
            .ThenBy(g => int.TryParse(g.Key, out var n) ? n : int.MaxValue)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new SaveRun(g.Key, g.Value.OrderBy(p => p, StringComparer.Ordinal).ToList()))
            .ToList();
    }
}
