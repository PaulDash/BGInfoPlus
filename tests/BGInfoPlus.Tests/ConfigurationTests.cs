using System.Text.Json;
using BGInfoPlus;
using Xunit;

namespace BGInfoPlus.Tests;

public sealed class ConfigurationTests
{
    private static AppConfiguration SharedConfiguration => new("<FlowDocument />",
        [new PlaceholderDefinition("Hostname", "Hostname", "Networking", "Hostname")],
        true, 1920, 1080, "#102030", WallpaperFit.Stretch);

    [Fact]
    public void ConfigurationRoundTripsAndSharedSaveRemovesPersonalStartupPolicy()
    {
        var directory = TestSupport.CreateTemporaryDirectory("BGInfoPlus.ConfigTests");
        try
        {
            var path = Path.Combine(directory, "config.json");
            ConfigurationFileStore.Save(path, SharedConfiguration);
            var loaded = ConfigurationFileStore.Load(path);
            Assert.Equal(SharedConfiguration.Document, loaded.Document);
            Assert.Equal(SharedConfiguration.Placeholders, loaded.Placeholders);
            Assert.True(loaded.RunAtSignIn);
            Assert.Equal(WallpaperFit.Stretch, loaded.WallpaperFit);
            ConfigurationFileStore.SaveShared(path, SharedConfiguration);
            Assert.False(ConfigurationFileStore.Load(path).RunAtSignIn);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void OmittedOptionalWallpaperFitDefaultsToFill()
    {
        var directory = TestSupport.CreateTemporaryDirectory("BGInfoPlus.ConfigTests");
        try
        {
            var path = Path.Combine(directory, "config.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                SharedConfiguration.Document, SharedConfiguration.Placeholders, SharedConfiguration.RunAtSignIn,
                SharedConfiguration.ImageWidth, SharedConfiguration.ImageHeight, SharedConfiguration.BackgroundColor
            }));
            Assert.Equal(WallpaperFit.Fill, ConfigurationFileStore.Load(path).WallpaperFit);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void UnknownConfigurationOptionsAreRejected()
    {
        var directory = TestSupport.CreateTemporaryDirectory("BGInfoPlus.ConfigTests");
        try
        {
            var path = Path.Combine(directory, "config.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                SharedConfiguration.Document, SharedConfiguration.Placeholders, SharedConfiguration.RunAtSignIn,
                SharedConfiguration.ImageWidth, SharedConfiguration.ImageHeight, SharedConfiguration.BackgroundColor,
                UnexpectedOption = true
            }));
            Assert.Throws<JsonException>(() => { ConfigurationFileStore.Load(path); });
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void NumericWallpaperFitIsRejected()
    {
        var directory = TestSupport.CreateTemporaryDirectory("BGInfoPlus.ConfigTests");
        try
        {
            var path = Path.Combine(directory, "config.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                SharedConfiguration.Document, SharedConfiguration.Placeholders, SharedConfiguration.RunAtSignIn,
                SharedConfiguration.ImageWidth, SharedConfiguration.ImageHeight, SharedConfiguration.BackgroundColor,
                WallpaperFit = 1
            }));
            Assert.Throws<JsonException>(() => { ConfigurationFileStore.Load(path); });
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void NullPlaceholderMembersAreRejectedSafely()
    {
        var directory = TestSupport.CreateTemporaryDirectory("BGInfoPlus.ConfigTests");
        try
        {
            var path = Path.Combine(directory, "config.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                SharedConfiguration.Document,
                Placeholders = new[] { new { Name = (string?)null, Label = "Label", Kind = "Environment", Argument = "PATH" } },
                SharedConfiguration.RunAtSignIn, SharedConfiguration.ImageWidth, SharedConfiguration.ImageHeight,
                SharedConfiguration.BackgroundColor
            }));
            Assert.Throws<InvalidDataException>(() => { ConfigurationFileStore.Load(path); });
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ConfigurationPrecedenceIsConsistentForEditorAndAutomaticApply()
    {
        var directory = TestSupport.CreateTemporaryDirectory("BGInfoPlus.ConfigTests");
        try
        {
            var userPath = Path.Combine(directory, "user.json");
            var allUsersPath = Path.Combine(directory, "all-users.json");
            var defaultPath = Path.Combine(directory, "DefaultConfig.json");
            ConfigurationFileStore.Save(userPath, SharedConfiguration with { BackgroundColor = "#111111" });
            ConfigurationFileStore.SaveShared(allUsersPath, SharedConfiguration with { BackgroundColor = "#222222" });
            ConfigurationFileStore.SaveShared(defaultPath, SharedConfiguration with { BackgroundColor = "#333333" });

            var editor = ConfigurationResolver.ResolveForEditor(userPath, allUsersPath, defaultPath);
            Assert.Equal(ConfigurationScope.AllUsers, editor?.Scope);
            Assert.Equal("#222222", editor?.Configuration.BackgroundColor);
            var automatic = ConfigurationResolver.ResolveForAutomaticApply(userPath, allUsersPath, defaultPath);
            Assert.Equal(ConfigurationScope.AllUsers, automatic?.Scope);
            Assert.Equal("#222222", automatic?.Configuration.BackgroundColor);
            File.Delete(allUsersPath);
            Assert.Equal(ConfigurationScope.User,
                ConfigurationResolver.ResolveForAutomaticApply(userPath, allUsersPath, defaultPath)?.Scope);
            File.Delete(userPath);
            Assert.Equal(ConfigurationScope.Default,
                ConfigurationResolver.ResolveForAutomaticApply(userPath, allUsersPath, defaultPath)?.Scope);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void InvalidHigherPrecedencePolicyIsNotBypassed()
    {
        var directory = TestSupport.CreateTemporaryDirectory("BGInfoPlus.ConfigTests");
        try
        {
            var userPath = Path.Combine(directory, "user.json");
            var allUsersPath = Path.Combine(directory, "all-users.json");
            var defaultPath = Path.Combine(directory, "DefaultConfig.json");
            ConfigurationFileStore.Save(userPath, SharedConfiguration);
            File.WriteAllText(allUsersPath, "{broken}");
            Assert.Throws<JsonException>(() =>
            {
                ConfigurationResolver.ResolveForAutomaticApply(userPath, allUsersPath, defaultPath);
            });
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void MalformedAndOversizedConfigurationsAreRejectedBeforeRendering()
    {
        var directory = TestSupport.CreateTemporaryDirectory("BGInfoPlus.ConfigTests");
        try
        {
            var path = Path.Combine(directory, "config.json");
            File.WriteAllText(path,
                "{\"Document\":null,\"Placeholders\":[],\"ImageWidth\":1920,\"ImageHeight\":1080,\"BackgroundColor\":\"#000000\"}");
            Assert.Throws<InvalidDataException>(() => { ConfigurationFileStore.Load(path); });
            using (var oversized = new FileStream(path, FileMode.Create, FileAccess.Write))
                oversized.SetLength(AppLimits.MaxConfigurationFileBytes + 1L);
            Assert.Throws<InvalidDataException>(() => { ConfigurationFileStore.Load(path); });
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ApplicationDefaultConfigurationIsBesideExecutable()
    {
        Assert.Equal("DefaultConfig.json", Path.GetFileName(ConfigurationStore.DefaultPath));
        Assert.Equal(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory),
            Path.GetDirectoryName(ConfigurationStore.DefaultPath));
    }
}
