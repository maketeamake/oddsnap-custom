using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using OddSnap.Models;

namespace OddSnap.Services;

public static class ClipboardService
{
    public const string EditableAnnotationsFormat = "OddSnap.EditableAnnotations.v1";
    public const string AutomaticClipboardTokenFormat = "OddSnap.AutomaticClipboardToken.v1";
    private const long MaxPngClipboardPixels = 12_000_000;

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

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

    public static void CopyPreparedImageToClipboard(
        Bitmap bitmap,
        ImageClipboardPayload payload,
        string? automaticClipboardToken = null)
        => SetClipboardWithRetry(CreateImageDataObject(bitmap, payload, automaticClipboardToken));

    public static void CopyAnnotationsToClipboard(
        Bitmap bitmap,
        IReadOnlyList<Annotation> annotations,
        string? filePath = null)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(annotations);

        var payload = PrepareImageClipboardPayload(bitmap, filePath);
        var dataObject = CreateImageDataObject(bitmap, payload, automaticClipboardToken: null);
        try
        {
            var json = EditableScreenshotService.SerializeAnnotations(annotations);
            if (json.Length <= 4 * 1024 * 1024)
                dataObject.SetData(EditableAnnotationsFormat, false, json);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            AppDiagnostics.LogWarning(
                "clipboard.copy-annotations",
                "Editable clipboard payload was omitted; bitmap copy remains available.",
                ex);
        }

        SetClipboardWithRetry(dataObject);
    }

    public static bool TryGetAnnotationsFromClipboard(out List<Annotation> annotations)
    {
        annotations = new List<Annotation>();
        try
        {
            if (!System.Windows.Forms.Clipboard.TryGetData<string>(EditableAnnotationsFormat, out var json))
                return false;

            return EditableScreenshotService.TryDeserializeAnnotations(json, out annotations);
        }
        catch (ExternalException)
        {
            return false;
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("clipboard.read-annotations", "Failed to read editable clipboard data.", ex);
            return false;
        }
    }

    public static bool TryGetClipboardToken(out string? token)
    {
        token = null;
        try
        {
            if (!System.Windows.Forms.Clipboard.TryGetData<string>(AutomaticClipboardTokenFormat, out var value))
                return false;

            token = value;
            return !string.IsNullOrWhiteSpace(token);
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("clipboard.read-token", "Failed to inspect clipboard ownership.", ex);
            return false;
        }
    }

    public static uint GetClipboardSequenceNumberSafe()
    {
        try
        {
            return GetClipboardSequenceNumber();
        }
        catch (Exception ex)
        {
            AppDiagnostics.LogWarning("clipboard.sequence", "Failed to read clipboard sequence number.", ex);
            return 0;
        }
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

    private static System.Windows.Forms.DataObject CreateImageDataObject(
        Bitmap bitmap,
        ImageClipboardPayload payload,
        string? automaticClipboardToken)
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

        if (!string.IsNullOrWhiteSpace(automaticClipboardToken))
            dataObject.SetData(AutomaticClipboardTokenFormat, false, automaticClipboardToken);

        return dataObject;
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
