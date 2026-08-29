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
        Assert.Equal(0.25d, HistoryLibraryWindow.CalculateInlineZoom(0.25d, -120));
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
}
