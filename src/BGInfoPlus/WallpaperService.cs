using System.Runtime.InteropServices;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading;

namespace BGInfoPlus;

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
            if (!SystemParametersInfo(0x0014, 0, path, 0x0001 | 0x0002))
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, string value, uint flags);
}
