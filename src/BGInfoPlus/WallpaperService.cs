using System.Runtime.InteropServices;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading;
using Microsoft.Win32;

namespace BGInfoPlus;

internal static class WallpaperService
{
    private const string DesktopRegistryPath = @"Control Panel\Desktop";
    private const string WallpaperRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers";
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
            SetBackgroundType((int)PersonalizationBackgroundKind.Picture);
            BroadcastWallpaperSettingChange();
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }

    public static WindowsWallpaperConfiguration? CaptureCurrentConfiguration()
    {
        IDesktopWallpaper? desktopWallpaper = null;
        IShellItemArray? slideshowItems = null;
        try
        {
            using var desktop = Registry.CurrentUser.OpenSubKey(DesktopRegistryPath);
            using var wallpapers = Registry.CurrentUser.OpenSubKey(WallpaperRegistryPath);
            var fallbackPath = desktop?.GetValue("WallPaper")?.ToString() ?? string.Empty;
            var backgroundType = ReadInt32(wallpapers, "BackgroundType");

            desktopWallpaper = DesktopWallpaperInterop.Create();
            DesktopWallpaperInterop.ThrowIfFailed(desktopWallpaper.GetPosition(out var position));
            DesktopWallpaperInterop.ThrowIfFailed(desktopWallpaper.GetBackgroundColor(out var backgroundColor));
            DesktopWallpaperInterop.ThrowIfFailed(desktopWallpaper.GetStatus(out var slideshowState));
            DesktopWallpaperInterop.ThrowIfFailed(desktopWallpaper.GetMonitorDevicePathCount(out var monitorCount));
            var monitorWallpapers = new List<MonitorWallpaper>(checked((int)monitorCount));
            for (uint index = 0; index < monitorCount; index++)
            {
                var monitorId = ReadDesktopString(desktopWallpaper.GetMonitorDevicePathAt, index);
                var path = ReadDesktopString(desktopWallpaper.GetWallpaper, monitorId);
                monitorWallpapers.Add(new MonitorWallpaper(monitorId, path));
            }

            var kind = DetermineBackgroundKind(backgroundType, slideshowState, monitorWallpapers.Any(item => !string.IsNullOrWhiteSpace(item.Path)));
            var slideshowOptions = DesktopSlideshowOptions.None;
            uint slideshowInterval = 0;
            if (kind == PersonalizationBackgroundKind.Slideshow)
            {
                DesktopWallpaperInterop.ThrowIfFailed(desktopWallpaper.GetSlideshow(out slideshowItems));
                DesktopWallpaperInterop.ThrowIfFailed(desktopWallpaper.GetSlideshowOptions(out slideshowOptions, out slideshowInterval));
            }

            var configuration = new WindowsWallpaperConfiguration
            {
                Kind = kind,
                BackgroundType = backgroundType,
                FallbackPath = fallbackPath,
                WallpaperStyle = desktop?.GetValue("WallpaperStyle")?.ToString(),
                TileWallpaper = desktop?.GetValue("TileWallpaper")?.ToString(),
                BackgroundColor = backgroundColor,
                Position = position,
                MonitorWallpapers = monitorWallpapers,
                SlideshowItems = slideshowItems,
                SlideshowOptions = slideshowOptions,
                SlideshowIntervalMilliseconds = slideshowInterval
            };
            slideshowItems = null; // Ownership moves to the configuration snapshot.
            return configuration;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or COMException)
        {
            return null;
        }
        finally
        {
            if (slideshowItems is not null && Marshal.IsComObject(slideshowItems)) Marshal.FinalReleaseComObject(slideshowItems);
            if (desktopWallpaper is not null && Marshal.IsComObject(desktopWallpaper)) Marshal.FinalReleaseComObject(desktopWallpaper);
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
            RestoreDesktopConfiguration(configuration);
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
        using var desktop = Registry.CurrentUser.CreateSubKey(DesktopRegistryPath, writable: true)
            ?? throw new InvalidOperationException("The Windows wallpaper settings could not be opened.");
        desktop.SetValue("WallpaperStyle", values.WallpaperStyle, RegistryValueKind.String);
        desktop.SetValue("TileWallpaper", values.TileWallpaper, RegistryValueKind.String);
    }

    private static void RestoreRegistryValue(RegistryKey key, string name, string? value)
    {
        if (value is null) key.DeleteValue(name, throwOnMissingValue: false);
        else key.SetValue(name, value, RegistryValueKind.String);
    }

    internal static PersonalizationBackgroundKind DetermineBackgroundKind(int? backgroundType, DesktopSlideshowState slideshowState, bool hasWallpaper) =>
        backgroundType switch
        {
            (int)PersonalizationBackgroundKind.WindowsSpotlight => PersonalizationBackgroundKind.WindowsSpotlight,
            (int)PersonalizationBackgroundKind.SolidColor => PersonalizationBackgroundKind.SolidColor,
            (int)PersonalizationBackgroundKind.Slideshow => PersonalizationBackgroundKind.Slideshow,
            (int)PersonalizationBackgroundKind.Picture => PersonalizationBackgroundKind.Picture,
            _ when slideshowState.HasFlag(DesktopSlideshowState.Slideshow) => PersonalizationBackgroundKind.Slideshow,
            _ when !hasWallpaper => PersonalizationBackgroundKind.SolidColor,
            _ => PersonalizationBackgroundKind.Picture
        };

    private static void RestoreDesktopConfiguration(WindowsWallpaperConfiguration configuration)
    {
        IDesktopWallpaper? desktopWallpaper = null;
        try
        {
            desktopWallpaper = DesktopWallpaperInterop.Create();
            DesktopWallpaperInterop.ThrowIfFailed(desktopWallpaper.SetBackgroundColor(configuration.BackgroundColor));
            DesktopWallpaperInterop.ThrowIfFailed(desktopWallpaper.SetPosition(configuration.Position));

            if (configuration.Kind == PersonalizationBackgroundKind.Slideshow && configuration.SlideshowItems is not null)
            {
                DesktopWallpaperInterop.ThrowIfFailed(desktopWallpaper.SetSlideshow(configuration.SlideshowItems));
                DesktopWallpaperInterop.ThrowIfFailed(desktopWallpaper.SetSlideshowOptions(
                    configuration.SlideshowOptions, configuration.SlideshowIntervalMilliseconds));
            }
            else if (configuration.Kind == PersonalizationBackgroundKind.SolidColor)
            {
                if (!SystemParametersInfo(0x0014, 0, string.Empty, 0x0001 | 0x0002))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            else
            {
                RestoreMonitorWallpapers(desktopWallpaper, configuration);
            }
        }
        finally
        {
            if (desktopWallpaper is not null && Marshal.IsComObject(desktopWallpaper)) Marshal.FinalReleaseComObject(desktopWallpaper);
        }

        // BackgroundType is the selector used by Windows Personalization. Spotlight has
        // no public desktop API, so restoring this selector and its captured current image
        // allows the Windows content service to resume managing subsequent images.
        using var desktop = Registry.CurrentUser.CreateSubKey(DesktopRegistryPath, writable: true)
            ?? throw new InvalidOperationException("The Windows wallpaper settings could not be opened.");
        RestoreRegistryValue(desktop, "WallpaperStyle", configuration.WallpaperStyle);
        RestoreRegistryValue(desktop, "TileWallpaper", configuration.TileWallpaper);
        if (configuration.BackgroundType is int backgroundType) SetBackgroundType(backgroundType);
        BroadcastWallpaperSettingChange();
    }

    private static void RestoreMonitorWallpapers(IDesktopWallpaper desktopWallpaper, WindowsWallpaperConfiguration configuration)
    {
        if (configuration.MonitorWallpapers.Count == 0)
        {
            DesktopWallpaperInterop.ThrowIfFailed(desktopWallpaper.SetWallpaper(null, configuration.FallbackPath));
            return;
        }

        foreach (var item in configuration.MonitorWallpapers)
            DesktopWallpaperInterop.ThrowIfFailed(desktopWallpaper.SetWallpaper(item.MonitorId, item.Path));
    }

    private static string ReadDesktopString(DesktopWallpaperIndexedStringGetter getter, uint index)
    {
        var result = getter(index, out var value);
        return DesktopWallpaperInterop.ReadAndFreeString(result, value);
    }

    private static string ReadDesktopString(DesktopWallpaperStringGetter getter, string argument)
    {
        var result = getter(argument, out var value);
        return DesktopWallpaperInterop.ReadAndFreeString(result, value);
    }

    private delegate int DesktopWallpaperIndexedStringGetter(uint index, out IntPtr value);
    private delegate int DesktopWallpaperStringGetter(string argument, out IntPtr value);

    private static int? ReadInt32(RegistryKey? key, string name)
    {
        var value = key?.GetValue(name);
        if (value is null) return null;
        try { return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture); }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException) { return null; }
    }

    private static void SetBackgroundType(int backgroundType)
    {
        using var wallpapers = Registry.CurrentUser.CreateSubKey(WallpaperRegistryPath, writable: true)
            ?? throw new InvalidOperationException("The Windows personalization settings could not be opened.");
        wallpapers.SetValue("BackgroundType", backgroundType, RegistryValueKind.DWord);
    }

    private static void BroadcastWallpaperSettingChange()
    {
        _ = SendMessageTimeout(new IntPtr(0xffff), 0x001A, IntPtr.Zero, DesktopRegistryPath,
            0x0002, 2000, out _);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, string value, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam,
        string lParam, uint flags, uint timeout, out IntPtr result);
}
