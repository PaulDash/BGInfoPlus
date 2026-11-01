using System.Runtime.InteropServices;
using System.Windows.Media;

namespace BGInfoPlus;

internal static class NativeColorDialog
{
    private const uint CcRgbInit = 0x00000001;
    private const uint CcFullOpen = 0x00000002;
    private const uint CcAnyColor = 0x00000100;
    private const uint CcSolidColor = 0x00000080;

    public static Color? Show(nint owner, Color initial)
    {
        nint customColors = Marshal.AllocHGlobal(16 * sizeof(uint));
        try
        {
            for (var offset = 0; offset < 16 * sizeof(uint); offset += sizeof(uint))
                Marshal.WriteInt32(customColors, offset, 0x00FFFFFF);

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
            Marshal.FreeHGlobal(customColors);
        }
    }

    private static uint ToColorRef(Color color) =>
        color.R | ((uint)color.G << 8) | ((uint)color.B << 16);

    private static Color FromColorRef(uint value) => Color.FromRgb(
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
