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
    private const string KeyPath = @"Software\BGInfoPlus";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static AppConfiguration? Load()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        var document = key?.GetValue("Document") as string;
        if (string.IsNullOrWhiteSpace(document)) return null;
        var placeholderJson = key?.GetValue("Placeholders") as string ?? "[]";
        if (placeholderJson.Length > AppLimits.MaxPlaceholderJsonCharacters)
            throw new InvalidDataException("The placeholder configuration is too large.");
        var placeholders = JsonSerializer.Deserialize<PlaceholderDefinition[]>(placeholderJson) ?? [];
        var current = ScreenResolution.Current;
        var width = key?.GetValue("ImageWidth") is int savedWidth ? savedWidth : current.Width;
        var height = key?.GetValue("ImageHeight") is int savedHeight ? savedHeight : current.Height;
        var background = key?.GetValue("BackgroundColor") as string ?? "#0F172A";
        var configuration = new AppConfiguration(document, placeholders, (key?.GetValue("RunAtSignIn") as int? ?? 0) != 0,
            width, height, background);
        AppLimits.Validate(configuration);
        return configuration;
    }

    public static void Save(AppConfiguration configuration)
    {
        AppLimits.Validate(configuration);
        using (var key = Registry.CurrentUser.CreateSubKey(KeyPath))
        {
            key.SetValue("Placeholders", JsonSerializer.Serialize(configuration.Placeholders), RegistryValueKind.String);
            key.SetValue("RunAtSignIn", configuration.RunAtSignIn ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue("ImageWidth", configuration.ImageWidth, RegistryValueKind.DWord);
            key.SetValue("ImageHeight", configuration.ImageHeight, RegistryValueKind.DWord);
            key.SetValue("BackgroundColor", configuration.BackgroundColor, RegistryValueKind.String);
            // Write the document last; its presence is the configuration commit marker.
            key.SetValue("Document", configuration.Document, RegistryValueKind.String);
        }
        using var run = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (configuration.RunAtSignIn) run.SetValue("BGInfoPlus", $"\"{Environment.ProcessPath}\" --apply", RegistryValueKind.String);
        else run.DeleteValue("BGInfoPlus", false);
    }
}

internal static class ConfigurationFileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppConfiguration Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists) throw new FileNotFoundException("The shared BGInfoPlus configuration was not found.", fullPath);
        if (info.Length > AppLimits.MaxConfigurationFileBytes)
            throw new InvalidDataException("The shared BGInfoPlus configuration is too large.");
        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.SequentialScan);
        var configuration = JsonSerializer.Deserialize<AppConfiguration>(stream, JsonOptions)
            ?? throw new InvalidDataException("The shared BGInfoPlus configuration is empty.");
        AppLimits.Validate(configuration);
        return configuration;
    }

    public static void Save(string path, AppConfiguration configuration)
    {
        configuration = configuration with { RunAtSignIn = false };
        AppLimits.Validate(configuration);
        AtomicFile.Write(path, stream =>
        {
            JsonSerializer.Serialize(stream, configuration, JsonOptions);
            if (stream.Length > AppLimits.MaxConfigurationFileBytes)
                throw new InvalidDataException("The shared BGInfoPlus configuration is too large.");
        });
    }
}
