using System.Text;
using System.Text.Json;
using System.IO;
using BGInfoPlus.Core;
using BGInfoPlus;
using System.Windows.Documents;

var count = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); count++; }
var json = "{\"team\":\"Ops\",\"nested\":{\"enabled\":true},\"list\":[42,null],\"a/b\":\"escaped\",\"a~b\":\"tilde\"}";
var raw = UserDataJson.Parse(json);
var encoded = UserDataJson.Parse(Convert.ToBase64String(Encoding.UTF8.GetBytes(json)));
Check(raw.SequenceEqual(encoded), "Raw and Base64 JSON must match");
Check(raw.Single(f => f.Key == "userdata:/nested/enabled").Value == "true", "Nested boolean");
Check(raw.Single(f => f.Key == "userdata:/list/0").Value == "42", "Array number");
Check(raw.Single(f => f.Key == "userdata:/list/1").Value == "null", "Array null");
Check(raw.Single(f => f.Key == "userdata:/a~1b").Value == "escaped", "JSON pointer slash escaping");
Check(raw.Single(f => f.Key == "userdata:/a~0b").Value == "tilde", "JSON pointer tilde escaping");
Check(UserDataJson.Parse("\uFEFF" + json).SequenceEqual(raw), "UTF8 BOM");
Check(UserDataJson.Parse("{}").Count == 0, "Empty object");
foreach (var invalid in new[] { "not base64!", Convert.ToBase64String(Encoding.UTF8.GetBytes("not json")), "{broken}", "{\"x\":1,\"x\":2}" })
{
    var rejected = false;
    try { UserDataJson.Parse(invalid); } catch (Exception e) when (e is FormatException or JsonException) { rejected = true; }
    Check(rejected, "Reject malformed/ambiguous data: " + invalid);
}

void MustReject(string content, string name)
{
    var rejected = false;
    try { UserDataJson.Parse(content); } catch (Exception e) when (e is FormatException or JsonException) { rejected = true; }
    Check(rejected, name);
}
MustReject("{\"value\":\"" + new string('x', InputLimits.MaxValueCharacters + 1) + "\"}", "Oversized scalar rejected");
MustReject("{" + string.Join(',', Enumerable.Range(0, InputLimits.MaxUserDataFields + 1).Select(i => $"\"p{i}\":{i}")) + "}", "Too many JSON values rejected");
MustReject("{\"padding\":\"" + new string('x', InputLimits.MaxUserDataBytes) + "\"}", "Oversized UserData rejected");
Console.WriteLine($"Passed {count} UserData checks.");

void RunSta(Action action)
{
    Exception? failure = null;
    var thread = new Thread(() => { try { action(); } catch (Exception e) { failure = e; } });
    thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
    if (failure is not null) throw new Exception("STA test failed", failure);
}

RunSta(() =>
{
    static FlowDocument Document(params string[] runs)
    {
        var document = new FlowDocument(); var paragraph = new Paragraph();
        foreach (var text in runs) paragraph.Inlines.Add(new Run(text));
        document.Blocks.Add(paragraph); return document;
    }
    static string Text(FlowDocument document) => new TextRange(document.ContentStart, document.ContentEnd).Text.Trim();

    var missing = DocumentTools.Resolve(DocumentTools.Save(Document("{Missing}")), new Dictionary<string, string>());
    Check(Text(missing) == "", "Missing placeholders resolve to empty text");

    var split = DocumentTools.Resolve(DocumentTools.Save(Document("{Host", "name}")),
        new Dictionary<string, string> { ["Hostname"] = "SERVER01" });
    Check(Text(split) == "SERVER01", "Placeholder split across formatted runs");

    var cascade = DocumentTools.Resolve(DocumentTools.Save(Document("{First}")),
        new Dictionary<string, string> { ["First"] = "{Second}", ["Second"] = "SECRET" });
    Check(Text(cascade) == "{Second}", "Placeholder values are substituted exactly once");

    var separatedDocument = new FlowDocument(); var separatedParagraph = new Paragraph();
    separatedParagraph.Inlines.Add(new Run("{Host")); separatedParagraph.Inlines.Add(new LineBreak());
    separatedParagraph.Inlines.Add(new Run("name}")); separatedDocument.Blocks.Add(separatedParagraph);
    var separated = DocumentTools.Resolve(DocumentTools.Save(separatedDocument),
        new Dictionary<string, string> { ["Hostname"] = "SERVER01" });
    Check(!Text(separated).Contains("SERVER01"), "Placeholder matching does not cross line breaks");

    var oversizedDocument = Document(new string('x', AppLimits.MaxDocumentCharacters + 1));
    var documentRejected = false;
    try { DocumentTools.Save(oversizedDocument); } catch (InvalidDataException) { documentRejected = true; }
    Check(documentRejected, "Oversized rich-text document rejected");

    var configuration = new AppConfiguration(DocumentTools.Save(Document("{Hostname}")), [], false, 640, 480, "#112233");
    var bitmap = WallpaperService.RenderResolved(configuration, new Dictionary<string, string> { ["Hostname"] = "TEST" });
    Check(bitmap.PixelWidth == 640 && bitmap.PixelHeight == 480, "Renderer honors validated dimensions");
    var unsafeRenderRejected = false;
    try { WallpaperService.RenderResolved(configuration with { ImageWidth = 5000 }, new Dictionary<string, string>()); }
    catch (InvalidDataException) { unsafeRenderRejected = true; }
    Check(unsafeRenderRejected, "Renderer rejects unsafe dimensions before allocation");
});

Check(AppLimits.IsValidResolution(1920, 1080), "Common resolution accepted");
Check(AppLimits.IsValidResolution(3840, 2160), "Maximum resolution accepted");
Check(!AppLimits.IsValidResolution(4096, 2160), "Oversized resolution rejected");
Check(!AppLimits.IsValidResolution(3840, 2161), "Excessive pixel buffer rejected");
var fitted = AppLimits.FitResolution(7680, 4320);
Check(fitted.Width == 3840 && fitted.Height == 2160, "Oversized current display is safely scaled");
var capped = DataSources.LimitValue(new string('x', AppLimits.MaxResolvedValueCharacters + 100));
Check(capped.Length == AppLimits.MaxResolvedValueCharacters && capped.EndsWith('…'), "Resolved values are bounded");
var invalidConfigurationRejected = false;
try
{
    AppLimits.Validate(new AppConfiguration("", [new PlaceholderDefinition("1bad", "label", "Environment", "x")], false, 1920, 1080, "#000000"));
}
catch (InvalidDataException) { invalidConfigurationRejected = true; }
Check(invalidConfigurationRejected, "Invalid placeholder names rejected");

var atomicDirectory = Path.Combine(Path.GetTempPath(), "BGInfoPlus.Tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(atomicDirectory);
var atomicPath = Path.Combine(atomicDirectory, "image.bin");
File.WriteAllText(atomicPath, "old");
AtomicFile.Write(atomicPath, stream => stream.Write(Encoding.UTF8.GetBytes("new")));
Check(File.ReadAllText(atomicPath) == "new", "Atomic writer replaces the destination");
Check(Directory.GetFiles(atomicDirectory).Length == 1, "Atomic writer cleans temporary files");
var writeFailed = false;
try { AtomicFile.Write(atomicPath, stream => { stream.WriteByte(1); throw new IOException("injected"); }); }
catch (IOException) { writeFailed = true; }
Check(writeFailed && File.ReadAllText(atomicPath) == "new", "Failed atomic write preserves the prior file");
Check(Directory.GetFiles(atomicDirectory).Length == 1, "Failed atomic write cleans temporary files");
Directory.Delete(atomicDirectory, true);

Console.WriteLine($"Passed {count} checks.");
