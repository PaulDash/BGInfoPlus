using System.Runtime.InteropServices;
using System.Windows.Media;

namespace BGInfoPlus;

internal static class NativeColorDialog
{
    private const uint CcRgbInit = 0x00000001;
    private const uint CcFullOpen = 0x00000002;
    private const uint CcAnyColor = 0x00000100;
    private const uint CcSolidColor = 0x00000080;

    public static Color? Show(nint owner, Color initial, NativeColorPalette customPalette)
    {
        ArgumentNullException.ThrowIfNull(customPalette);
        nint customColors = Marshal.AllocHGlobal(NativeColorPalette.ColorCount * sizeof(int));
        try
        {
            customPalette.CopyTo(customColors);

            var state = new ChooseColorState
            {
                Size = (uint)Marshal.SizeOf<ChooseColorState>(),
                Owner = owner,
                Color = ToColorRef(initial),
                CustomColors = customColors,
                Flags = CcRgbInit | CcFullOpen | CcAnyColor | CcSolidColor
            };

            return ChooseColor(ref state) ? FromColorRef(state.Color) : null;
        }
        finally
        {
            // CHOOSECOLOR edits this caller-owned buffer when "Add to Custom Colors"
            // is used. Preserve it even if the user later cancels the selection.
            try { customPalette.CopyFrom(customColors); }
            finally { Marshal.FreeHGlobal(customColors); }
        }
    }

    internal static uint ToColorRef(Color color) =>
        color.R | ((uint)color.G << 8) | ((uint)color.B << 16);

    internal static Color FromColorRef(uint value) => Color.FromRgb(
        (byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF));

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChooseColor(ref ChooseColorState state);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ChooseColorState
    {
        public uint Size;
        public nint Owner;
        public nint Instance;
        public uint Color;
        public nint CustomColors;
        public uint Flags;
        public nint CustomData;
        public nint Hook;
        public nint TemplateName;
    }
}

/// <summary>
/// Owns the 16 COLORREF values required by the native Windows color dialog.
/// One instance is retained by the editor so font and wallpaper dialogs share
/// custom colors for the lifetime of that editor session.
/// </summary>
internal sealed class NativeColorPalette
{
    internal const int ColorCount = 16;
    private const int WhiteColorRef = 0x00FFFFFF;
    private readonly int[] colors = new int[ColorCount];

    internal NativeColorPalette()
    {
        Array.Fill(colors, WhiteColorRef);
    }

    internal void CopyTo(nint destination) => Marshal.Copy(colors, 0, destination, colors.Length);

    internal void CopyFrom(nint source) => Marshal.Copy(source, colors, 0, colors.Length);

    internal int[] Snapshot() => (int[])colors.Clone();
}
