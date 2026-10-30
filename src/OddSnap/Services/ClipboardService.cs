using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;

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

        // DataFormats.Bitmap hands the clipboard a GDI bitmap that is never freed,
        // leaking one full-size frame per copy. A DIB stream gives the same pixels
        // and Windows synthesizes CF_BITMAP from it for apps that ask.
        dataObject.SetData(System.Windows.Forms.DataFormats.Dib, false, CreateDibStream(bitmap));

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

    public static bool TryGetImageFromClipboard(out Bitmap? bitmap, int maxRetries = 3)
    {
        bitmap = null;
        Exception? lastError = null;
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                if (!System.Windows.Forms.Clipboard.ContainsImage())
                    return false;

                using var image = System.Windows.Forms.Clipboard.GetImage();
                if (image is null)
                    return false;
                bitmap = new Bitmap(image);
                return true;
            }
            catch (ExternalException ex) when (i < maxRetries - 1)
            {
                lastError = ex;
                System.Threading.Thread.Sleep(50 * (i + 1));
            }
            catch (Exception ex)
            {
                lastError = ex;
                break;
            }
        }

        throw new InvalidOperationException(
            "Windows clipboard is busy or the copied image is unavailable. Try again in a moment.",
            lastError);
    }

    private static MemoryStream CreateDibStream(Bitmap bitmap)
    {
        const int headerSize = 40;
        int stride = bitmap.Width * 4;
        var dib = new byte[headerSize + stride * bitmap.Height];
        BitConverter.TryWriteBytes(dib.AsSpan(0), headerSize);
        BitConverter.TryWriteBytes(dib.AsSpan(4), bitmap.Width);
        BitConverter.TryWriteBytes(dib.AsSpan(8), bitmap.Height); // positive height = bottom-up rows
        BitConverter.TryWriteBytes(dib.AsSpan(12), (short)1);
        BitConverter.TryWriteBytes(dib.AsSpan(14), (short)32);
        BitConverter.TryWriteBytes(dib.AsSpan(20), stride * bitmap.Height);

        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        using var opaque = new Bitmap(bitmap.Width, bitmap.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        using (var g = Graphics.FromImage(opaque))
        {
            // Same backdrop Bitmap.GetHbitmap() used for transparent pixels.
            g.Clear(Color.LightGray);
            g.DrawImage(bitmap, rect);
        }

        var bits = opaque.LockBits(
            rect,
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(
                    bits.Scan0 + y * bits.Stride,
                    dib,
                    headerSize + (bitmap.Height - 1 - y) * stride,
                    stride);
            }
        }
        finally
        {
            opaque.UnlockBits(bits);
        }

        return new MemoryStream(dib, writable: false);
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
