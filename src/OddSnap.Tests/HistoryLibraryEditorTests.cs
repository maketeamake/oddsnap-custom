using System.Drawing;
using OddSnap.UI;
using Xunit;

namespace OddSnap.Tests;

public sealed class HistoryLibraryEditorTests
{
    [Theory]
    [InlineData("1", 1)]
    [InlineData("3", 3)]
    [InlineData("9999", 9999)]
    public void TryParseInlineStepNumber_AcceptsSupportedValues(string text, int expected)
    {
        bool parsed = HistoryLibraryWindow.TryParseInlineStepNumber(text, out int number);

        Assert.True(parsed);
        Assert.Equal(expected, number);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("10000")]
    [InlineData("3a")]
    public void TryParseInlineStepNumber_RejectsUnsupportedValues(string text)
    {
        Assert.False(HistoryLibraryWindow.TryParseInlineStepNumber(text, out _));
    }

    [Theory]
    [InlineData(10, 10, 80, 25, 80, 10)]
    [InlineData(10, 10, 25, 80, 10, 80)]
    [InlineData(40, 40, -10, 30, -10, 40)]
    [InlineData(40, 40, 30, -10, 40, -10)]
    public void ConstrainInlineAxisPoint_SnapsToHorizontalOrVertical(
        int startX,
        int startY,
        int currentX,
        int currentY,
        int expectedX,
        int expectedY)
    {
        var constrained = HistoryLibraryWindow.ConstrainInlineAxisPoint(
            new Point(startX, startY),
            new Point(currentX, currentY));

        Assert.Equal(new Point(expectedX, expectedY), constrained);
    }

    [Fact]
    public void CalculateInlineZoom_ChangesAndClampsZoom()
    {
        Assert.True(HistoryLibraryWindow.CalculateInlineZoom(1d, 120) > 1d);
        Assert.True(HistoryLibraryWindow.CalculateInlineZoom(1d, -120) < 1d);
        Assert.Equal(4d, HistoryLibraryWindow.CalculateInlineZoom(4d, 120));
        Assert.Equal(0.05d, HistoryLibraryWindow.CalculateInlineZoom(0.05d, -120));
    }

    [Theory]
    [InlineData(1000, 500, 1000, 1000, 1, 0.5)]
    [InlineData(1000, 500, 1000, 1000, 2, 1)]
    [InlineData(960, 540, 960, 540, 1, 1)]
    [InlineData(960, 540, 960, 540, 0.5, 0.5)]
    public void CalculateInlineDisplayScale_UsesFitScaleAndZoom(
        double viewportWidth,
        double viewportHeight,
        int imageWidth,
        int imageHeight,
        double zoomFactor,
        double expected)
    {
        double scale = HistoryLibraryWindow.CalculateInlineDisplayScale(
            viewportWidth,
            viewportHeight,
            imageWidth,
            imageHeight,
            zoomFactor);

        Assert.Equal(expected, scale, 6);
    }

    [Theory]
    [InlineData(1000, 500, 500, 250, 1, 0.5)]
    [InlineData(1000, 500, 2000, 1000, 1, 2)]
    [InlineData(1000, 500, 100, 50, 1, 0.1)]
    public void CalculateInlineZoomForDisplayScale_PreservesAbsoluteImageScale(
        double viewportWidth,
        double viewportHeight,
        int imageWidth,
        int imageHeight,
        double desiredDisplayScale,
        double expectedZoom)
    {
        double zoom = HistoryLibraryWindow.CalculateInlineZoomForDisplayScale(
            viewportWidth,
            viewportHeight,
            imageWidth,
            imageHeight,
            desiredDisplayScale);

        Assert.Equal(expectedZoom, zoom, 6);
        Assert.Equal(
            desiredDisplayScale,
            HistoryLibraryWindow.CalculateInlineDisplayScale(
                viewportWidth,
                viewportHeight,
                imageWidth,
                imageHeight,
                zoom),
            6);
    }

    [Theory]
    [InlineData(0.25, 36)]
    [InlineData(0.5, 18)]
    [InlineData(1, 9)]
    [InlineData(2, 5)]
    public void CalculateInlineHitTolerance_KeepsAVisibleScreenRadius(double scale, int expected)
    {
        Assert.Equal(expected, HistoryLibraryWindow.CalculateInlineHitTolerance(scale));
    }

    [Theory]
    [InlineData(100, 0.5, 7)]
    [InlineData(100, 1, 14)]
    [InlineData(400, 2, 42)]
    public void CalculateInlineArrowheadSize_ScalesWithTheImage(
        double imageLength,
        double displayScale,
        double expected)
    {
        Assert.Equal(
            expected,
            HistoryLibraryWindow.CalculateInlineArrowheadSize(imageLength, displayScale),
            6);
    }

    [Fact]
    public void IsInlineTextFrameHit_AllowsDraggingFromBlankAreaInsideTextBox()
    {
        var text = new OddSnap.Models.TextAnnotation(
            new Point(20, 30),
            "Short",
            18f,
            Color.Red,
            true,
            false,
            false,
            false,
            false,
            "Segoe UI",
            300);

        Assert.True(HistoryLibraryWindow.IsInlineTextFrameHit(text, new Point(270, 35)));
        Assert.False(HistoryLibraryWindow.IsInlineTextFrameHit(text, new Point(400, 35)));
    }

    [Fact]
    public void CalculateExpandedCanvasSize_NeverShrinksPastedImage()
    {
        Assert.Equal(
            new Size(150, 100),
            HistoryLibraryWindow.CalculateExpandedCanvasSize(100, 80, 150, 100));
        Assert.Equal(
            new Size(200, 120),
            HistoryLibraryWindow.CalculateExpandedCanvasSize(200, 120, 90, 60));
    }

    [Fact]
    public void FloodFillBitmap_RecolorsOnlyConnectedArea()
    {
        using var source = new Bitmap(3, 2);
        source.SetPixel(0, 0, Color.White);
        source.SetPixel(1, 0, Color.White);
        source.SetPixel(2, 0, Color.Black);
        source.SetPixel(0, 1, Color.White);
        source.SetPixel(1, 1, Color.Black);
        source.SetPixel(2, 1, Color.Black);

        using var filled = HistoryLibraryWindow.FloodFillBitmap(source, new Point(0, 0), Color.Red, 0);

        Assert.NotNull(filled);
        Assert.Equal(Color.Red.ToArgb(), filled.GetPixel(1, 0).ToArgb());
        Assert.Equal(Color.Black.ToArgb(), filled.GetPixel(2, 0).ToArgb());
    }

    [Fact]
    public void StepNumberBounds_GrowForMultipleDigits()
    {
        var oneDigit = OddSnap.Capture.RegionOverlayForm.MeasureStepNumberBounds(new Point(50, 50), 1);
        var fourDigits = OddSnap.Capture.RegionOverlayForm.MeasureStepNumberBounds(new Point(50, 50), 9999);

        Assert.Equal(oneDigit.Height, fourDigits.Height, 2);
        Assert.True(fourDigits.Width > oneDigit.Width);
    }
}
