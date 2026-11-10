using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.InteropServices;
using System.IO;

namespace BGInfoPlus;

internal sealed record PlaceholderDefinition(string Name, string Label, string Kind, string Argument);
internal sealed record DisplayResolution(int Width, int Height, string? Description = null)
{
    public override string ToString() => $"{Width} × {Height}" + (Description is null ? "" : $"  {Description}");
}
internal enum WallpaperFit { Fill, Fit, Stretch }
internal sealed record AppConfiguration(string Document, PlaceholderDefinition[] Placeholders, bool RunAtSignIn, int ImageWidth, int ImageHeight, string BackgroundColor, WallpaperFit WallpaperFit = WallpaperFit.Fill);
internal enum ConfigurationScope { BuiltIn, Default, User, AllUsers, Explicit }
internal sealed record ResolvedConfiguration(AppConfiguration Configuration, ConfigurationScope Scope, string? Path);

internal static class ScreenResolution
{
    public static DisplayResolution Current
    {
        get
        {
            var width = GetSystemMetrics(0); var height = GetSystemMetrics(1);
            return AppLimits.FitResolution(width, height, "Current");
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}

internal static class ConfigurationStore
{
    public static string UserPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BGInfoPlus", "config.json");
    public static string AllUsersPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BGInfoPlus", "config.json");
    // The optional fallback travels with the executable. This keeps portable and
    // installed copies self-contained without creating a second ProgramData scope.
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "DefaultConfig.json");

    // Both UI and automatic paths show/use the same effective design: enforced all-users
    // policy first, then the user's file, then the application-local fallback.
    public static ResolvedConfiguration LoadForEditor() =>
        ConfigurationResolver.ResolveForEditor(UserPath, AllUsersPath, DefaultPath)
        ?? new ResolvedConfiguration(ConfigurationFileStore.CreateDefault(), ConfigurationScope.BuiltIn, null);

    public static ResolvedConfiguration? LoadForAutomaticApply() =>
        ConfigurationResolver.ResolveForAutomaticApply(UserPath, AllUsersPath, DefaultPath);

    public static void SaveUser(AppConfiguration configuration)
    {
        ConfigurationFileStore.Save(UserPath, configuration);
        UserStartupRegistration.SetEnabled(configuration.RunAtSignIn);
    }
}

internal static class ConfigurationFileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) }
    };

    public static AppConfiguration CreateDefault() => new(
        DocumentTools.Save(DocumentTools.NewDocument()),
        [
            new PlaceholderDefinition("Hostname", "Hostname", "Networking", "Hostname"),
            new PlaceholderDefinition("IPv4", "IPv4 addresses", "Networking", "IPv4")
        ],
        false, 1920, 1080, "#0F172A");

    public static AppConfiguration Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists) throw new FileNotFoundException("The BGInfoPlus configuration was not found.", fullPath);
        if (info.Length > AppLimits.MaxConfigurationFileBytes)
            throw new InvalidDataException("The BGInfoPlus configuration is too large.");
        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.SequentialScan);
        var configuration = JsonSerializer.Deserialize<AppConfiguration>(stream, JsonOptions)
            ?? throw new InvalidDataException("The BGInfoPlus configuration is empty.");
        AppLimits.Validate(configuration);
        return configuration;
    }

    public static void Save(string path, AppConfiguration configuration)
    {
        AppLimits.Validate(configuration);
        AtomicFile.Write(path, stream =>
        {
            JsonSerializer.Serialize(stream, configuration, JsonOptions);
            if (stream.Length > AppLimits.MaxConfigurationFileBytes)
                throw new InvalidDataException("The BGInfoPlus configuration is too large.");
        });
    }

    public static void SaveShared(string path, AppConfiguration configuration) =>
        Save(path, configuration with { RunAtSignIn = false });
}

internal static class ConfigurationResolver
{
    public static ResolvedConfiguration? ResolveForEditor(string userPath, string allUsersPath, string defaultPath) =>
        LoadFirst((allUsersPath, ConfigurationScope.AllUsers), (userPath, ConfigurationScope.User),
            (defaultPath, ConfigurationScope.Default));

    public static ResolvedConfiguration? ResolveForAutomaticApply(string userPath, string allUsersPath, string defaultPath) =>
        LoadFirst((allUsersPath, ConfigurationScope.AllUsers), (userPath, ConfigurationScope.User),
            (defaultPath, ConfigurationScope.Default));

    private static ResolvedConfiguration? LoadFirst(params (string Path, ConfigurationScope Scope)[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate.Path)) continue;
            // Do not silently fall through when a higher-precedence file is invalid. That
            // could bypass an administrator's all-users policy with a private user file.
            return new ResolvedConfiguration(ConfigurationFileStore.Load(candidate.Path), candidate.Scope, candidate.Path);
        }
        return null;
    }
}

internal static class UserStartupRegistration
{
    internal const string ShortcutFileName = "BGInfoPlus.lnk";
    public static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup), ShortcutFileName);

    public static void SetEnabled(bool enabled)
    {
        var shortcutPath = ShortcutPath;
        if (!enabled)
        {
            File.Delete(shortcutPath);
            return;
        }

        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("The BGInfoPlus executable path is unavailable.");
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        CreateShortcut(shortcutPath, executable);
    }

    internal static void CreateShortcut(string shortcutPath, string executable)
    {
        shortcutPath = Path.GetFullPath(shortcutPath);
        executable = Path.GetFullPath(executable);
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        ShellLinkInterop.Create(shortcutPath, executable);
    }
}
