using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using DLBeastSaveManager.Models;

namespace DLBeastSaveManager.Services;

public sealed class SnapshotIndex
{
    public const string ManifestEntryName = "_dlbsm_manifest.json";

    public const string FilesPrefix = "files/";

    public const string IndexFileName = "index.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public int SchemaVersion { get; set; } = 1;

    public List<Snapshot> Snapshots { get; set; } = new();

    public static string IndexPath(string backupRoot) => Path.Combine(backupRoot, IndexFileName);

    public static SnapshotIndex Load(string backupRoot)
    {
        Directory.CreateDirectory(backupRoot);

        SnapshotIndex? index = null;
        var path = IndexPath(backupRoot);
        if (File.Exists(path))
        {
            try
            {
                index = JsonSerializer.Deserialize<SnapshotIndex>(File.ReadAllText(path), JsonOptions);
            }
            catch (Exception)
            {
                index = null;
            }
        }

        index ??= new SnapshotIndex();

        var zipsOnDisk = Directory
            .EnumerateFiles(backupRoot, "*.zip", SearchOption.TopDirectoryOnly)
            .Select(p => (Id: Path.GetFileNameWithoutExtension(p) ?? string.Empty, Path: p))
            .Where(t => t.Id.Length > 0)
            .ToDictionary(t => t.Id, t => t.Path, StringComparer.OrdinalIgnoreCase);

        index.Snapshots.RemoveAll(s => !zipsOnDisk.ContainsKey(s.Id));

        var known = index.Snapshots.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, zipPath) in zipsOnDisk)
        {
            if (known.Contains(id)) continue;
            var recovered = ReadManifest(zipPath);
            if (recovered is not null) index.Snapshots.Add(recovered);
        }

        index.Sort();
        return index;
    }

    public static SnapshotIndex Rebuild(string backupRoot)
    {
        Directory.CreateDirectory(backupRoot);
        var index = new SnapshotIndex();

        foreach (var zipPath in Directory.EnumerateFiles(backupRoot, "*.zip", SearchOption.TopDirectoryOnly))
        {
            var snapshot = ReadManifest(zipPath);
            if (snapshot is not null) index.Snapshots.Add(snapshot);
        }

        index.Sort();
        return index;
    }

    public void Save(string backupRoot)
    {
        Directory.CreateDirectory(backupRoot);
        Sort();

        var path = IndexPath(backupRoot);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));

        if (File.Exists(path)) File.Replace(temp, path, destinationBackupFileName: null);
        else File.Move(temp, path);
    }

    public void Sort() =>
        Snapshots.Sort((a, b) => b.CreatedUtc.CompareTo(a.CreatedUtc));

    public Snapshot? Newest => Snapshots.Count > 0 ? Snapshots[0] : null;

    public Snapshot? ById(string id) =>
        Snapshots.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    public sealed record RunChange(IReadOnlyList<string> Changed, IReadOnlyList<string> Removed)
    {
        public static RunChange None { get; } = new(Array.Empty<string>(), Array.Empty<string>());
    }

    public Dictionary<string, RunChange> RunChanges()
    {
        var result = new Dictionary<string, RunChange>(StringComparer.OrdinalIgnoreCase);
        var oldestFirst = Snapshots.OrderBy(s => s.CreatedUtc).ToList();

        IReadOnlyList<string> carried = Array.Empty<string>();
        Snapshot? previous = null;

        foreach (var snapshot in oldestFirst)
        {
            var change = Compare(previous, snapshot, carried);
            if (change.Changed.Count > 0) carried = change.Changed;

            result[snapshot.Id] = change;
            previous = snapshot;
        }

        return result;
    }

    private static RunChange Compare(Snapshot? previous, Snapshot current, IReadOnlyList<string> carried)
    {
        if (current.Files.Count == 0) return new RunChange(carried, Array.Empty<string>());
        if (previous is null) return new RunChange(RunKeys(current), Array.Empty<string>());
        if (previous.Files.Count == 0) return new RunChange(carried, Array.Empty<string>());

        var changed = new List<string>();
        var removed = new List<string>();

        foreach (var key in RunKeys(previous).Union(RunKeys(current), StringComparer.Ordinal).OrderBy(SlotOrder))
        {
            var before = FilesOfRun(previous, key);
            var after = FilesOfRun(current, key);

            if (after.Count == 0 && before.Count > 0) removed.Add(key);
            else if (!SameFiles(before, after)) changed.Add(key);
        }

        if (changed.Count == 0 && removed.Count == 0) return new RunChange(carried, Array.Empty<string>());

        return new RunChange(changed, removed);
    }

    private static int SlotOrder(string key) =>
        int.TryParse(key, out var n) ? n : int.MaxValue;

    private static IReadOnlyList<string> RunKeys(Snapshot snapshot) =>
        snapshot.Files.Select(f => SaveRuns.KeyFor(f.Path))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(SlotOrder)
            .ToList();

    private static Dictionary<string, string> FilesOfRun(Snapshot snapshot, string key) =>
        snapshot.Files
            .Where(f => SaveRuns.KeyFor(f.Path) == key)
            .GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Sha256, StringComparer.OrdinalIgnoreCase);

    private static bool SameFiles(Dictionary<string, string> a, Dictionary<string, string> b) =>
        a.Count == b.Count &&
        a.All(kv => b.TryGetValue(kv.Key, out var sha) &&
                    string.Equals(sha, kv.Value, StringComparison.OrdinalIgnoreCase));

    public bool BackfillRuns(string backupRoot)
    {
        var filled = false;

        foreach (var snapshot in Snapshots)
        {
            if (snapshot.Files.Count > 0) continue;

            var zipPath = Path.Combine(backupRoot, snapshot.ZipFileName);
            if (!File.Exists(zipPath)) continue;

            try
            {
                using var archive = ZipFile.OpenRead(zipPath);

                var files = new List<SnapshotFile>();
                var descriptions = new Dictionary<string, SaveDescription>(StringComparer.Ordinal);

                foreach (var entry in archive.Entries)
                {
                    if (!entry.FullName.StartsWith(FilesPrefix, StringComparison.Ordinal)) continue;

                    var relative = entry.FullName[FilesPrefix.Length..];
                    if (relative.Length == 0 || relative.EndsWith('/')) continue;

                    using var stream = entry.Open();
                    using var buffer = new MemoryStream();
                    stream.CopyTo(buffer);
                    var bytes = buffer.ToArray();

                    files.Add(new SnapshotFile
                    {
                        Path = relative,
                        Length = bytes.Length,
                        Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
                    });

                    var key = SaveRuns.KeyFor(relative);
                    if (SaveRuns.IsCampaignSave(relative) || !descriptions.ContainsKey(key))
                        descriptions[key] = SaveMetadataReader.Read(new MemoryStream(bytes));
                }

                if (files.Count == 0) continue;

                snapshot.Files = files;
                snapshot.Runs = SaveRuns.Group(files.Select(f => f.Path))
                    .Select(run => descriptions.TryGetValue(run.Key, out var d)
                        ? d.ToRunInfo(run.Key)
                        : SaveDescription.Empty.ToRunInfo(run.Key))
                    .ToList();

                filled = true;
            }
            catch (Exception)
            {
            }
        }

        return filled;
    }

    public static Snapshot? ReadManifest(string zipPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var entry = archive.GetEntry(ManifestEntryName);
            if (entry is not null)
            {
                using var stream = entry.Open();
                var snapshot = JsonSerializer.Deserialize<Snapshot>(stream, JsonOptions);
                if (snapshot is not null && !string.IsNullOrEmpty(snapshot.Id)) return snapshot;
            }

            var files = archive.Entries
                .Where(e => e.FullName.StartsWith(FilesPrefix, StringComparison.Ordinal) && e.Length > 0)
                .ToList();
            if (files.Count == 0) return null;

            return new Snapshot
            {
                Id = Path.GetFileNameWithoutExtension(zipPath),
                CreatedUtc = File.GetLastWriteTimeUtc(zipPath),
                SetHash = string.Empty,
                Trigger = SnapshotTrigger.Manual,
                Label = "(recovered)",
                FileCount = files.Count,
                SizeBytes = files.Sum(e => e.Length)
            };
        }
        catch (Exception)
        {
            return null;
        }
    }
}
