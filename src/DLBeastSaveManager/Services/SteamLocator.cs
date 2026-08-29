using DLBeastSaveManager.Models;
using Microsoft.Win32;

namespace DLBeastSaveManager.Services;

public static class SteamLocator
{
    public const string AppId = "3008130";

    private const string SteamRegistryKey = @"Software\Valve\Steam";

    private static string EpicSavePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "dying light the beast", "out", "storage");

    public static string? FindSteamRoot()
    {
        foreach (var (hive, view) in new[]
                 {
                     (RegistryHive.CurrentUser, RegistryView.Default),
                     (RegistryHive.LocalMachine, RegistryView.Registry64),
                     (RegistryHive.LocalMachine, RegistryView.Registry32)
                 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(SteamRegistryKey);
                var path = key?.GetValue("SteamPath") as string
                           ?? key?.GetValue("InstallPath") as string;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    var normalized = Path.GetFullPath(path.Replace('/', '\\'));
                    if (Directory.Exists(normalized)) return normalized;
                }
            }
            catch (Exception)
            {
            }
        }

        return null;
    }

    public static IReadOnlyList<SaveLocation> FindAll()
    {
        var results = new List<SaveLocation>();

        var steamRoot = FindSteamRoot();
        if (steamRoot is not null)
        {
            var userdata = Path.Combine(steamRoot, "userdata");
            if (Directory.Exists(userdata))
            {
                foreach (var accountDir in SafeEnumerateDirectories(userdata))
                {
                    var appDir = Path.Combine(accountDir, AppId);
                    var savePath = Path.Combine(appDir, "remote", "out", "save");
                    if (!Directory.Exists(savePath)) continue;

                    results.Add(new SaveLocation
                    {
                        Platform = SavePlatform.Steam,
                        SavePath = savePath,
                        SteamUserId = Path.GetFileName(accountDir),
                        SteamRoot = steamRoot,
                        RemoteCachePath = Path.Combine(appDir, "remotecache.vdf")
                    });
                }
            }
        }

        if (Directory.Exists(EpicSavePath))
        {
            results.Add(new SaveLocation
            {
                Platform = SavePlatform.Epic,
                SavePath = EpicSavePath
            });
        }

        return results;
    }

    public static SaveLocation? Resolve(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.SavePathOverride))
        {
            var match = FindAll().FirstOrDefault(l =>
                PathsEqual(l.SavePath, settings.SavePathOverride!));

            return match ?? new SaveLocation
            {
                Platform = SavePlatform.Manual,
                SavePath = Path.GetFullPath(settings.SavePathOverride!)
            };
        }

        var all = FindAll();
        if (all.Count == 0) return null;

        if (!string.IsNullOrWhiteSpace(settings.PreferredSteamUserId))
        {
            var preferred = all.FirstOrDefault(l => l.SteamUserId == settings.PreferredSteamUserId);
            if (preferred is not null) return preferred;
        }

        return all
            .OrderByDescending(l => l.SaveFileCount > 0)
            .ThenByDescending(l => l.Platform == SavePlatform.Steam)
            .First();
    }

    public static bool PathsEqual(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path);
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }
}
