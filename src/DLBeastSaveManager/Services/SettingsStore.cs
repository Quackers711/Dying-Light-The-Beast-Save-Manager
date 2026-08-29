using System.Text.Json;
using System.Text.Json.Serialization;
using DLBeastSaveManager.Models;
using Microsoft.Win32;

namespace DLBeastSaveManager.Services;

public static class SettingsStore
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "DLBeastSaveManager";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DLBeastSaveManager");

    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions);
                if (loaded is not null) return loaded;
            }
        }
        catch (Exception)
        {
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var temp = SettingsPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));

        if (File.Exists(SettingsPath)) File.Replace(temp, SettingsPath, destinationBackupFileName: null);
        else File.Move(temp, SettingsPath);
    }

    public static string ResolveBackupRoot(AppSettings settings) =>
        string.IsNullOrWhiteSpace(settings.BackupRootOverride)
            ? BackupService.DefaultBackupRoot
            : Path.GetFullPath(settings.BackupRootOverride);

    public static bool IsStartWithWindowsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(RunValueName) is string value && value.Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool SetStartWithWindows(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null) return false;

            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exe)) return false;
                key.SetValue(RunValueName, $"\"{exe}\" --minimized");
            }
            else if (key.GetValue(RunValueName) is not null)
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
