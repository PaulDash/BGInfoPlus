using System.Windows;

namespace BGInfoPlus;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var exportIndex = Array.FindIndex(args, a => a.Equals("--export-config", StringComparison.OrdinalIgnoreCase));
        if (exportIndex >= 0)
        {
            try
            {
                if (exportIndex + 1 >= args.Length || string.IsNullOrWhiteSpace(args[exportIndex + 1]))
                    throw new ArgumentException("--export-config requires a destination path.");
                var configuration = ConfigurationStore.LoadForEditor().Configuration;
                ConfigurationFileStore.SaveShared(args[exportIndex + 1], configuration);
                return 0;
            }
            catch (Exception exception)
            {
                AppLog.Error("Shared configuration export failed.", exception);
                return 1;
            }
        }
        var defaultExportIndex = Array.FindIndex(args, a => a.Equals("--export-default-config", StringComparison.OrdinalIgnoreCase));
        if (defaultExportIndex >= 0)
        {
            try
            {
                if (defaultExportIndex + 1 >= args.Length || string.IsNullOrWhiteSpace(args[defaultExportIndex + 1]))
                    throw new ArgumentException("--export-default-config requires a destination path.");
                ConfigurationFileStore.SaveShared(args[defaultExportIndex + 1], ConfigurationFileStore.CreateDefault());
                return 0;
            }
            catch (Exception exception)
            {
                AppLog.Error("Default configuration export failed.", exception);
                return 1;
            }
        }
        var installConfigIndex = Array.FindIndex(args, a => a.Equals("--install-config", StringComparison.OrdinalIgnoreCase));
        if (installConfigIndex >= 0)
        {
            try
            {
                if (installConfigIndex + 2 >= args.Length || string.IsNullOrWhiteSpace(args[installConfigIndex + 1]) ||
                    string.IsNullOrWhiteSpace(args[installConfigIndex + 2]))
                    throw new ArgumentException("--install-config requires a source and destination path.");
                ConfigurationFileStore.SaveShared(args[installConfigIndex + 2],
                    ConfigurationFileStore.Load(args[installConfigIndex + 1]));
                return 0;
            }
            catch (Exception exception)
            {
                AppLog.Error("Shared configuration installation failed.", exception);
                return 1;
            }
        }
        if (args.Any(a => a.Equals("--apply", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var configIndex = Array.FindIndex(args, a => a.Equals("--config", StringComparison.OrdinalIgnoreCase));
                var resolved = configIndex >= 0
                    ? new ResolvedConfiguration(
                        ConfigurationFileStore.Load(configIndex + 1 < args.Length
                            ? args[configIndex + 1]
                            : throw new ArgumentException("--config requires a file path.")),
                        ConfigurationScope.Explicit, args[configIndex + 1])
                    : ConfigurationStore.LoadForAutomaticApply();
                if (resolved is not null) WallpaperService.Apply(resolved.Configuration);
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
