using System.IO;
using System.Text;

namespace BGInfoPlus;

internal static class AppLog
{
    private const long MaximumBytes = 256 * 1024;
    private static readonly object Sync = new();
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BGInfoPlus");
    public static readonly string PathName = Path.Combine(DirectoryPath, "BGInfoPlus.log");

    public static void Error(string operation, Exception exception) => Write("ERROR", operation, exception);
    public static void Warning(string operation, Exception exception) => Write("WARN", operation, exception);

    private static void Write(string severity, string operation, Exception exception)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(DirectoryPath);
                if (File.Exists(PathName) && new FileInfo(PathName).Length >= MaximumBytes)
                    File.Move(PathName, PathName + ".old", overwrite: true);
                var message = exception.ToString();
                if (message.Length > 8192) message = message[..8192] + "…";
                File.AppendAllText(PathName,
                    $"{DateTimeOffset.Now:O} [{severity}] {operation}{Environment.NewLine}{message}{Environment.NewLine}",
                    new UTF8Encoding(false));
            }
        }
        catch { /* Diagnostics must never prevent wallpaper application. */ }
    }
}
