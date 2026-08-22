using System.Text;
using System.Windows;
using System.Windows.Media;
using Bitmap = System.Drawing.Bitmap;
using Color = System.Windows.Media.Color;

namespace OddSnap.UI;

internal sealed record ToastSpec
{
    private const int MaxToastTitleChars = 140;
    private const int MaxToastBodyChars = 900;

    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public Color? SwatchColor { get; init; }
    public Bitmap? PreviewBitmap { get; init; }
    public Bitmap? InlinePreviewBitmap { get; init; }
    public string? FilePath { get; init; }
    public string? ClickActionUrl { get; init; }
    public string? ClickActionLabel { get; init; }
    public bool OpenHistoryEditorOnClick { get; init; }
    public bool PlayCaptureSound { get; init; }
    public bool PlayErrorSound { get; init; }
    public bool SuppressSound { get; init; }
    public bool IsError { get; init; }
    public bool AutoPin { get; init; }
    public bool TransparentShell { get; init; }
    public bool ShowOverlayButtons { get; init; }
    public Stretch PreviewStretch { get; init; } = Stretch.Uniform;
    public Thickness PreviewMargin { get; init; }
    public double? PreviewMaxHeight { get; init; }
    public int? MaxWidthOverride { get; init; }
    public int? MinWidthOverride { get; init; }

    public static ToastSpec Standard(string title, string body = "", string? filePath = null) => new()
    {
        Title = TrimToastText(title, MaxToastTitleChars, appendNotice: false),
        Body = TrimToastText(body, MaxToastBodyChars, appendNotice: true),
        FilePath = filePath
    };

    public static ToastSpec Error(string title, string body = "", string? filePath = null) => new()
    {
        Title = TrimToastText(title, MaxToastTitleChars, appendNotice: false),
        Body = TrimToastText(body, MaxToastBodyChars, appendNotice: true),
        FilePath = filePath,
        PlayErrorSound = true,
        IsError = true
    };

    public static ToastSpec WithColor(string title, string body, Color color) => new()
    {
        Title = TrimToastText(title, MaxToastTitleChars, appendNotice: false),
        Body = TrimToastText(body, MaxToastBodyChars, appendNotice: true),
        SwatchColor = color
    };

    public static ToastSpec InlinePreview(Bitmap preview, string title, string body, string? filePath = null) => new()
    {
        Title = TrimToastText(title, MaxToastTitleChars, appendNotice: false),
        Body = TrimToastText(body, MaxToastBodyChars, appendNotice: true),
        InlinePreviewBitmap = preview,
        FilePath = filePath
    };

    public static ToastSpec ImagePreview(
        Bitmap preview,
        string title,
        string body,
        string? filePath,
        bool autoPin,
        bool transparentShell,
        bool showOverlayButtons,
        string? clickActionUrl = null,
        string? clickActionLabel = null,
        bool openHistoryEditorOnClick = false) => new()
        {
            Title = TrimToastText(title, MaxToastTitleChars, appendNotice: false),
            Body = TrimToastText(body, MaxToastBodyChars, appendNotice: true),
            PreviewBitmap = preview,
            FilePath = filePath,
            ClickActionUrl = clickActionUrl,
            ClickActionLabel = clickActionLabel,
            OpenHistoryEditorOnClick = openHistoryEditorOnClick,
            AutoPin = autoPin,
            TransparentShell = transparentShell,
            ShowOverlayButtons = showOverlayButtons
        };

    public static ToastSpec Sticker(Bitmap sticker) => new()
    {
        PreviewBitmap = sticker,
        TransparentShell = false,
        PreviewStretch = Stretch.Uniform,
        PreviewMargin = new Thickness(0),
        ShowOverlayButtons = false
    };

    public static string CompactTextPreview(string? text, int maxChars = 80)
    {
        if (string.IsNullOrWhiteSpace(text) || maxChars <= 0)
            return "";

        var builder = new StringBuilder(Math.Min(maxChars, text.Length));
        bool pendingSpace = false;
        bool wroteText = false;

        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (wroteText)
                    pendingSpace = true;
                continue;
            }

            if (pendingSpace)
            {
                if (builder.Length >= maxChars)
                    return builder.ToString().TrimEnd() + "...";

                builder.Append(' ');
                pendingSpace = false;
            }

            if (builder.Length >= maxChars)
                return builder.ToString().TrimEnd() + "...";

            builder.Append(ch);
            wroteText = true;
        }

        return builder.ToString();
    }

    private static string TrimToastText(string? text, int maxChars, bool appendNotice)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (normalized.Length <= maxChars)
            return normalized;

        var trimmed = normalized[..maxChars].TrimEnd();
        return appendNotice
            ? trimmed + "\n... Details shortened."
            : trimmed + "...";
    }
}
