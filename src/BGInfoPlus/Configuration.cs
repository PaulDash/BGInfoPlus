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
