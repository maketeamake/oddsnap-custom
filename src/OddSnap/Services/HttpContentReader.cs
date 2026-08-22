using System.IO;
using System.Net.Http;
using System.Text;

namespace OddSnap.Services;

internal static class HttpContentReader
{
    public static async Task<byte[]> ReadLimitedBytesAsync(
        HttpContent content,
        long maxBytes,
        CancellationToken cancellationToken = default)
    {
        if (maxBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));

        var contentLength = content.Headers.ContentLength;
        if (contentLength.HasValue && contentLength.Value > maxBytes)
            throw new InvalidOperationException($"Response is {FormatBytes(contentLength.Value)}, above the {FormatBytes(maxBytes)} limit.");

        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = contentLength is > 0 and <= int.MaxValue
            ? new MemoryStream((int)contentLength.Value)
            : new MemoryStream();

        var buffer = new byte[64 * 1024];
        long totalRead = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                break;

            totalRead += read;
            if (totalRead > maxBytes)
                throw new InvalidOperationException($"Response exceeded the {FormatBytes(maxBytes)} limit.");

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    public static async Task<string> ReadLimitedStringAsync(
        HttpContent content,
        long maxBytes,
        CancellationToken cancellationToken = default)
    {
        var bytes = await ReadLimitedBytesAsync(content, maxBytes, cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(bytes);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
            return $"{bytes / (1024d * 1024 * 1024):0.#} GB";
        if (bytes >= 1024L * 1024)
            return $"{bytes / (1024d * 1024):0.#} MB";
        if (bytes >= 1024)
            return $"{bytes / 1024d:0.#} KB";
        return $"{bytes} bytes";
    }
}
