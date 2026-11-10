using System.Runtime.InteropServices;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading;
using Microsoft.Win32;

namespace BGInfoPlus;

internal sealed record WindowsWallpaperConfiguration(string Path, string? WallpaperStyle, string? TileWallpaper);

internal static class WallpaperService
{
    private static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BGInfoPlus");
    private static readonly SemaphoreSlim RenderGate = new(1, 1);

    public static void Apply(AppConfiguration configuration)
    {
        AppLimits.Validate(configuration);
        RenderGate.Wait();
        try
        {
            var values = DataSources.ResolveAsync(configuration.Placeholders).GetAwaiter().GetResult();
            ApplyResolved(configuration, values);
        }
        finally { RenderGate.Release(); }
    }

    public static async Task ApplyAsync(AppConfiguration configuration)
    {
        AppLimits.Validate(configuration);
        await RenderGate.WaitAsync();
        try
        {
            var values = await DataSources.ResolveAsync(configuration.Placeholders);
            ApplyResolved(configuration, values);
        }
        finally { RenderGate.Release(); }
    }

    private static void ApplyResolved(AppConfiguration configuration, IReadOnlyDictionary<string, string> values)
    {
        using var mutex = new Mutex(false, @"Local\BGInfoPlus.WallpaperApply");
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(15)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("Another BGInfoPlus wallpaper operation is still running.");
            var bitmap = RenderResolved(configuration, values);
            var path = Path.Combine(DataDirectory, "wallpaper.bmp");
            var encoder = new BmpBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            AtomicFile.Write(path, encoder.Save);
            SetWallpaperFit(configuration.WallpaperFit);
            if (!SystemParametersInfo(0x0014, 0, path, 0x0001 | 0x0002))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }

    public static WindowsWallpaperConfiguration? CaptureCurrentConfiguration()
    {
        try
        {
            using var desktop = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            return new WindowsWallpaperConfiguration(
                desktop?.GetValue("WallPaper")?.ToString() ?? string.Empty,
                desktop?.GetValue("WallpaperStyle")?.ToString(),
                desktop?.GetValue("TileWallpaper")?.ToString());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    public static void RestoreConfiguration(WindowsWallpaperConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        using var mutex = new Mutex(false, @"Local\BGInfoPlus.WallpaperApply");
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(15)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("Another BGInfoPlus wallpaper operation is still running.");
            using var desktop = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop", writable: true)
                ?? throw new InvalidOperationException("The Windows wallpaper settings could not be opened.");
            RestoreRegistryValue(desktop, "WallpaperStyle", configuration.WallpaperStyle);
            RestoreRegistryValue(desktop, "TileWallpaper", configuration.TileWallpaper);
            if (!SystemParametersInfo(0x0014, 0, configuration.Path, 0x0001 | 0x0002))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }

    public static async Task<RenderTargetBitmap> RenderAsync(AppConfiguration configuration)
    {
        AppLimits.Validate(configuration);
        await RenderGate.WaitAsync();
        try
        {
            var values = await DataSources.ResolveAsync(configuration.Placeholders);
            return RenderResolved(configuration, values);
        }
        finally { RenderGate.Release(); }
    }

    internal static RenderTargetBitmap RenderResolved(AppConfiguration configuration, IReadOnlyDictionary<string, string> values)
    {
        AppLimits.Validate(configuration);
        var width = configuration.ImageWidth;
        var height = configuration.ImageHeight;
        var document = DocumentTools.Resolve(configuration.Document, values);
        document.PageWidth = width; document.PageHeight = height; document.ColumnWidth = width;
        var viewer = new FlowDocumentScrollViewer
        {
            Width = width, Height = height, Document = document,
            Background = Brushes.Transparent,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            IsToolBarVisible = false
        };
        var surface = new Grid { Width = width, Height = height, Background = new SolidColorBrush(ParseColor(configuration.BackgroundColor)) };
        surface.Children.Add(viewer);
        surface.Measure(new Size(width, height)); surface.Arrange(new Rect(0, 0, width, height)); surface.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface); bitmap.Freeze();
        return bitmap;
    }

    private static Color ParseColor(string value)
    {
        try { return (Color)ColorConverter.ConvertFromString(value); }
        catch { return Color.FromRgb(15, 23, 42); }
    }

    internal static (string WallpaperStyle, string TileWallpaper) FitRegistryValues(WallpaperFit fit) => fit switch
    {
        WallpaperFit.Fill => ("10", "0"),
        WallpaperFit.Fit => ("6", "0"),
        WallpaperFit.Stretch => ("2", "0"),
        _ => throw new InvalidDataException("The wallpaper fit mode is invalid.")
    };

    private static void SetWallpaperFit(WallpaperFit fit)
    {
        var values = FitRegistryValues(fit);
        using var desktop = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop", writable: true)
            ?? throw new InvalidOperationException("The Windows wallpaper settings could not be opened.");
        desktop.SetValue("WallpaperStyle", values.WallpaperStyle, RegistryValueKind.String);
        desktop.SetValue("TileWallpaper", values.TileWallpaper, RegistryValueKind.String);
    }

    private static void RestoreRegistryValue(RegistryKey key, string name, string? value)
    {
        if (value is null) key.DeleteValue(name, throwOnMissingValue: false);
        else key.SetValue(name, value, RegistryValueKind.String);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, string value, uint flags);
}
