using System.Drawing;
using System.IO;
using System.Collections.Specialized;

namespace OddSnap.Services;

public static class ClipboardService
{
    private const long MaxPngClipboardPixels = 12_000_000;

    public sealed class ImageClipboardPayload
    {
        public ImageClipboardPayload(ArraySegment<byte>? pngBytes, string? filePath)
        {
            PngBytes = pngBytes;
            FilePath = filePath;
        }

        public ArraySegment<byte>? PngBytes { get; }
        public string? FilePath { get; }
    }

    public static void CopyToClipboard(Bitmap bitmap, string? filePath = null)
        => CopyPreparedImageToClipboard(bitmap, PrepareImageClipboardPayload(bitmap, filePath));

    public static ImageClipboardPayload PrepareImageClipboardPayload(Bitmap bitmap, string? filePath = null)
    {
        ArraySegment<byte>? pngBytes = null;
        if (ShouldIncludePngClipboardPayload(bitmap))
        {
            using var pngStream = new MemoryStream();
            CaptureOutputService.WritePng(bitmap, pngStream);
            pngBytes = pngStream.TryGetBuffer(out var pngBuffer)
                ? pngBuffer
                : new ArraySegment<byte>(pngStream.ToArray());
        }

        var existingFilePath = !string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath)
            ? filePath
            : null;

        return new ImageClipboardPayload(pngBytes, existingFilePath);
    }

    public static void CopyPreparedImageToClipboard(Bitmap bitmap, ImageClipboardPayload payload)
    {
        var dataObject = new System.Windows.Forms.DataObject();

        dataObject.SetData(System.Windows.Forms.DataFormats.Bitmap, bitmap);

        if (payload.PngBytes is { } pngBytes && pngBytes.Array is not null && pngBytes.Count > 0)
        {
            dataObject.SetData(
                "PNG",
                false,
                new MemoryStream(pngBytes.Array, pngBytes.Offset, pngBytes.Count, writable: false, publiclyVisible: true));
        }

        if (!string.IsNullOrWhiteSpace(payload.FilePath) && File.Exists(payload.FilePath))
        {
            dataObject.SetFileDropList(new StringCollection { payload.FilePath });
        }

        SetClipboardWithRetry(dataObject);
    }

    public static void CopyTextToClipboard(string text)
    {
        var dataObject = new System.Windows.Forms.DataObject();
        dataObject.SetData(System.Windows.Forms.DataFormats.UnicodeText, false, text);
        dataObject.SetData(System.Windows.Forms.DataFormats.Text, false, text);

        SetClipboardWithRetry(dataObject);
    }

    private static bool ShouldIncludePngClipboardPayload(Bitmap bitmap) =>
        (long)bitmap.Width * bitmap.Height <= MaxPngClipboardPixels;

    public static void CopyFilesToClipboard(params string[] filePaths)
        => CopyFilesToClipboard((IEnumerable<string>)filePaths);

    public static void CopyFilesToClipboard(IEnumerable<string> filePaths)
    {
        var files = new StringCollection();
        foreach (var filePath in filePaths)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                continue;

            if (!File.Exists(filePath))
                throw new FileNotFoundException("The file is no longer on disk.", filePath);

            files.Add(filePath);
        }

        if (files.Count == 0)
            throw new InvalidOperationException("No files are available to copy.");

        var dataObject = new System.Windows.Forms.DataObject();
        dataObject.SetFileDropList(files);
        SetClipboardWithRetry(dataObject);
    }

    private static void SetClipboardWithRetry(System.Windows.Forms.DataObject dataObject, int maxRetries = 3)
    {
        Exception? lastError = null;
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                System.Windows.Forms.Clipboard.SetDataObject(dataObject, true);
                return;
            }
            catch (Exception ex) when (i < maxRetries - 1)
            {
                lastError = ex;
                System.Threading.Thread.Sleep(50 * (i + 1));
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        if (lastError is not null)
        {
            AppDiagnostics.LogWarning("clipboard.set", "Failed to write to clipboard after retries.", lastError);
            throw new InvalidOperationException("Windows clipboard is busy or unavailable. Try again in a moment.", lastError);
        }
    }
}
