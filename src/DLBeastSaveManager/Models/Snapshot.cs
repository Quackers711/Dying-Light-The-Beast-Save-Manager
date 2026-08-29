using System.Text.Json.Serialization;

namespace DLBeastSaveManager.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SnapshotTrigger
{
    Auto,

    Manual,

    Hotkey,

    Interval,

    PreRestore,

    GameExit
}

public sealed class SnapshotFile
{
    public string Path { get; set; } = string.Empty;
    public long Length { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class RunInfo
{
    public string Key { get; set; } = string.Empty;

    public string? Difficulty { get; set; }
    public string? Area { get; set; }
    public string? Checkpoint { get; set; }
    public string? GameVersion { get; set; }
}

public sealed class Snapshot
{
    public string Id { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }

    public string SetHash { get; set; } = string.Empty;

    public SnapshotTrigger Trigger { get; set; } = SnapshotTrigger.Auto;

    public string? Label { get; set; }

    public bool Pinned { get; set; }

    public int FileCount { get; set; }

    public long SizeBytes { get; set; }

    public List<SnapshotFile> Files { get; set; } = new();

    public List<RunInfo> Runs { get; set; } = new();

    [JsonIgnore]
    public DateTime CreatedLocal => CreatedUtc.ToLocalTime();

    public string ZipFileName => Id + ".zip";
}
