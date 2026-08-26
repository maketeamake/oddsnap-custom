using System.Drawing;
using OddSnap.Capture;
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

    [Fact]
    public void CreateImageFragment_CopiesPixelsAndCanBeMovedWithoutChangingSource()
    {
        using var source = new Bitmap(3, 1);
        source.SetPixel(0, 0, Color.Red);
        source.SetPixel(1, 0, Color.Green);
        source.SetPixel(2, 0, Color.Blue);

        var fragment = EditableScreenshotService.CreateImageFragment(source, new Rectangle(0, 0, 1, 1));
        var moved = Assert.IsType<OddSnap.Models.ImageFragmentAnnotation>(
            EditableScreenshotService.Translate(fragment, 2, 0));
        using var decoded = EditableScreenshotService.DecodeImageFragment(moved);
        using var rendered = RegionOverlayForm.RenderEditorProject(source, [moved], strokeShadow: false);

        Assert.Equal(new Rectangle(2, 0, 1, 1), moved.Rect);
        Assert.Equal(Color.Red.ToArgb(), decoded.GetPixel(0, 0).ToArgb());
        Assert.Equal(Color.Red.ToArgb(), source.GetPixel(0, 0).ToArgb());
        Assert.Equal(Color.Blue.ToArgb(), source.GetPixel(2, 0).ToArgb());
        Assert.Equal(Color.Red.ToArgb(), rendered.GetPixel(2, 0).ToArgb());
    }

    [Fact]
    public void ImageFragment_PersistsWithEditableProject()
    {
        string imagePath = Path.Combine(Path.GetTempPath(), $"oddsnap-fragment-{Guid.NewGuid():N}.png");
        try
        {
            using var source = new Bitmap(2, 1);
            source.SetPixel(0, 0, Color.Orange);
            source.SetPixel(1, 0, Color.Purple);
            CaptureOutputService.SavePng(source, imagePath);
            var fragment = EditableScreenshotService.CreateImageFragment(source, new Rectangle(1, 0, 1, 1));

            EditableScreenshotService.SaveProject(imagePath, source, [fragment]);
            using var loaded = EditableScreenshotService.Load(imagePath);
            var loadedFragment = Assert.IsType<OddSnap.Models.ImageFragmentAnnotation>(Assert.Single(loaded.Annotations));
            using var decoded = EditableScreenshotService.DecodeImageFragment(loadedFragment);

            Assert.True(loaded.AlreadyEditable);
            Assert.Equal(fragment.Rect, loadedFragment.Rect);
            Assert.Equal(Color.Purple.ToArgb(), decoded.GetPixel(0, 0).ToArgb());
        }
        finally
        {
            EditableScreenshotService.DeleteProject(imagePath);
            File.Delete(imagePath);
        }
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
