using System.Runtime.InteropServices;

namespace BGInfoPlus;

internal enum PersonalizationBackgroundKind
{
    Unknown = -1,
    Picture = 0,
    SolidColor = 1,
    Slideshow = 2,
    WindowsSpotlight = 3
}

internal enum DesktopWallpaperPosition
{
    Center,
    Tile,
    Stretch,
    Fit,
    Fill,
    Span
}

[Flags]
internal enum DesktopSlideshowState
{
    Enabled = 0x1,
    Slideshow = 0x2,
    DisabledByRemoteSession = 0x4
}

[Flags]
internal enum DesktopSlideshowOptions
{
    None = 0,
    ShuffleImages = 0x1
}

internal sealed record MonitorWallpaper(string MonitorId, string Path);

// The shell owns this object. Keeping its COM reference for the editor session lets
// us restore the exact slideshow item array without serializing undocumented PIDLs.
internal sealed class WindowsWallpaperConfiguration : IDisposable
{
    public required PersonalizationBackgroundKind Kind { get; init; }
    public required int? BackgroundType { get; init; }
    public required string FallbackPath { get; init; }
    public required string? WallpaperStyle { get; init; }
    public required string? TileWallpaper { get; init; }
    public required uint BackgroundColor { get; init; }
    public required DesktopWallpaperPosition Position { get; init; }
    public required IReadOnlyList<MonitorWallpaper> MonitorWallpapers { get; init; }
    public IShellItemArray? SlideshowItems { get; init; }
    public DesktopSlideshowOptions SlideshowOptions { get; init; }
    public uint SlideshowIntervalMilliseconds { get; init; }

    public void Dispose()
    {
        if (SlideshowItems is not null && Marshal.IsComObject(SlideshowItems))
            Marshal.FinalReleaseComObject(SlideshowItems);
    }
}

[ComImport]
[Guid("B63EA76D-1F85-456F-A19C-48159EFA858B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemArray { }

[ComImport]
[Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDesktopWallpaper
{
    [PreserveSig] int SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
    [PreserveSig] int GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, out IntPtr wallpaper);
    [PreserveSig] int GetMonitorDevicePathAt(uint monitorIndex, out IntPtr monitorId);
    [PreserveSig] int GetMonitorDevicePathCount(out uint count);
    [PreserveSig] int GetMonitorRect([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out NativeRect displayRect);
    [PreserveSig] int SetBackgroundColor(uint color);
    [PreserveSig] int GetBackgroundColor(out uint color);
    [PreserveSig] int SetPosition(DesktopWallpaperPosition position);
    [PreserveSig] int GetPosition(out DesktopWallpaperPosition position);
    [PreserveSig] int SetSlideshow([MarshalAs(UnmanagedType.Interface)] IShellItemArray items);
    [PreserveSig] int GetSlideshow([MarshalAs(UnmanagedType.Interface)] out IShellItemArray? items);
    [PreserveSig] int SetSlideshowOptions(DesktopSlideshowOptions options, uint slideshowTick);
    [PreserveSig] int GetSlideshowOptions(out DesktopSlideshowOptions options, out uint slideshowTick);
    [PreserveSig] int AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, int direction);
    [PreserveSig] int GetStatus(out DesktopSlideshowState state);
    [PreserveSig] int Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

internal static class DesktopWallpaperInterop
{
    private static readonly Guid DesktopWallpaperClassId = new("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD");

    public static IDesktopWallpaper Create()
    {
        var type = Type.GetTypeFromCLSID(DesktopWallpaperClassId, throwOnError: true)
            ?? throw new PlatformNotSupportedException("The Windows desktop wallpaper API is unavailable.");
        return (IDesktopWallpaper)(Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("The Windows desktop wallpaper API could not be started."));
    }

    public static void ThrowIfFailed(int result)
    {
        if (result < 0) Marshal.ThrowExceptionForHR(result);
    }

    public static string ReadAndFreeString(int result, IntPtr value)
    {
        try
        {
            ThrowIfFailed(result);
            return value == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(value) ?? string.Empty;
        }
        finally
        {
            if (value != IntPtr.Zero) Marshal.FreeCoTaskMem(value);
        }
    }
}
