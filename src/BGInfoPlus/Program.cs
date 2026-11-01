using System.Windows;

namespace BGInfoPlus;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (args.Any(a => a.Equals("--apply", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var configuration = ConfigurationStore.Load();
                if (configuration is not null) WallpaperService.Apply(configuration);
            }
            catch (Exception exception)
            {
                AppLog.Error("Automatic wallpaper application failed.", exception);
                return 1;
            }
            return 0;
        }

        app.ShutdownMode = ShutdownMode.OnMainWindowClose;
        return app.Run(new Editor());
    }
}
