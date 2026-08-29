using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using DLBeastSaveManager.Models;

namespace DLBeastSaveManager.Services;

public static class SaveMetadataReader
{
    private const int ScanBytes = 32 * 1024;

    private const int MinStringLength = 3;
    private const int MaxStringLength = 200;

    private static readonly byte[] GzipMagic = { 0x1f, 0x8b, 0x08 };

    private static readonly string[] Difficulties =
        { "Story", "Easy", "Normal", "Hard", "Nightmare" };

    private const string VersionPrefix = "EVersion::";

    private static readonly Regex ChapterPattern =
        new(@"^chp(?<n>\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex CamelBoundary =
        new(@"(?<=[a-z0-9])(?=[A-Z])", RegexOptions.CultureInvariant);

    public static SaveDescription ReadFile(string path)
    {
        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Read(stream);
        }
        catch (Exception)
        {
            return SaveDescription.Empty;
        }
    }

    public static SaveDescription Read(Stream stream)
    {
        try
        {
            var head = ReadHead(stream);
            return head.Length == 0 ? SaveDescription.Empty : Describe(head);
        }
        catch (Exception)
        {
            return SaveDescription.Empty;
        }
    }

    private static byte[] ReadHead(Stream stream)
    {
        var probe = new byte[3];
        var probed = ReadExactly(stream, probe, probe.Length);

        var buffer = new byte[ScanBytes];
        int read;

        if (probed == 3 && probe.SequenceEqual(GzipMagic))
        {
            Stream source;
            if (stream.CanSeek)
            {
                stream.Position = 0;
                source = stream;
            }
            else
            {
                source = new ConcatStream(probe, stream);
            }

            using var gzip = new GZipStream(source, CompressionMode.Decompress, leaveOpen: true);
            read = ReadExactly(gzip, buffer, buffer.Length);
        }
        else
        {
            Array.Copy(probe, buffer, probed);
            read = probed + ReadExactly(stream, buffer.AsSpan(probed), buffer.Length - probed);
        }

        if (read == buffer.Length) return buffer;
        var exact = new byte[read];
        Array.Copy(buffer, exact, read);
        return exact;
    }

    private static SaveDescription Describe(byte[] data)
    {
        var strings = ExtractStrings(data);

        string? version = null, difficulty = null, checkpoint = null, chapter = null;
        string? zone = null, map = null;

        foreach (var s in strings)
        {
            if (version is null && s.StartsWith(VersionPrefix, StringComparison.Ordinal))
                version = s[VersionPrefix.Length..];

            if (difficulty is null && Array.IndexOf(Difficulties, s) >= 0)
                difficulty = s;

            if (checkpoint is null && s.Length > "Checkpoint".Length &&
                s.EndsWith("Checkpoint", StringComparison.Ordinal))
                checkpoint = Humanise(s);

            if (chapter is null && ChapterPattern.Match(s) is { Success: true } m)
            {
                var number = m.Groups["n"].Value.TrimStart('0');
                chapter = $"Chapter {(number.Length > 0 ? number : "0")}";
            }

            if (s.Contains('/') && s.StartsWith("dlc_", StringComparison.OrdinalIgnoreCase))
            {
                zone ??= ZoneFrom(s);
                map ??= PrettyMap(s.Split('/')[0]);
            }
        }

        return new SaveDescription(difficulty, zone ?? map, checkpoint ?? chapter, version);
    }

    private static string? ZoneFrom(string path)
    {
        var parts = path.Split('/');
        for (var i = 0; i < parts.Length - 1; i++)
            if (string.Equals(parts[i], "Zones", StringComparison.OrdinalIgnoreCase))
                return Humanise(parts[i + 1]);

        return null;
    }

    private static string PrettyMap(string token)
    {
        var t = token.TrimStart('^');
        if (t.StartsWith("dlc_", StringComparison.OrdinalIgnoreCase)) t = t[4..];
        if (t.StartsWith("ft_", StringComparison.OrdinalIgnoreCase)) t = t[3..];

        t = t.Replace('_', ' ').Trim();
        if (t.Length == 0) return token;

        return char.ToUpperInvariant(t[0]) + t[1..];
    }

    private static string Humanise(string value)
    {
        var spaced = CamelBoundary.Replace(value.Replace('_', ' '), " ").Trim();
        if (spaced.Length == 0) return value;

        var lowered = spaced[0] + spaced[1..].ToLowerInvariant();
        return char.ToUpperInvariant(lowered[0]) + lowered[1..];
    }

    private static IEnumerable<string> ExtractStrings(byte[] data)
    {
        for (var i = 0; i + 2 < data.Length; i++)
        {
            int length = data[i] | (data[i + 1] << 8);
            if (length is < MinStringLength or > MaxStringLength) continue;
            if (i + 2 + length > data.Length) continue;

            var printable = true;
            for (var j = i + 2; j < i + 2 + length; j++)
            {
                if (data[j] is >= 0x20 and <= 0x7E) continue;
                printable = false;
                break;
            }

            if (printable) yield return Encoding.ASCII.GetString(data, i + 2, length);
        }
    }

    private static int ReadExactly(Stream stream, byte[] buffer, int count) =>
        ReadExactly(stream, buffer.AsSpan(0, count), count);

    private static int ReadExactly(Stream stream, Span<byte> buffer, int count)
    {
        var total = 0;
        while (total < count)
        {
            var read = stream.Read(buffer[total..]);
            if (read == 0) break;
            total += read;
        }

        return total;
    }

    private sealed class ConcatStream : Stream
    {
        private readonly byte[] _head;
        private readonly Stream _rest;
        private int _headPosition;

        public ConcatStream(byte[] head, Stream rest)
        {
            _head = head;
            _rest = rest;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_headPosition < _head.Length)
            {
                var take = Math.Min(count, _head.Length - _headPosition);
                Array.Copy(_head, _headPosition, buffer, offset, take);
                _headPosition += take;
                return take;
            }

            return _rest.Read(buffer, offset, count);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

public sealed record SaveDescription(
    string? Difficulty,
    string? Area,
    string? Checkpoint,
    string? GameVersion)
{
    public static SaveDescription Empty { get; } = new(null, null, null, null);

    public bool IsEmpty => Difficulty is null && Area is null && Checkpoint is null && GameVersion is null;

    public RunInfo ToRunInfo(string key) => new()
    {
        Key = key,
        Difficulty = Difficulty,
        Area = Area,
        Checkpoint = Checkpoint,
        GameVersion = GameVersion
    };
}
