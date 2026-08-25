using System.Drawing;
using OddSnap.Services;
using OddSnap.UI;
using Xunit;

namespace OddSnap.Tests;

public sealed class EditableScreenshotRegionTests
{
    [Fact]
    public void ExtractRegion_CopiesPixelsWithoutChangingSource()
    {
        using var source = new Bitmap(4, 3);
        source.SetPixel(1, 1, Color.Red);
        source.SetPixel(2, 1, Color.Blue);

        using var result = EditableScreenshotService.ExtractRegion(source, new Rectangle(1, 1, 2, 1));

        Assert.Equal(new Size(2, 1), result.Size);
        Assert.Equal(Color.Red.ToArgb(), result.GetPixel(0, 0).ToArgb());
        Assert.Equal(Color.Blue.ToArgb(), result.GetPixel(1, 0).ToArgb());
        Assert.Equal(Color.Red.ToArgb(), source.GetPixel(1, 1).ToArgb());
        Assert.Equal(Color.Blue.ToArgb(), source.GetPixel(2, 1).ToArgb());
    }

    [Fact]
    public void ExtractRegion_ClipsSelectionToImageBounds()
    {
        using var source = new Bitmap(5, 4);

        using var result = EditableScreenshotService.ExtractRegion(source, new Rectangle(-2, -1, 5, 4));

        Assert.Equal(new Size(3, 3), result.Size);
    }
}

public sealed class HistoryLibraryTextGeometryTests
{
    [Theory]
    [InlineData(100, 305, 500, 200)]
    [InlineData(100, 330, 500, 225)]
    [InlineData(100, 110, 500, 40)]
    [InlineData(100, 900, 300, 300)]
    public void CalculateInlineTextMaxWidthFromHandle_TracksVisibleFrameEdge(
        int textX,
        int handleX,
        int maximumWidth,
        int expectedWidth)
    {
        int width = HistoryLibraryWindow.CalculateInlineTextMaxWidthFromHandle(textX, handleX, maximumWidth);

        Assert.Equal(expectedWidth, width);
    }
}
