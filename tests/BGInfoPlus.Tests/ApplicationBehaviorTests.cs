using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Media;
using BGInfoPlus;
using Xunit;

namespace BGInfoPlus.Tests;

public sealed class ApplicationBehaviorTests
{
    [Fact]
    public void ResolutionLimitsAndScalingAreEnforced()
    {
        Assert.True(AppLimits.IsValidResolution(1920, 1080));
        Assert.True(AppLimits.IsValidResolution(3840, 2160));
        Assert.False(AppLimits.IsValidResolution(4096, 2160));
        Assert.False(AppLimits.IsValidResolution(3840, 2161));
        var fitted = AppLimits.FitResolution(7680, 4320);
        Assert.Equal((3840, 2160), (fitted.Width, fitted.Height));
    }

    [Fact]
    public void FontSizeParserAcceptsOnlySupportedFiniteValues()
    {
        Assert.True(Editor.TryParseFontSize("8", out var minimum));
        Assert.Equal(8, minimum);
        Assert.True(Editor.TryParseFontSize("96", out var maximum));
        Assert.Equal(96, maximum);
        var decimalText = 24.5.ToString(CultureInfo.CurrentCulture);
        Assert.True(Editor.TryParseFontSize(decimalText, out var decimalSize));
        Assert.Equal(24.5, decimalSize);
        foreach (var invalid in new[] { "", "7.99", "96.01", "NaN", "Infinity", "invalid" })
            Assert.False(Editor.TryParseFontSize(invalid, out _));
    }

    [Theory]
    [InlineData(1920, 1080, "16:9")]
    [InlineData(1366, 768, "16:9")]
    [InlineData(1680, 1050, "16:10")]
    [InlineData(1280, 1024, "5:4")]
    [InlineData(0, 1080, "")]
    public void AspectRatioUsesConventionalNotation(int width, int height, string expected) =>
        Assert.Equal(expected, Editor.AspectRatioText(width, height));

    [Fact]
    public void ResolutionsAreSortedAndCurrentReplacesDuplicateBuiltInEntry()
    {
        var current = new DisplayResolution(1366, 768, "Current");
        var sorted = Editor.SortResolutions(current,
            [new DisplayResolution(800, 600), new DisplayResolution(1920, 1080), new DisplayResolution(1366, 768)]);
        Assert.Equal([(1920, 1080), (1366, 768), (800, 600)], sorted.Select(item => (item.Width, item.Height)));
        Assert.Same(current, sorted[1]);
        Assert.Equal(3, sorted.Count);
    }

    [Fact]
    public void WallpaperModesMapToWindowsSettings()
    {
        Assert.Equal(("10", "0"), WallpaperService.FitRegistryValues(WallpaperFit.Fill));
        Assert.Equal(("6", "0"), WallpaperService.FitRegistryValues(WallpaperFit.Fit));
        Assert.Equal(("2", "0"), WallpaperService.FitRegistryValues(WallpaperFit.Stretch));
        Assert.Equal(PersonalizationBackgroundKind.Picture, WallpaperService.DetermineBackgroundKind(0, 0, true));
        Assert.Equal(PersonalizationBackgroundKind.SolidColor, WallpaperService.DetermineBackgroundKind(1, 0, false));
        Assert.Equal(PersonalizationBackgroundKind.Slideshow,
            WallpaperService.DetermineBackgroundKind(2, DesktopSlideshowState.DisabledByRemoteSession, true));
        Assert.Equal(PersonalizationBackgroundKind.WindowsSpotlight, WallpaperService.DetermineBackgroundKind(3, 0, true));
        Assert.Equal(PersonalizationBackgroundKind.Slideshow,
            WallpaperService.DetermineBackgroundKind(null, DesktopSlideshowState.Slideshow, true));
    }

    [Fact]
    public void CustomColorsRemainAvailableAcrossDialogs()
    {
        var palette = new NativeColorPalette();
        var buffer = Marshal.AllocHGlobal(NativeColorPalette.ColorCount * sizeof(int));
        try
        {
            palette.CopyTo(buffer);
            Assert.All(palette.Snapshot(), value => Assert.Equal(0x00FFFFFF, value));
            var color = Color.FromRgb(0x12, 0x34, 0x56);
            var colorRef = unchecked((int)NativeColorDialog.ToColorRef(color));
            Marshal.WriteInt32(buffer, 3 * sizeof(int), colorRef);
            palette.CopyFrom(buffer);
            Marshal.WriteInt32(buffer, 3 * sizeof(int), 0);
            palette.CopyTo(buffer);
            Assert.Equal(colorRef, Marshal.ReadInt32(buffer, 3 * sizeof(int)));
            Assert.Equal(color, NativeColorDialog.FromColorRef(unchecked((uint)colorRef)));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [Fact]
    public void ResolvedValuesAndConfigurationFieldsAreBounded()
    {
        var capped = DataSources.LimitValue(new string('x', AppLimits.MaxResolvedValueCharacters + 100));
        Assert.Equal(AppLimits.MaxResolvedValueCharacters, capped.Length);
        Assert.EndsWith("…", capped);
        Assert.Throws<InvalidDataException>(() => AppLimits.Validate(new AppConfiguration("",
            [new PlaceholderDefinition("1bad", "label", "Environment", "x")], false, 1920, 1080, "#000000")));
        Assert.Throws<InvalidDataException>(() => AppLimits.Validate(
            ConfigurationFileStore.CreateDefault() with { WallpaperFit = (WallpaperFit)999 }));
        Assert.Throws<InvalidDataException>(() => AppLimits.Validate(
            ConfigurationFileStore.CreateDefault() with { BackgroundColor = "blue" }));
    }
}
