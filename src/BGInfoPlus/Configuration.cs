using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.IO;

namespace BGInfoPlus;

internal sealed record PlaceholderDefinition(string Name, string Label, string Kind, string Argument);
internal sealed record DisplayResolution(int Width, int Height, string? Description = null)
{
    public override string ToString() => $"{Width} × {Height}" + (Description is null ? "" : $"  {Description}");
}
internal sealed record AppConfiguration(string Document, PlaceholderDefinition[] Placeholders, bool RunAtSignIn, int ImageWidth, int ImageHeight, string BackgroundColor);
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
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BGInfoPlus", "default-config.json");

    // Both UI and automatic paths show/use the same effective design: enforced all-users
    // policy first, then the user's file, then the machine fallback for unconfigured users.
    public static ResolvedConfiguration LoadForEditor()
    {
        MigrateLegacyRegistryConfiguration();
        return ConfigurationResolver.ResolveForEditor(UserPath, AllUsersPath, DefaultPath)
            ?? new ResolvedConfiguration(ConfigurationFileStore.CreateDefault(), ConfigurationScope.BuiltIn, null);
    }

    public static ResolvedConfiguration? LoadForAutomaticApply()
    {
        MigrateLegacyRegistryConfiguration();
        return ConfigurationResolver.ResolveForAutomaticApply(UserPath, AllUsersPath, DefaultPath);
    }

    public static void SaveUser(AppConfiguration configuration)
    {
        ConfigurationFileStore.Save(UserPath, configuration);
        UserStartupRegistration.SetEnabled(configuration.RunAtSignIn);
    }

    public static bool HasMachineLogonConfiguration => File.Exists(AllUsersPath) || File.Exists(DefaultPath);

    private static void MigrateLegacyRegistryConfiguration()
    {
        if (File.Exists(UserPath)) return;
        LegacyRegistryConfiguration.TryMigrate(UserPath, !HasMachineLogonConfiguration);
    }
}

internal static class ConfigurationFileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

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
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new PlatformNotSupportedException("Windows Script Host is unavailable.");
        dynamic? shell = null;
        dynamic? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType) ?? throw new InvalidOperationException("Windows Script Host could not be started.");
            shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = executable;
            shortcut.Arguments = "--apply";
            shortcut.WorkingDirectory = Path.GetDirectoryName(executable)!;
            shortcut.IconLocation = executable + ",0";
            shortcut.Description = "Apply the BGInfoPlus wallpaper for the current user";
            shortcut.Save();
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }
}

internal static class LegacyRegistryConfiguration
{
    private const string KeyPath = @"Software\BGInfoPlus";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void TryMigrate(string userPath, bool createStartupShortcut)
        => TryMigrate(userPath, createStartupShortcut, KeyPath, RunKeyPath);

    internal static void TryMigrate(string userPath, bool createStartupShortcut, string keyPath, string runKeyPath)
    {
        AppConfiguration? configuration;
        using (var key = Registry.CurrentUser.OpenSubKey(keyPath))
        {
            var document = key?.GetValue("Document") as string;
            if (string.IsNullOrWhiteSpace(document)) return;
            var placeholderJson = key?.GetValue("Placeholders") as string ?? "[]";
            if (placeholderJson.Length > AppLimits.MaxPlaceholderJsonCharacters)
                throw new InvalidDataException("The legacy placeholder configuration is too large.");
            var placeholders = JsonSerializer.Deserialize<PlaceholderDefinition[]>(placeholderJson) ?? [];
            var current = ScreenResolution.Current;
            configuration = new AppConfiguration(document, placeholders,
                (key?.GetValue("RunAtSignIn") as int? ?? 0) != 0,
                key?.GetValue("ImageWidth") is int width ? width : current.Width,
                key?.GetValue("ImageHeight") is int height ? height : current.Height,
                key?.GetValue("BackgroundColor") as string ?? "#0F172A");
        }
        AppLimits.Validate(configuration);

        // Save and establish the replacement startup mechanism before deleting the old data.
        ConfigurationFileStore.Save(userPath, configuration);
        UserStartupRegistration.SetEnabled(createStartupShortcut && configuration.RunAtSignIn);
        using (var run = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: true)) run?.DeleteValue("BGInfoPlus", false);
        Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
    }
}
