using System.Windows.Documents;
using BGInfoPlus;
using Xunit;

namespace BGInfoPlus.Tests;

public sealed class DocumentAndRenderingTests
{
    [Fact]
    public void DefaultConfigurationIsSafeAndUseful() => TestSupport.RunSta(() =>
    {
        var configuration = ConfigurationFileStore.CreateDefault();
        Assert.Equal((1920, 1080), (configuration.ImageWidth, configuration.ImageHeight));
        Assert.False(configuration.RunAtSignIn);
        Assert.Equal(WallpaperFit.Fill, configuration.WallpaperFit);
        Assert.Equal(["Hostname", "IPv4"], configuration.Placeholders.Select(placeholder => placeholder.Name));
    });

    [Fact]
    public void TabsSurviveRoundTripAndRendering() => TestSupport.RunSta(() =>
    {
        var roundTrip = DocumentTools.Load(DocumentTools.Save(TestSupport.Document("Name\tValue")));
        Assert.Contains('\t', TestSupport.Text(roundTrip));
        var configuration = new AppConfiguration(DocumentTools.Save(roundTrip), [], false, 640, 480, "#112233");
        var bitmap = WallpaperService.RenderResolved(configuration, new Dictionary<string, string>());
        Assert.Equal((640, 480), (bitmap.PixelWidth, bitmap.PixelHeight));
    });

    [Fact]
    public void PlaceholdersResolveOnceAndRespectDocumentBoundaries() => TestSupport.RunSta(() =>
    {
        var missing = DocumentTools.Resolve(DocumentTools.Save(TestSupport.Document("{Missing}")), new Dictionary<string, string>());
        Assert.Empty(TestSupport.Text(missing));
        var split = DocumentTools.Resolve(DocumentTools.Save(TestSupport.Document("{Host", "name}")),
            new Dictionary<string, string> { ["Hostname"] = "SERVER01" });
        Assert.Equal("SERVER01", TestSupport.Text(split));
        var cascade = DocumentTools.Resolve(DocumentTools.Save(TestSupport.Document("{First}")),
            new Dictionary<string, string> { ["First"] = "{Second}", ["Second"] = "SECRET" });
        Assert.Equal("{Second}", TestSupport.Text(cascade));

        var separatedDocument = new FlowDocument();
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run("{Host"));
        paragraph.Inlines.Add(new LineBreak());
        paragraph.Inlines.Add(new Run("name}"));
        separatedDocument.Blocks.Add(paragraph);
        var separated = DocumentTools.Resolve(DocumentTools.Save(separatedDocument),
            new Dictionary<string, string> { ["Hostname"] = "SERVER01" });
        Assert.DoesNotContain("SERVER01", TestSupport.Text(separated));
    });

    [Fact]
    public void OversizedRichTextDocumentIsRejected() => TestSupport.RunSta(() =>
    {
        var document = TestSupport.Document(new string('x', AppLimits.MaxDocumentCharacters + 1));
        Assert.Throws<InvalidDataException>(() => { DocumentTools.Save(document); });
    });

    [Fact]
    public void RendererHonorsValidatedDimensionsAndRejectsUnsafeDimensions() => TestSupport.RunSta(() =>
    {
        var configuration = new AppConfiguration(
            DocumentTools.Save(TestSupport.Document("{Hostname}")), [], false, 640, 480, "#112233");
        var bitmap = WallpaperService.RenderResolved(configuration,
            new Dictionary<string, string> { ["Hostname"] = "TEST" });
        Assert.Equal((640, 480), (bitmap.PixelWidth, bitmap.PixelHeight));
        Assert.Throws<InvalidDataException>(() =>
        {
            WallpaperService.RenderResolved(configuration with { ImageWidth = 5000 }, new Dictionary<string, string>());
        });
    });
}
