namespace DLBeastSaveManager.Models;

public enum SavePlatform
{
    Steam,
    Epic,
    Manual
}

public sealed class SaveLocation
{
    public required SavePlatform Platform { get; init; }

    public required string SavePath { get; init; }

    public string? SteamUserId { get; init; }

    public string? SteamRoot { get; init; }

    public string? RemoteCachePath { get; init; }

    public bool Exists => Directory.Exists(SavePath);

    public int SaveFileCount =>
        Exists ? Directory.GetFiles(SavePath, "*", SearchOption.AllDirectories).Length : 0;

    public string DisplayName => Platform switch
    {
        SavePlatform.Steam => SteamUserId is null ? "Steam" : $"Steam (account {SteamUserId})",
        SavePlatform.Epic => "Epic Games",
        _ => "Manual path"
    };

    public override string ToString() => $"{DisplayName} - {SavePath}";
}
