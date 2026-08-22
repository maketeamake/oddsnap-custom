using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace OddSnap.Services;

internal static class HistoryEntryUtilities
{
    public static string GetStablePathKey(string path)
    {
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
#pragma warning disable CA5350 // Compatibility ID only; changing the hash would orphan existing history metadata.
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(normalizedPath))).ToLowerInvariant();
#pragma warning restore CA5350
    }

    public static bool IsSupportedHistoryFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" or ".mp4" or ".webm" or ".mkv";
    }

    public static HistoryEntry CloneEntry(HistoryEntry entry)
    {
        return new HistoryEntry
        {
            FileName = entry.FileName,
            FilePath = entry.FilePath,
            CapturedAt = entry.CapturedAt,
            Width = entry.Width,
            Height = entry.Height,
            FileSizeBytes = entry.FileSizeBytes,
            Kind = entry.Kind,
            UploadUrl = entry.UploadUrl,
            UploadProvider = entry.UploadProvider,
            UploadError = entry.UploadError,
            ForegroundProcessName = entry.ForegroundProcessName,
            ForegroundWindowTitle = entry.ForegroundWindowTitle,
            Tags = entry.Tags
        };
    }

    public static string BuildMetadataSearchText(HistoryEntry entry)
    {
        return string.Join(
            ' ',
            new[] { entry.ForegroundProcessName, entry.ForegroundWindowTitle, entry.Tags }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    public static string NormalizeTags(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
            return "";

        return string.Join(
            ", ",
            tags.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(tag => tag.Trim().TrimStart('#').Trim())
                .Where(tag => tag.Length > 0)
                .Select(tag => tag.Length > 48 ? tag[..48] : tag)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(20));
    }

    public static HistoryKind GetKindForPath(string path, HistoryKind? fallback = null, params string[] stickerDirs)
    {
        foreach (var stickerDir in stickerDirs)
        {
            if (!string.IsNullOrWhiteSpace(stickerDir) &&
                IsPathWithinDirectory(path, stickerDir))
            {
                return HistoryKind.Sticker;
            }
        }

        if (Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase))
            return HistoryKind.Gif;

        if (IsVideoPath(path))
            return HistoryKind.Video;

        return fallback ?? HistoryKind.Image;
    }

    internal static bool IsPathWithinDirectory(string path, string directory)
    {
        try
        {
            var relativePath = Path.GetRelativePath(
                Path.GetFullPath(directory),
                Path.GetFullPath(path));

            return !Path.IsPathRooted(relativePath) &&
                   !relativePath.Equals("..", StringComparison.Ordinal) &&
                   !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                   !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsVideoPath(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".webm", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".mkv", StringComparison.OrdinalIgnoreCase);
    }
}
