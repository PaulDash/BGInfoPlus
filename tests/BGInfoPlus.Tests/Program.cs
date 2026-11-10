using System.Text;
using System.Text.Json;
using System.IO;
using System.Globalization;
using BGInfoPlus.Core;
using BGInfoPlus;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;

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

    var defaultConfiguration = ConfigurationFileStore.CreateDefault();
    Check(defaultConfiguration.ImageWidth == 1920 && defaultConfiguration.ImageHeight == 1080 && !defaultConfiguration.RunAtSignIn && defaultConfiguration.WallpaperFit == WallpaperFit.Fill,
        "Default shared configuration uses a standard resolution and does not enable per-user auto-apply");
    Check(defaultConfiguration.Placeholders.Select(p => p.Name).SequenceEqual(new[] { "Hostname", "IPv4" }),
        "Default shared configuration includes its system placeholders");

    var tabDocument = Document("Name\tValue");
    var tabRoundTrip = DocumentTools.Load(DocumentTools.Save(tabDocument));
    Check(Text(tabRoundTrip).Contains('\t'), "Tab character survives formatted document round-trip");
    var tabConfiguration = new AppConfiguration(DocumentTools.Save(tabRoundTrip), [], false, 640, 480, "#112233");
    var tabBitmap = WallpaperService.RenderResolved(tabConfiguration, new Dictionary<string, string>());
    Check(tabBitmap.PixelWidth == 640 && tabBitmap.PixelHeight == 480, "Renderer accepts documents containing tabs");

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

// Opt-in because desktop COM state is unavailable to some headless build agents.
if (Environment.GetEnvironmentVariable("BGINFOPLUS_TEST_DESKTOP") == "1")
{
    RunSta(() =>
    {
        using var snapshot = WallpaperService.CaptureCurrentConfiguration();
        Check(snapshot is not null && Enum.IsDefined(snapshot.Kind),
            "The interactive Windows wallpaper state can be captured");
    });
}

Check(AppLimits.IsValidResolution(1920, 1080), "Common resolution accepted");
Check(AppLimits.IsValidResolution(3840, 2160), "Maximum resolution accepted");
Check(!AppLimits.IsValidResolution(4096, 2160), "Oversized resolution rejected");
Check(!AppLimits.IsValidResolution(3840, 2161), "Excessive pixel buffer rejected");
Check(Editor.TryParseFontSize("8", out var minimumFontSize) && minimumFontSize == 8, "Minimum font size accepted");
Check(Editor.TryParseFontSize("96", out var maximumFontSize) && maximumFontSize == 96, "Maximum font size accepted");
var decimalFontSizeText = 24.5.ToString(CultureInfo.CurrentCulture);
Check(Editor.TryParseFontSize(decimalFontSizeText, out var decimalFontSize) && decimalFontSize == 24.5, "Decimal font size accepted");
foreach (var invalidSize in new[] { "", "7.99", "96.01", "NaN", "Infinity", "invalid" })
    Check(!Editor.TryParseFontSize(invalidSize, out _), "Invalid font size rejected: " + invalidSize);
Check(Editor.AspectRatioText(1920, 1080) == "16:9", "Common aspect ratio is reduced");
Check(Editor.AspectRatioText(1366, 768) == "16:9", "Near-standard display resolution uses its familiar aspect ratio");
Check(Editor.AspectRatioText(1680, 1050) == "16:10", "Display aspect ratio uses conventional notation");
Check(Editor.AspectRatioText(1280, 1024) == "5:4", "Non-widescreen aspect ratio is reduced");
Check(Editor.AspectRatioText(0, 1080) == string.Empty, "Invalid aspect ratio dimensions are not displayed");
var currentResolution = new DisplayResolution(1366, 768, "Current");
var sortedResolutions = Editor.SortResolutions(currentResolution,
    [new DisplayResolution(800, 600), new DisplayResolution(1920, 1080), new DisplayResolution(1366, 768)]);
Check(sortedResolutions.Select(item => (item.Width, item.Height)).SequenceEqual([(1920, 1080), (1366, 768), (800, 600)]),
    "Resolutions are sorted from highest to lowest pixel count");
Check(ReferenceEquals(sortedResolutions[1], currentResolution) && sortedResolutions.Count == 3,
    "Current resolution keeps its sorted position and replaces a matching built-in entry");
Check(WallpaperService.FitRegistryValues(WallpaperFit.Fill) == ("10", "0"), "Fill maps to Windows wallpaper settings");
Check(WallpaperService.FitRegistryValues(WallpaperFit.Fit) == ("6", "0"), "Fit maps to Windows wallpaper settings");
Check(WallpaperService.FitRegistryValues(WallpaperFit.Stretch) == ("2", "0"), "Stretch maps to Windows wallpaper settings");
Check(WallpaperService.DetermineBackgroundKind(0, 0, true) == PersonalizationBackgroundKind.Picture,
    "Picture personalization mode is recognized");
Check(WallpaperService.DetermineBackgroundKind(1, 0, false) == PersonalizationBackgroundKind.SolidColor,
    "Solid-color personalization mode is recognized");
Check(WallpaperService.DetermineBackgroundKind(2, DesktopSlideshowState.DisabledByRemoteSession, true) == PersonalizationBackgroundKind.Slideshow,
    "Slideshow personalization mode remains recognized in a remote session");
Check(WallpaperService.DetermineBackgroundKind(3, 0, true) == PersonalizationBackgroundKind.WindowsSpotlight,
    "Windows Spotlight personalization mode is recognized");
Check(WallpaperService.DetermineBackgroundKind(null, DesktopSlideshowState.Slideshow, true) == PersonalizationBackgroundKind.Slideshow,
    "Desktop API slideshow state is used when the Personalization selector is unavailable");
var customPalette = new NativeColorPalette();
var customColorBuffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(NativeColorPalette.ColorCount * sizeof(int));
try
{
    customPalette.CopyTo(customColorBuffer);
    Check(customPalette.Snapshot().All(value => value == 0x00FFFFFF), "Native custom color palette starts with 16 white slots");
    var customColor = Color.FromRgb(0x12, 0x34, 0x56);
    var customColorRef = unchecked((int)NativeColorDialog.ToColorRef(customColor));
    System.Runtime.InteropServices.Marshal.WriteInt32(customColorBuffer, 3 * sizeof(int), customColorRef);
    customPalette.CopyFrom(customColorBuffer); // Simulate the first dialog adding a custom color.
    System.Runtime.InteropServices.Marshal.WriteInt32(customColorBuffer, 3 * sizeof(int), 0);
    customPalette.CopyTo(customColorBuffer);   // Simulate opening the other color selector.
    Check(System.Runtime.InteropServices.Marshal.ReadInt32(customColorBuffer, 3 * sizeof(int)) == customColorRef,
        "A custom color remains available to the next font or wallpaper dialog");
    Check(NativeColorDialog.FromColorRef(unchecked((uint)customColorRef)) == customColor,
        "Native COLORREF encoding preserves RGB channel order");
}
finally
{
    System.Runtime.InteropServices.Marshal.FreeHGlobal(customColorBuffer);
}
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
var invalidFitRejected = false;
try { AppLimits.Validate(ConfigurationFileStore.CreateDefault() with { WallpaperFit = (WallpaperFit)999 }); }
catch (InvalidDataException) { invalidFitRejected = true; }
Check(invalidFitRejected, "Invalid wallpaper fit mode rejected");

var configurationDirectory = Path.Combine(Path.GetTempPath(), "BGInfoPlus.ConfigTests", Guid.NewGuid().ToString("N"));
var configurationPath = Path.Combine(configurationDirectory, "config.json");
var sharedConfiguration = new AppConfiguration("<FlowDocument />", [new PlaceholderDefinition("Hostname", "Hostname", "Networking", "Hostname")], true, 1920, 1080, "#102030", WallpaperFit.Stretch);
ConfigurationFileStore.Save(configurationPath, sharedConfiguration);
var loadedSharedConfiguration = ConfigurationFileStore.Load(configurationPath);
Check(loadedSharedConfiguration.Document == sharedConfiguration.Document, "Shared configuration document round-trips");
Check(loadedSharedConfiguration.Placeholders.SequenceEqual(sharedConfiguration.Placeholders), "Shared configuration placeholders round-trip");
Check(loadedSharedConfiguration.RunAtSignIn, "A personal configuration preserves its login preference");
Check(loadedSharedConfiguration.WallpaperFit == WallpaperFit.Stretch, "Wallpaper fit mode round-trips as configuration data");
var legacyConfigurationJson = JsonSerializer.Serialize(new
{
    sharedConfiguration.Document, sharedConfiguration.Placeholders, sharedConfiguration.RunAtSignIn,
    sharedConfiguration.ImageWidth, sharedConfiguration.ImageHeight, sharedConfiguration.BackgroundColor
});
File.WriteAllText(configurationPath, legacyConfigurationJson);
Check(ConfigurationFileStore.Load(configurationPath).WallpaperFit == WallpaperFit.Fill,
    "Configuration files from before wallpaper fit support default to Fill");
ConfigurationFileStore.SaveShared(configurationPath, sharedConfiguration);
Check(!ConfigurationFileStore.Load(configurationPath).RunAtSignIn, "A portable or machine configuration excludes per-user startup policy");

var userConfigurationPath = Path.Combine(configurationDirectory, "user.json");
var allUsersConfigurationPath = Path.Combine(configurationDirectory, "all-users.json");
var defaultConfigurationPath = Path.Combine(configurationDirectory, "DefaultConfig.json");
Check(Path.GetFileName(ConfigurationStore.DefaultPath) == "DefaultConfig.json" &&
      Path.GetDirectoryName(ConfigurationStore.DefaultPath) == Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory),
    "Default configuration path is beside the application executable");
ConfigurationFileStore.Save(userConfigurationPath, sharedConfiguration with { BackgroundColor = "#111111" });
ConfigurationFileStore.SaveShared(allUsersConfigurationPath, sharedConfiguration with { BackgroundColor = "#222222" });
ConfigurationFileStore.SaveShared(defaultConfigurationPath, sharedConfiguration with { BackgroundColor = "#333333" });
var editorResolution = ConfigurationResolver.ResolveForEditor(userConfigurationPath, allUsersConfigurationPath, defaultConfigurationPath);
Check(editorResolution?.Scope == ConfigurationScope.AllUsers && editorResolution.Configuration.BackgroundColor == "#222222",
    "Editor resolution shows the enforced configuration that automatic apply will use");
var automaticResolution = ConfigurationResolver.ResolveForAutomaticApply(userConfigurationPath, allUsersConfigurationPath, defaultConfigurationPath);
Check(automaticResolution?.Scope == ConfigurationScope.AllUsers && automaticResolution.Configuration.BackgroundColor == "#222222",
    "Automatic resolution enforces the all-users configuration first");
File.Delete(allUsersConfigurationPath);
automaticResolution = ConfigurationResolver.ResolveForAutomaticApply(userConfigurationPath, allUsersConfigurationPath, defaultConfigurationPath);
Check(automaticResolution?.Scope == ConfigurationScope.User, "Automatic resolution uses a personal configuration before the application default");
File.Delete(userConfigurationPath);
automaticResolution = ConfigurationResolver.ResolveForAutomaticApply(userConfigurationPath, allUsersConfigurationPath, defaultConfigurationPath);
Check(automaticResolution?.Scope == ConfigurationScope.Default, "Automatic resolution falls back to the application-local default for a new user");
File.WriteAllText(allUsersConfigurationPath, "{broken}");
var invalidPolicyRejected = false;
try { ConfigurationResolver.ResolveForAutomaticApply(userConfigurationPath, allUsersConfigurationPath, defaultConfigurationPath); }
catch (JsonException) { invalidPolicyRejected = true; }
Check(invalidPolicyRejected, "An invalid all-users policy is not silently bypassed by a lower-precedence file");
File.Delete(allUsersConfigurationPath);
File.WriteAllText(configurationPath, "{\"Document\":null,\"Placeholders\":[],\"ImageWidth\":1920,\"ImageHeight\":1080,\"BackgroundColor\":\"#000000\"}");
var malformedConfigurationRejected = false;
try { ConfigurationFileStore.Load(configurationPath); } catch (InvalidDataException) { malformedConfigurationRejected = true; }
Check(malformedConfigurationRejected, "Malformed shared configuration is rejected before rendering");
using (var oversized = new FileStream(configurationPath, FileMode.Create, FileAccess.Write))
    oversized.SetLength(AppLimits.MaxConfigurationFileBytes + 1L);
var oversizedConfigurationRejected = false;
try { ConfigurationFileStore.Load(configurationPath); } catch (InvalidDataException) { oversizedConfigurationRejected = true; }
Check(oversizedConfigurationRejected, "Oversized shared configuration is rejected before parsing");
Directory.Delete(configurationDirectory, true);

var migrationId = Guid.NewGuid().ToString("N");
var migrationKeyPath = $@"Software\BGInfoPlus.Tests.{migrationId}";
var migrationRunPath = $@"Software\BGInfoPlus.Tests.Run.{migrationId}";
var migrationFile = Path.Combine(Path.GetTempPath(), "BGInfoPlus.MigrationTests", migrationId, "config.json");
try
{
    using (var key = Registry.CurrentUser.CreateSubKey(migrationKeyPath))
    {
        key.SetValue("Document", sharedConfiguration.Document);
        key.SetValue("Placeholders", JsonSerializer.Serialize(sharedConfiguration.Placeholders));
        key.SetValue("RunAtSignIn", 0, RegistryValueKind.DWord);
        key.SetValue("ImageWidth", sharedConfiguration.ImageWidth, RegistryValueKind.DWord);
        key.SetValue("ImageHeight", sharedConfiguration.ImageHeight, RegistryValueKind.DWord);
        key.SetValue("BackgroundColor", sharedConfiguration.BackgroundColor);
    }
    using (var run = Registry.CurrentUser.CreateSubKey(migrationRunPath)) run.SetValue("BGInfoPlus", "legacy");
    LegacyRegistryConfiguration.TryMigrate(migrationFile, false, migrationKeyPath, migrationRunPath);
    Check(ConfigurationFileStore.Load(migrationFile).Document == sharedConfiguration.Document,
        "Legacy registry configuration migrates to the personal JSON format");
    using var removedKey = Registry.CurrentUser.OpenSubKey(migrationKeyPath);
    using var cleanedRun = Registry.CurrentUser.OpenSubKey(migrationRunPath);
    Check(removedKey is null && cleanedRun?.GetValue("BGInfoPlus") is null,
        "Legacy registry state is removed only after successful file migration");
}
finally
{
    Registry.CurrentUser.DeleteSubKeyTree(migrationKeyPath, false);
    Registry.CurrentUser.DeleteSubKeyTree(migrationRunPath, false);
    var migrationDirectory = Path.GetDirectoryName(migrationFile)!;
    if (Directory.Exists(migrationDirectory)) Directory.Delete(migrationDirectory, true);
}

var shortcutDirectory = Path.Combine(Path.GetTempPath(), "BGInfoPlus.ShortcutTests", Guid.NewGuid().ToString("N"));
var shortcutPath = Path.Combine(shortcutDirectory, UserStartupRegistration.ShortcutFileName);
UserStartupRegistration.CreateShortcut(shortcutPath, Environment.ProcessPath!);
Check(File.Exists(shortcutPath) && new FileInfo(shortcutPath).Length > 0, "Per-user login shortcut can be created without registry startup state");
Directory.Delete(shortcutDirectory, true);

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
