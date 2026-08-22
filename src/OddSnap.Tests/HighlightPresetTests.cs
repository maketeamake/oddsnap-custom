using System.Drawing;
using System.Drawing.Imaging;
using OddSnap.Capture;
using OddSnap.Services;
using Xunit;

namespace OddSnap.Tests;

public sealed class HighlightPresetTests
{
    [Fact]
    public async Task CustomBuild_DoesNotOfferAnUpstreamAutoUpdate()
    {
        var result = await UpdateService.CheckForUpdatesAsync(
            forceRefresh: true,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsUpdateAvailable);
        Assert.Contains("Custom build", result.StatusMessage);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(40)]
    [InlineData(60)]
    [InlineData(80)]
    public void DrawHighlightRect_PreservesPresetColorAndOpacity(int opacityPercent)
    {
        using var bitmap = new Bitmap(40, 40, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        int expectedAlpha = (int)Math.Round(opacityPercent / 100d * byte.MaxValue);
        var presetColor = Color.FromArgb(expectedAlpha, 255, 0, 0);

        SketchRenderer.DrawHighlightRect(graphics, new Rectangle(5, 5, 30, 30), presetColor);

        var pixel = bitmap.GetPixel(20, 20);
        Assert.Equal(expectedAlpha, pixel.A);
        Assert.InRange(pixel.R, 254, 255);
        Assert.Equal(0, pixel.G);
        Assert.Equal(0, pixel.B);
    }
}
