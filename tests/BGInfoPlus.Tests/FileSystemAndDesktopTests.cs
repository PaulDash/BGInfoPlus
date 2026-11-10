using System.Text;
using BGInfoPlus;
using Xunit;

namespace BGInfoPlus.Tests;

public sealed class FileSystemAndDesktopTests
{
    [Fact]
    public void LoginShortcutCanBeCreatedWithoutRegistryStartupState()
    {
        var directory = TestSupport.CreateTemporaryDirectory("BGInfoPlus.ShortcutTests");
        try
        {
            var shortcutPath = Path.Combine(directory, UserStartupRegistration.ShortcutFileName);
            UserStartupRegistration.CreateShortcut(shortcutPath, Environment.ProcessPath!);
            Assert.True(File.Exists(shortcutPath));
            Assert.True(new FileInfo(shortcutPath).Length > 0);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void AtomicWriterReplacesDestinationAndCleansTemporaryFiles()
    {
        var directory = TestSupport.CreateTemporaryDirectory("BGInfoPlus.AtomicTests");
        try
        {
            var path = Path.Combine(directory, "image.bin");
            File.WriteAllText(path, "old");
            AtomicFile.Write(path, stream => stream.Write(Encoding.UTF8.GetBytes("new")));
            Assert.Equal("new", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(directory));
            Assert.Throws<IOException>(() => AtomicFile.Write(path, stream =>
            {
                stream.WriteByte(1);
                throw new IOException("injected");
            }));
            Assert.Equal("new", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void InteractiveWallpaperStateCanBeCapturedWhenEnabled()
    {
        // Desktop COM state is unavailable to some headless build agents.
        if (Environment.GetEnvironmentVariable("BGINFOPLUS_TEST_DESKTOP") != "1") return;
        TestSupport.RunSta(() =>
        {
            using var snapshot = WallpaperService.CaptureCurrentConfiguration();
            Assert.NotNull(snapshot);
            Assert.True(Enum.IsDefined(snapshot.Kind));
        });
    }
}
