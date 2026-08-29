using System.IO.Compression;
using System.Text.Json;
using DLBeastSaveManager.Models;

namespace DLBeastSaveManager.Services;

public enum BackupOutcome
{
    Created,

    SkippedUnchanged,

    SkippedNoSaveFiles,

    Failed
}

public sealed record BackupResult(BackupOutcome Outcome, Snapshot? Snapshot, string Message)
{
    public bool Success => Outcome is BackupOutcome.Created;
}

public sealed record RestoreResult(
    bool Success,
    string Message,
    Snapshot? SafetySnapshot,
    int FilesRestored);

public sealed class RestoreOptions
{
    public bool TakeSafetySnapshot { get; init; } = true;

    public bool KeepReplacedFilesInTrash { get; init; } = true;

    public bool StampCurrentTimestamps { get; init; } = true;

    public bool ResetSteamCloudCache { get; init; }

    public string? RemoteCachePath { get; init; }

    public string? RunKey { get; init; }
}

public sealed class BackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public BackupService(string savePath, string backupRoot)
    {
        SavePath = savePath;
        BackupRoot = backupRoot;
        Directory.CreateDirectory(backupRoot);
        Index = LoadIndex(backupRoot);
    }

    public string SavePath { get; private set; }
    public string BackupRoot { get; private set; }
    public SnapshotIndex Index { get; private set; }

    public string TrashRoot => Path.Combine(BackupRoot, ".trash");

    public static string DefaultBackupRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DLBeastSaveManager", "backups");

    public void Retarget(string savePath, string backupRoot)
    {
        _gate.Wait();
        try
        {
            SavePath = savePath;
            if (!SteamLocator.PathsEqual(backupRoot, BackupRoot))
            {
                BackupRoot = backupRoot;
                Directory.CreateDirectory(backupRoot);
                Index = LoadIndex(backupRoot);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void ReloadIndex()
    {
        _gate.Wait();
        try { Index = LoadIndex(BackupRoot); }
        finally { _gate.Release(); }
    }

    private static SnapshotIndex LoadIndex(string backupRoot)
    {
        var index = SnapshotIndex.Load(backupRoot);

        try
        {
            if (index.BackfillRuns(backupRoot)) index.Save(backupRoot);
        }
        catch (Exception)
        {
        }

        return index;
    }

    public async Task<BackupResult> CreateSnapshotAsync(
        SnapshotTrigger trigger,
        AppSettings settings,
        string? label = null,
        bool pinned = false,
        bool force = false,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await CreateSnapshotCoreAsync(trigger, settings, label, pinned, force, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<BackupResult> CreateSnapshotCoreAsync(
        SnapshotTrigger trigger,
        AppSettings settings,
        string? label,
        bool pinned,
        bool force,
        CancellationToken ct)
    {
        if (!Directory.Exists(SavePath))
            return new BackupResult(BackupOutcome.SkippedNoSaveFiles,
                null, $"Save folder not found: {SavePath}");

        var set = await SaveSetHasher.HashAsync(SavePath, ct).ConfigureAwait(false);
        if (set.IsEmpty)
            return new BackupResult(BackupOutcome.SkippedNoSaveFiles,
                null, "Save folder is empty - nothing to back up.");

        if (!force && Index.Newest is { } newest &&
            string.Equals(newest.SetHash, set.SetHash, StringComparison.OrdinalIgnoreCase))
        {
            return new BackupResult(BackupOutcome.SkippedUnchanged, newest,
                "Save is unchanged since the last snapshot.");
        }

        var createdUtc = DateTime.UtcNow;
        var snapshot = new Snapshot
        {
            Id = MakeUniqueId(createdUtc, set.SetHash),
            CreatedUtc = createdUtc,
            SetHash = set.SetHash,
            Trigger = trigger,
            Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim(),
            Pinned = pinned,
            FileCount = set.Count,
            SizeBytes = set.TotalBytes,
            Files = set.Files
                .Select(f => new SnapshotFile { Path = f.RelativePath, Length = f.Length, Sha256 = f.Sha256 })
                .ToList(),
            Runs = DescribeRuns(set)
        };

        var zipPath = Path.Combine(BackupRoot, snapshot.ZipFileName);
        var tempPath = zipPath + ".tmp";

        try
        {
            await WriteZipAsync(tempPath, set, snapshot, ct).ConfigureAwait(false);
            File.Move(tempPath, zipPath, overwrite: true);
        }
        catch (Exception ex)
        {
            TryDelete(tempPath);
            return new BackupResult(BackupOutcome.Failed, null, $"Backup failed: {ex.Message}");
        }

        Index.Snapshots.Add(snapshot);
        Index.Save(BackupRoot);

        PruneCore(settings, DateTime.UtcNow);

        return new BackupResult(BackupOutcome.Created, snapshot,
            $"Snapshot saved ({set.Count} files, {FormatSize(set.TotalBytes)}).");
    }

    private async Task WriteZipAsync(string zipPath, SaveSet set, Snapshot snapshot, CancellationToken ct)
    {
        await using var zipStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);

        var manifestEntry = archive.CreateEntry(SnapshotIndex.ManifestEntryName, CompressionLevel.Optimal);
        await using (var manifestStream = manifestEntry.Open())
            await JsonSerializer.SerializeAsync(manifestStream, snapshot, JsonOptions, ct).ConfigureAwait(false);

        foreach (var file in set.Files)
        {
            ct.ThrowIfCancellationRequested();
            var source = Path.Combine(SavePath, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(source)) continue;

            var entry = archive.CreateEntry(SnapshotIndex.FilesPrefix + file.RelativePath, CompressionLevel.Optimal);
            entry.LastWriteTime = File.GetLastWriteTime(source);

            await using var input = new FileStream(
                source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024, useAsync: true);
            await using var output = entry.Open();
            await input.CopyToAsync(output, ct).ConfigureAwait(false);
        }
    }

    private List<RunInfo> DescribeRuns(SaveSet set)
    {
        var runs = new List<RunInfo>();

        foreach (var run in SaveRuns.Group(set.Files.Select(f => f.RelativePath)))
        {
            var primary = run.Files.FirstOrDefault(SaveRuns.IsCampaignSave) ?? run.Files[0];
            var full = Path.Combine(SavePath, primary.Replace('/', Path.DirectorySeparatorChar));
            runs.Add(SaveMetadataReader.ReadFile(full).ToRunInfo(run.Key));
        }

        return runs;
    }

    private string MakeUniqueId(DateTime createdUtc, string setHash)
    {
        var stamp = createdUtc.ToLocalTime().ToString("yyyy-MM-dd_HHmmss");
        var frag = setHash.Length >= 6 ? setHash[..6] : "000000";
        var id = $"{stamp}_{frag}";

        var suffix = 1;
        while (File.Exists(Path.Combine(BackupRoot, id + ".zip")) || Index.ById(id) is not null)
            id = $"{stamp}_{frag}_{suffix++}";

        return id;
    }

    public async Task<RestoreResult> RestoreAsync(
        Snapshot snapshot,
        AppSettings settings,
        RestoreOptions options,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var zipPath = Path.Combine(BackupRoot, snapshot.ZipFileName);
            if (!File.Exists(zipPath))
                return new RestoreResult(false, $"Snapshot file is missing: {zipPath}", null, 0);

            var runKey = options.RunKey;
            bool InScope(string relativePath) =>
                runKey is null || SaveRuns.KeyFor(relativePath) == runKey;

            if (runKey is not null && snapshot.Files.Count > 0 &&
                !snapshot.Files.Any(f => InScope(f.Path)))
            {
                return new RestoreResult(false,
                    $"This snapshot holds no files for {SaveRuns.NameFor(runKey)}.", null, 0);
            }

            Directory.CreateDirectory(SavePath);

            Snapshot? safety = null;
            if (options.TakeSafetySnapshot)
            {
                var result = await CreateSnapshotCoreAsync(
                    SnapshotTrigger.PreRestore, settings,
                    label: $"before restoring {snapshot.CreatedLocal:HH:mm:ss}",
                    pinned: true, force: false, ct).ConfigureAwait(false);

                safety = result.Snapshot;
                if (result.Outcome == BackupOutcome.Failed)
                    return new RestoreResult(false,
                        $"Aborted - could not take a safety snapshot first. {result.Message}", null, 0);
            }

            try
            {
                ClearSaveFolder(options.KeepReplacedFilesInTrash, InScope);
            }
            catch (Exception ex)
            {
                return new RestoreResult(false,
                    $"Could not clear the save folder (is the game running?): {ex.Message}", safety, 0);
            }

            var restored = 0;
            try
            {
                using var archive = ZipFile.OpenRead(zipPath);
                foreach (var entry in archive.Entries)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!entry.FullName.StartsWith(SnapshotIndex.FilesPrefix, StringComparison.Ordinal)) continue;

                    var relative = entry.FullName[SnapshotIndex.FilesPrefix.Length..];
                    if (string.IsNullOrEmpty(relative) || relative.EndsWith('/')) continue;
                    if (!InScope(relative)) continue;

                    var destination = SafeCombine(SavePath, relative);
                    if (destination is null) continue;

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination, overwrite: true);

                    File.SetLastWriteTime(destination,
                        options.StampCurrentTimestamps ? DateTime.Now : entry.LastWriteTime.LocalDateTime);
                    restored++;
                }
            }
            catch (Exception ex)
            {
                return new RestoreResult(false, $"Restore failed while extracting: {ex.Message}", safety, restored);
            }

            if (options.ResetSteamCloudCache && !string.IsNullOrWhiteSpace(options.RemoteCachePath))
                TryDelete(options.RemoteCachePath!);

            var what = runKey is null ? string.Empty : $" of {SaveRuns.NameFor(runKey)}";
            return new RestoreResult(true,
                $"Restored {restored} file{(restored == 1 ? "" : "s")}{what} from {snapshot.CreatedLocal:HH:mm:ss}.",
                safety, restored);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ClearSaveFolder(bool useTrash, Func<string, bool> inScope)
    {
        var files = Directory
            .GetFiles(SavePath, "*", SearchOption.AllDirectories)
            .Where(f => inScope(SaveSetHasher.NormalizeRelative(SavePath, f)))
            .ToArray();

        if (files.Length == 0) return;

        if (!useTrash)
        {
            foreach (var file in files) File.Delete(file);
            return;
        }

        var bin = Path.Combine(TrashRoot, DateTime.Now.ToString("yyyy-MM-dd_HHmmss"));
        Directory.CreateDirectory(bin);

        foreach (var file in files)
        {
            var relative = SaveSetHasher.NormalizeRelative(SavePath, file);
            var destination = Path.Combine(bin, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(file, destination, overwrite: true);
        }
    }

    private static string? SafeCombine(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        return full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    public void SetPinned(Snapshot snapshot, bool pinned)
    {
        snapshot.Pinned = pinned;
        UpdateManifest(snapshot);
        Index.Save(BackupRoot);
    }

    public void SetLabel(Snapshot snapshot, string? label)
    {
        snapshot.Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        UpdateManifest(snapshot);
        Index.Save(BackupRoot);
    }

    public bool Delete(Snapshot snapshot)
    {
        var ok = TryDelete(Path.Combine(BackupRoot, snapshot.ZipFileName));
        Index.Snapshots.RemoveAll(s => s.Id == snapshot.Id);
        Index.Save(BackupRoot);
        return ok;
    }

    public int Prune(AppSettings settings)
    {
        _gate.Wait();
        try { return PruneCore(settings, DateTime.UtcNow); }
        finally { _gate.Release(); }
    }

    private int PruneCore(AppSettings settings, DateTime nowUtc)
    {
        var doomed = RetentionPolicy.SelectForDeletion(Index.Snapshots, settings, nowUtc);
        if (doomed.Count == 0) return 0;

        foreach (var snapshot in doomed)
        {
            TryDelete(Path.Combine(BackupRoot, snapshot.ZipFileName));
            Index.Snapshots.RemoveAll(s => s.Id == snapshot.Id);
        }

        Index.Save(BackupRoot);
        return doomed.Count;
    }

    public long TotalBackupBytes()
    {
        try
        {
            return Directory.EnumerateFiles(BackupRoot, "*.zip", SearchOption.TopDirectoryOnly)
                .Sum(p => new FileInfo(p).Length);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private void UpdateManifest(Snapshot snapshot)
    {
        var zipPath = Path.Combine(BackupRoot, snapshot.ZipFileName);
        if (!File.Exists(zipPath)) return;

        try
        {
            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Update);
            archive.GetEntry(SnapshotIndex.ManifestEntryName)?.Delete();
            var entry = archive.CreateEntry(SnapshotIndex.ManifestEntryName, CompressionLevel.Optimal);
            using var stream = entry.Open();
            JsonSerializer.Serialize(stream, snapshot, JsonOptions);
        }
        catch (Exception)
        {
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB"
    };
}
