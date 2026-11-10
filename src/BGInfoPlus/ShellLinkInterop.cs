using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.IO;

namespace BGInfoPlus;

[ComImport]
[Guid("000214F9-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maximumCharacters, IntPtr findData, uint flags);
    void GetIdList(out IntPtr itemIdList);
    void SetIdList(IntPtr itemIdList);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int maximumCharacters);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int maximumCharacters);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int maximumCharacters);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
    void GetHotkey(out short hotkey);
    void SetHotkey(short hotkey);
    void GetShowCommand(out int showCommand);
    void SetShowCommand(int showCommand);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maximumCharacters, out int iconIndex);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
    void Resolve(IntPtr window, uint flags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
}

internal static class ShellLinkInterop
{
    private static readonly Guid ShellLinkClassId = new("00021401-0000-0000-C000-000000000046");

    public static void Create(string shortcutPath, string executable)
    {
        var type = Type.GetTypeFromCLSID(ShellLinkClassId, throwOnError: true)
            ?? throw new PlatformNotSupportedException("The Windows Shell Link API is unavailable.");
        var comObject = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("The Windows Shell Link API could not be started.");
        try
        {
            var link = (IShellLinkW)comObject;
            link.SetPath(executable);
            link.SetArguments("--apply");
            link.SetWorkingDirectory(Path.GetDirectoryName(executable)!);
            link.SetIconLocation(executable, 0);
            link.SetDescription("Apply the BGInfoPlus wallpaper for the current user");
            ((IPersistFile)comObject).Save(shortcutPath, true);
        }
        finally
        {
            if (Marshal.IsComObject(comObject)) Marshal.FinalReleaseComObject(comObject);
        }
    }
}
