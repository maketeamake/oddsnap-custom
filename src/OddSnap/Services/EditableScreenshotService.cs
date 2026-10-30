using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OddSnap.Helpers;
using OddSnap.Models;

namespace OddSnap.Services;

/// <summary>
/// Stores the immutable pixels and editable annotation objects separately while
/// keeping the user's normal image file as a flattened, shareable result.
/// </summary>
public static class EditableScreenshotService
{
    private const int CurrentVersion = 1;
    private const int MaxClipboardAnnotationCount = 256;
    private const int MaxClipboardPointCount = 20_000;
    private const int MaxClipboardJsonBytes = 4 * 1024 * 1024;
    private const int MaxClipboardPngBytes = 16 * 1024 * 1024;
    private const int MaxClipboardCoordinate = 1_000_000;
    private const long MaxClipboardPixels = 12_000_000;
    private static readonly string ProjectDirectory = Path.Combine(HistoryService.HistoryDir, "cache", "projects");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, MaxDepth = 32 };

    public static EditableScreenshotProject Load(string imagePath)
    {
        var (projectPath, basePath) = GetProjectPaths(imagePath);
        if (File.Exists(projectPath) && File.Exists(basePath))
        {
            try
            {
                var stored = JsonSerializer.Deserialize<StoredProject>(File.ReadAllText(projectPath), JsonOptions);
                if (stored is { Version: CurrentVersion })
                {
                    var baseImage = BitmapPerf.LoadDetached(basePath);
                    return new EditableScreenshotProject(baseImage, stored.Annotations.Select(FromStored).ToList(), true);
                }
            }
            catch (Exception ex)
            {
                AppDiagnostics.LogWarning("editable-project.load", $"Could not load editable data for {Path.GetFileName(imagePath)}: {ex.Message}", ex);
            }
        }

        return new EditableScreenshotProject(BitmapPerf.LoadDetached(imagePath), [], false);
    }

    public static void SaveProject(string imagePath, Bitmap baseImage, IReadOnlyList<Annotation> annotations)
    {
        Directory.CreateDirectory(ProjectDirectory);
        var (projectPath, basePath) = GetProjectPaths(imagePath);
        // The base pixels are immutable for the lifetime of an editable project.
        // Re-encoding a large PNG after every arrow/text change was the main
        // source of multi-second stalls in the library editor.
        if (!File.Exists(basePath))
            CaptureOutputService.SavePng(baseImage, basePath);

        var stored = new StoredProject
        {
            Version = CurrentVersion,
            SourceFileName = Path.GetFileName(imagePath),
            Width = baseImage.Width,
            Height = baseImage.Height,
            Annotations = annotations.Select(ToStored).ToList()
        };
        WriteTextAtomic(projectPath, JsonSerializer.Serialize(stored, JsonOptions));
    }

    public static void ReplaceProjectBase(string imagePath, Bitmap baseImage, IReadOnlyList<Annotation> annotations)
    {
        Directory.CreateDirectory(ProjectDirectory);
        var (projectPath, basePath) = GetProjectPaths(imagePath);
        var temporaryBasePath = basePath + ".crop.tmp.png";
        try
        {
            CaptureOutputService.SavePng(baseImage, temporaryBasePath);
            File.Move(temporaryBasePath, basePath, true);
            var stored = new StoredProject
            {
                Version = CurrentVersion,
                SourceFileName = Path.GetFileName(imagePath),
                Width = baseImage.Width,
                Height = baseImage.Height,
                Annotations = annotations.Select(ToStored).ToList()
            };
            WriteTextAtomic(projectPath, JsonSerializer.Serialize(stored, JsonOptions));
        }
        finally
        {
            TryDelete(temporaryBasePath);
        }
    }

    public static void SaveInitialProject(
        string imagePath,
        Bitmap baseImage,
        IReadOnlyList<Annotation> annotations,
        int targetWidth,
        int targetHeight)
    {
        if (annotations.Count == 0)
            return;

        if (baseImage.Width == targetWidth && baseImage.Height == targetHeight)
        {
            SaveProject(imagePath, baseImage, annotations);
            return;
        }

        using var resized = Resize(baseImage, targetWidth, targetHeight);
        float sx = targetWidth / (float)baseImage.Width;
        float sy = targetHeight / (float)baseImage.Height;
        SaveProject(imagePath, resized, annotations.Select(a => Scale(a, sx, sy)).ToList());
    }

    public static void SaveFlattenedImage(string imagePath, Bitmap bitmap, int jpegQuality)
    {
        var extension = Path.GetExtension(imagePath).ToLowerInvariant();
        var format = extension switch
        {
            ".jpg" or ".jpeg" => CaptureImageFormat.Jpeg,
            ".bmp" => CaptureImageFormat.Bmp,
            _ => CaptureImageFormat.Png
        };
        CaptureOutputService.SaveBitmap(bitmap, imagePath, format, jpegQuality);
    }

    public static void DeleteProject(string imagePath)
    {
        var (projectPath, basePath) = GetProjectPaths(imagePath);
        TryDelete(projectPath);
        TryDelete(basePath);
    }

    internal static string SerializeAnnotations(IReadOnlyList<Annotation> annotations)
    {
        ArgumentNullException.ThrowIfNull(annotations);
        if (annotations.Count > MaxClipboardAnnotationCount)
            throw new ArgumentOutOfRangeException(nameof(annotations));

        return JsonSerializer.Serialize(annotations.Select(ToStored).ToList(), JsonOptions);
    }

    internal static bool TryDeserializeAnnotations(string? json, out List<Annotation> annotations)
    {
        annotations = new List<Annotation>();
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxClipboardJsonBytes)
            return false;

        try
        {
            var stored = JsonSerializer.Deserialize<List<StoredAnnotation>>(json, JsonOptions);
            if (stored is null || stored.Count == 0 || stored.Count > MaxClipboardAnnotationCount)
                return false;

            foreach (var value in stored)
            {
                if (!IsSafeClipboardAnnotation(value))
                    return false;
            }

            var restored = stored.Select(FromStored).ToList();
            foreach (var fragment in restored.OfType<ImageFragmentAnnotation>())
            {
                if (fragment.PngData.Length > MaxClipboardPngBytes)
                    return false;

                using var stream = new MemoryStream(fragment.PngData, writable: false);
                using var bitmap = new Bitmap(stream);
                if (bitmap.Width <= 0 || bitmap.Height <= 0 || (long)bitmap.Width * bitmap.Height > MaxClipboardPixels)
                    return false;
            }

            annotations = restored;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or ArgumentException
            or NotSupportedException or OverflowException or OutOfMemoryException)
        {
            AppDiagnostics.LogWarning("editable-project.clipboard", "Rejected invalid editable clipboard payload.", ex);
            return false;
        }
    }

    internal static List<Annotation> CloneAnnotations(IReadOnlyList<Annotation> annotations)
    {
        ArgumentNullException.ThrowIfNull(annotations);
        return annotations.Select(annotation => FromStored(ToStored(annotation))).ToList();
    }

    private static bool IsSafeClipboardAnnotation(StoredAnnotation value)
    {
        if (value is null || string.IsNullOrWhiteSpace(value.Kind))
            return false;
        if (value.Points is { Count: > MaxClipboardPointCount }
            || value.Points?.Any(point => point is null
                || point.X is < -MaxClipboardCoordinate or > MaxClipboardCoordinate
                || point.Y is < -MaxClipboardCoordinate or > MaxClipboardCoordinate) == true)
            return false;
        if (value.Data is { Length: > MaxClipboardPngBytes })
            return false;
        if (value.Width < 0 || value.Height < 0
            || value.Width > MaxClipboardCoordinate || value.Height > MaxClipboardCoordinate
            || (long)value.Width * value.Height > MaxClipboardPixels
            || value.X1 is < -MaxClipboardCoordinate or > MaxClipboardCoordinate
            || value.Y1 is < -MaxClipboardCoordinate or > MaxClipboardCoordinate
            || value.X2 is < -MaxClipboardCoordinate or > MaxClipboardCoordinate
            || value.Y2 is < -MaxClipboardCoordinate or > MaxClipboardCoordinate)
            return false;

        bool validSize = float.IsFinite(value.Size) && value.Size is > 0f and <= 1024f;
        bool validText = value.Text != null && value.Text.Length <= 100_000;
        if (value.FontFamily != null && value.FontFamily.Length > 256)
            return false;
        if (value.Kind == "imageFragment" && (value.Data is not { Length: > 0 } || !HasSafePngDimensions(value.Data)))
            return false;

        return value.Kind switch
        {
            "draw" or "curvedArrow" => value.Points is { Count: >= 2 },
            "blur" or "rect" or "highlight" or "circle" or "eraser" => value.Width > 0 && value.Height > 0,
            "line" or "arrow" or "ruler" => true,
            "step" => value.Number is >= 1 and <= 9999,
            "text" => validText && validSize,
            "magnifier" => value.Width > 0 && value.Height > 0,
            "emoji" => value.Text != null && value.Text.Length <= 1000 && validSize,
            "imageFragment" => value.Width > 0 && value.Height > 0,
            _ => false
        };
    }

    private static bool HasSafePngDimensions(byte[] data)
    {
        if (data.Length < 24
            || !data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            || !data.AsSpan(12, 4).SequenceEqual("IHDR"u8))
            return false;

        int width = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(16, 4));
        int height = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(20, 4));
        return width > 0 && height > 0 && (long)width * height <= MaxClipboardPixels;
    }

    private static (string ProjectPath, string BasePath) GetProjectPaths(string imagePath)
    {
        var normalized = Path.GetFullPath(imagePath).ToUpperInvariant();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        return (Path.Combine(ProjectDirectory, key + ".json"), Path.Combine(ProjectDirectory, key + ".base.png"));
    }

    private static StoredAnnotation ToStored(Annotation annotation) => annotation switch
    {
        DrawStroke a => StoredAnnotation.PointsEntry("draw", a.Points, a.Color),
        BlurRect a => StoredAnnotation.RectEntry("blur", a.Rect),
        ArrowAnnotation a => StoredAnnotation.LineEntry("arrow", a.From, a.To, a.Color),
        CurvedArrowAnnotation a => StoredAnnotation.PointsEntry("curvedArrow", a.Points, a.Color),
        HighlightAnnotation a => StoredAnnotation.RectEntry("highlight", a.Rect, a.Color),
        StepNumberAnnotation a => new() { Kind = "step", X1 = a.Pos.X, Y1 = a.Pos.Y, Number = a.Number, ColorArgb = a.Color.ToArgb() },
        EraserFill a => StoredAnnotation.RectEntry("eraser", a.Rect, a.Color),
        TextAnnotation a => new()
        {
            Kind = "text",
            X1 = a.Pos.X,
            Y1 = a.Pos.Y,
            Text = a.Text,
            Size = a.FontSize,
            ColorArgb = a.Color.ToArgb(),
            Bold = a.Bold,
            Italic = a.Italic,
            Stroke = a.Stroke,
            Shadow = a.Shadow,
            Background = a.Background,
            FontFamily = a.FontFamily,
            Width = a.MaxWidth
        },
        MagnifierAnnotation a => new()
        {
            Kind = "magnifier",
            X1 = a.Pos.X,
            Y1 = a.Pos.Y,
            X2 = a.SrcRect.X,
            Y2 = a.SrcRect.Y,
            Width = a.SrcRect.Width,
            Height = a.SrcRect.Height
        },
        EmojiAnnotation a => new() { Kind = "emoji", X1 = a.Pos.X, Y1 = a.Pos.Y, Text = a.Emoji, Size = a.Size },
        LineAnnotation a => StoredAnnotation.LineEntry("line", a.From, a.To, a.Color),
        RulerAnnotation a => StoredAnnotation.LineEntry("ruler", a.From, a.To, Color.Empty),
        RectShapeAnnotation a => StoredAnnotation.ShapeEntry("rect", a.Rect, a.Color, a.FillColor, a.BorderColor),
        CircleShapeAnnotation a => StoredAnnotation.ShapeEntry("circle", a.Rect, a.Color, a.FillColor, a.BorderColor),
        ImageFragmentAnnotation a => new() { Kind = "imageFragment", X1 = a.Rect.X, Y1 = a.Rect.Y, Width = a.Rect.Width, Height = a.Rect.Height, Data = a.PngData },
        _ => throw new NotSupportedException($"Unsupported annotation type {annotation.GetType().Name}")
    };

    private static Annotation FromStored(StoredAnnotation a) => a.Kind switch
    {
        "draw" => new DrawStroke(ToPoints(a.Points), Color.FromArgb(a.ColorArgb)),
        "blur" => new BlurRect(ToRect(a)),
        "arrow" => new ArrowAnnotation(ToPoint1(a), ToPoint2(a), Color.FromArgb(a.ColorArgb)),
        "curvedArrow" => new CurvedArrowAnnotation(ToPoints(a.Points), Color.FromArgb(a.ColorArgb)),
        "highlight" => new HighlightAnnotation(ToRect(a), Color.FromArgb(a.ColorArgb)),
        "step" => new StepNumberAnnotation(ToPoint1(a), a.Number, Color.FromArgb(a.ColorArgb)),
        "eraser" => new EraserFill(ToRect(a), Color.FromArgb(a.ColorArgb)),
        "text" => new TextAnnotation(ToPoint1(a), a.Text ?? "", a.Size, Color.FromArgb(a.ColorArgb), a.Bold, a.Italic, a.Stroke, a.Shadow, a.Background, a.FontFamily ?? "Segoe UI", a.Width),
        "magnifier" => new MagnifierAnnotation(ToPoint1(a), new Rectangle(a.X2, a.Y2, a.Width, a.Height)),
        "emoji" => new EmojiAnnotation(ToPoint1(a), a.Text ?? "", a.Size),
        "line" => new LineAnnotation(ToPoint1(a), ToPoint2(a), Color.FromArgb(a.ColorArgb)),
        "ruler" => new RulerAnnotation(ToPoint1(a), ToPoint2(a)),
        "rect" => new RectShapeAnnotation(ToRect(a), Color.FromArgb(a.ColorArgb), ToOptionalColor(a.FillColorArgb), ToOptionalColor(a.BorderColorArgb)),
        "circle" => new CircleShapeAnnotation(ToRect(a), Color.FromArgb(a.ColorArgb), ToOptionalColor(a.FillColorArgb), ToOptionalColor(a.BorderColorArgb)),
        "imageFragment" => new ImageFragmentAnnotation(ToRect(a), a.Data is { Length: > 0 } ? a.Data : throw new InvalidDataException("Image fragment data is missing.")),
        _ => throw new InvalidDataException($"Unknown annotation kind '{a.Kind}'")
    };

    internal static Annotation Translate(Annotation a, int dx, int dy) => a switch
    {
        DrawStroke x => x with { Points = x.Points.Select(p => new Point(p.X + dx, p.Y + dy)).ToList() },
        BlurRect x => x with { Rect = Offset(x.Rect, dx, dy) },
        ArrowAnnotation x => x with { From = Offset(x.From, dx, dy), To = Offset(x.To, dx, dy) },
        CurvedArrowAnnotation x => x with { Points = x.Points.Select(p => Offset(p, dx, dy)).ToList() },
        HighlightAnnotation x => x with { Rect = Offset(x.Rect, dx, dy) },
        StepNumberAnnotation x => x with { Pos = Offset(x.Pos, dx, dy) },
        EraserFill x => x with { Rect = Offset(x.Rect, dx, dy) },
        TextAnnotation x => x with { Pos = Offset(x.Pos, dx, dy) },
        MagnifierAnnotation x => x with { Pos = Offset(x.Pos, dx, dy), SrcRect = Offset(x.SrcRect, dx, dy) },
        EmojiAnnotation x => x with { Pos = Offset(x.Pos, dx, dy) },
        LineAnnotation x => x with { From = Offset(x.From, dx, dy), To = Offset(x.To, dx, dy) },
        RulerAnnotation x => x with { From = Offset(x.From, dx, dy), To = Offset(x.To, dx, dy) },
        RectShapeAnnotation x => x with { Rect = Offset(x.Rect, dx, dy) },
        CircleShapeAnnotation x => x with { Rect = Offset(x.Rect, dx, dy) },
        ImageFragmentAnnotation x => x with { Rect = Offset(x.Rect, dx, dy) },
        _ => a
    };

    internal static Annotation Scale(Annotation a, float sx, float sy) => a switch
    {
        DrawStroke x => x with { Points = x.Points.Select(p => Scale(p, sx, sy)).ToList() },
        BlurRect x => x with { Rect = Scale(x.Rect, sx, sy) },
        ArrowAnnotation x => x with { From = Scale(x.From, sx, sy), To = Scale(x.To, sx, sy) },
        CurvedArrowAnnotation x => x with { Points = x.Points.Select(p => Scale(p, sx, sy)).ToList() },
        HighlightAnnotation x => x with { Rect = Scale(x.Rect, sx, sy) },
        StepNumberAnnotation x => x with { Pos = Scale(x.Pos, sx, sy) },
        EraserFill x => x with { Rect = Scale(x.Rect, sx, sy) },
        TextAnnotation x => x with
        {
            Pos = Scale(x.Pos, sx, sy),
            FontSize = x.FontSize * Math.Min(sx, sy),
            MaxWidth = x.MaxWidth <= 0 ? 0 : Math.Max(1, (int)Math.Round(x.MaxWidth * sx))
        },
        MagnifierAnnotation x => x with { Pos = Scale(x.Pos, sx, sy), SrcRect = Scale(x.SrcRect, sx, sy) },
        EmojiAnnotation x => x with { Pos = Scale(x.Pos, sx, sy), Size = x.Size * Math.Min(sx, sy) },
        LineAnnotation x => x with { From = Scale(x.From, sx, sy), To = Scale(x.To, sx, sy) },
        RulerAnnotation x => x with { From = Scale(x.From, sx, sy), To = Scale(x.To, sx, sy) },
        RectShapeAnnotation x => x with { Rect = Scale(x.Rect, sx, sy) },
        CircleShapeAnnotation x => x with { Rect = Scale(x.Rect, sx, sy) },
        ImageFragmentAnnotation x => x with { Rect = Scale(x.Rect, sx, sy) },
        _ => a
    };

    internal static ImageFragmentAnnotation CreateImageFragment(Bitmap source, Rectangle requestedRegion)
    {
        ArgumentNullException.ThrowIfNull(source);
        var sourceBounds = new Rectangle(0, 0, source.Width, source.Height);
        var regionBounds = Rectangle.Intersect(sourceBounds, requestedRegion);
        using var region = ExtractRegion(source, regionBounds);
        using var stream = new MemoryStream();
        CaptureOutputService.WritePng(region, stream);
        return new ImageFragmentAnnotation(regionBounds, stream.ToArray());
    }

    internal static ImageFragmentAnnotation CreateImageFragment(Bitmap source, Point destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var stream = new MemoryStream();
        CaptureOutputService.WritePng(source, stream);
        return new ImageFragmentAnnotation(
            new Rectangle(destination, source.Size),
            stream.ToArray());
    }

    internal static Bitmap DecodeImageFragment(ImageFragmentAnnotation fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        if (fragment.PngData.Length == 0)
            throw new InvalidDataException("Image fragment data is empty.");
        using var stream = new MemoryStream(fragment.PngData, writable: false);
        using var encoded = new Bitmap(stream);
        return new Bitmap(encoded);
    }

    internal static Bitmap ExtractRegion(Bitmap source, Rectangle requestedRegion)
    {
        ArgumentNullException.ThrowIfNull(source);
        var sourceBounds = new Rectangle(0, 0, source.Width, source.Height);
        var region = Rectangle.Intersect(sourceBounds, requestedRegion);
        if (region.Width <= 0 || region.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestedRegion), "The selected region is outside the image.");

        var result = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(result);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.DrawImage(
            source,
            new Rectangle(0, 0, region.Width, region.Height),
            region,
            GraphicsUnit.Pixel);
        return result;
    }

    private static Bitmap Resize(Bitmap source, int width, int height)
    {
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(source, new Rectangle(0, 0, width, height));
        return result;
    }

    private static Point Offset(Point p, int dx, int dy) => new(p.X + dx, p.Y + dy);
    private static Rectangle Offset(Rectangle r, int dx, int dy) => new(r.X + dx, r.Y + dy, r.Width, r.Height);
    private static Point Scale(Point p, float sx, float sy) => new((int)Math.Round(p.X * sx), (int)Math.Round(p.Y * sy));
    private static Rectangle Scale(Rectangle r, float sx, float sy) => new((int)Math.Round(r.X * sx), (int)Math.Round(r.Y * sy), Math.Max(1, (int)Math.Round(r.Width * sx)), Math.Max(1, (int)Math.Round(r.Height * sy)));
    private static Point ToPoint1(StoredAnnotation a) => new(a.X1, a.Y1);
    private static Point ToPoint2(StoredAnnotation a) => new(a.X2, a.Y2);
    private static Rectangle ToRect(StoredAnnotation a) => new(a.X1, a.Y1, a.Width, a.Height);
    private static List<Point> ToPoints(List<StoredPoint>? points) => points?.Select(p => new Point(p.X, p.Y)).ToList() ?? [];
    private static Color? ToOptionalColor(int? argb) => argb.HasValue ? Color.FromArgb(argb.Value) : null;

    private static void WriteTextAtomic(string path, string text)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, true);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { AppDiagnostics.LogWarning("editable-project.delete", ex.Message, ex); }
    }

    private sealed class StoredProject
    {
        public int Version { get; set; }
        public string SourceFileName { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public List<StoredAnnotation> Annotations { get; set; } = [];
    }

    private sealed class StoredAnnotation
    {
        public string Kind { get; set; } = "";
        public int X1 { get; set; }
        public int Y1 { get; set; }
        public int X2 { get; set; }
        public int Y2 { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int ColorArgb { get; set; }
        public int? FillColorArgb { get; set; }
        public int? BorderColorArgb { get; set; }
        public int Number { get; set; }
        public float Size { get; set; }
        public string? Text { get; set; }
        public string? FontFamily { get; set; }
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public bool Stroke { get; set; }
        public bool Shadow { get; set; }
        public bool Background { get; set; }
        public byte[]? Data { get; set; }
        public List<StoredPoint>? Points { get; set; }

        public static StoredAnnotation RectEntry(string kind, Rectangle rect, Color color = default) => new()
        { Kind = kind, X1 = rect.X, Y1 = rect.Y, Width = rect.Width, Height = rect.Height, ColorArgb = color.ToArgb() };

        public static StoredAnnotation ShapeEntry(string kind, Rectangle rect, Color color, Color? fillColor, Color? borderColor) => new()
        {
            Kind = kind,
            X1 = rect.X,
            Y1 = rect.Y,
            Width = rect.Width,
            Height = rect.Height,
            ColorArgb = color.ToArgb(),
            FillColorArgb = fillColor?.ToArgb(),
            BorderColorArgb = borderColor?.ToArgb()
        };

        public static StoredAnnotation LineEntry(string kind, Point from, Point to, Color color) => new()
        { Kind = kind, X1 = from.X, Y1 = from.Y, X2 = to.X, Y2 = to.Y, ColorArgb = color.ToArgb() };

        public static StoredAnnotation PointsEntry(string kind, IEnumerable<Point> points, Color color) => new()
        { Kind = kind, Points = points.Select(p => new StoredPoint { X = p.X, Y = p.Y }).ToList(), ColorArgb = color.ToArgb() };
    }

    private sealed class StoredPoint
    {
        public int X { get; set; }
        public int Y { get; set; }
    }
}

public sealed record EditableScreenshotProject(Bitmap BaseImage, IReadOnlyList<Annotation> Annotations, bool AlreadyEditable) : IDisposable
{
    public void Dispose() => BaseImage.Dispose();
}

public sealed record EditableCaptureData(Bitmap BaseImage, IReadOnlyList<Annotation> Annotations) : IDisposable
{
    public void Dispose() => BaseImage.Dispose();
}
