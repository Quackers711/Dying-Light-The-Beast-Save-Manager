using System.Text.Json.Serialization;
using System.Windows.Input;

namespace DLBeastSaveManager.Models;

public sealed class HotkeyBinding
{
    public ModifierKeys Modifiers { get; set; } = ModifierKeys.None;
    public Key Key { get; set; } = Key.None;
    public bool Enabled { get; set; } = true;

    [JsonIgnore]
    public bool IsBound => Enabled && Key != Key.None;

    public string Describe()
    {
        if (Key == Key.None) return "(unbound)";
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(Key.ToString());
        return string.Join("+", parts);
    }

    public static HotkeyBinding Of(Key key, ModifierKeys modifiers = ModifierKeys.None) =>
        new() { Key = key, Modifiers = modifiers };
}

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;

    public string? SavePathOverride { get; set; }

    public string? BackupRootOverride { get; set; }

    public string? PreferredSteamUserId { get; set; }

    public bool AutoBackupEnabled { get; set; } = true;

    public int DebounceSeconds { get; set; } = 3;

    public int IntervalMinutes { get; set; } = 5;

    public bool KeepEverything { get; set; }
    public int KeepLastCount { get; set; } = 30;
    public int KeepHourlyForHours { get; set; } = 24;
    public int KeepDailyForDays { get; set; } = 7;

    public bool HotkeysEnabled { get; set; } = true;
    public HotkeyBinding BackupNowHotkey { get; set; } = HotkeyBinding.Of(Key.F9);
    public HotkeyBinding PinLatestHotkey { get; set; } = HotkeyBinding.Of(Key.F10);

    public bool MinimizeToTray { get; set; } = true;
    public bool StartWithWindows { get; set; }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
