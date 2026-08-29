using System.Security.Cryptography;
using System.Text;

namespace DLBeastSaveManager.Services;

public sealed record SaveFileEntry(string RelativePath, long Length, string Sha256);

public sealed record SaveSet(string SetHash, IReadOnlyList<SaveFileEntry> Files)
{
    public long TotalBytes => Files.Sum(f => f.Length);
    public int Count => Files.Count;
    public static SaveSet Empty { get; } = new(string.Empty, Array.Empty<SaveFileEntry>());
    public bool IsEmpty => Files.Count == 0;
}

public static class SaveSetHasher
{
    private const int ReadAttempts = 4;
    private const int ReadRetryDelayMs = 250;

    public static SaveSet Hash(string folder) => HashAsync(folder).GetAwaiter().GetResult();

    public static async Task<SaveSet> HashAsync(string folder, CancellationToken ct = default)
    {
        if (!Directory.Exists(folder)) return SaveSet.Empty;

        var files = Directory
            .EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(full => (Full: full, Rel: NormalizeRelative(folder, full)))
            .OrderBy(t => t.Rel, StringComparer.Ordinal)
            .ToList();

        var entries = new List<SaveFileEntry>(files.Count);
        foreach (var (full, rel) in files)
        {
            ct.ThrowIfCancellationRequested();
            var (length, sha) = await HashFileAsync(full, ct).ConfigureAwait(false);
            entries.Add(new SaveFileEntry(rel, length, sha));
        }

        return new SaveSet(CombineHashes(entries), entries);
    }

    public static string CombineHashes(IEnumerable<SaveFileEntry> entries)
    {
        var ordered = entries.OrderBy(e => e.RelativePath, StringComparer.Ordinal).ToList();
        if (ordered.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        foreach (var e in ordered)
            sb.Append(e.RelativePath).Append(' ')
              .Append(e.Length).Append(' ')
              .Append(e.Sha256).Append('\n');

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    public static async Task<(long Length, string Sha256)> HashFileAsync(string path, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 64 * 1024, useAsync: true);

                var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
                return (stream.Length, Convert.ToHexString(hash).ToLowerInvariant());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < ReadAttempts)
            {
                await Task.Delay(ReadRetryDelayMs, ct).ConfigureAwait(false);
            }
        }
    }

    public static string NormalizeRelative(string root, string fullPath) =>
        Path.GetRelativePath(root, fullPath).Replace('\\', '/');
}
