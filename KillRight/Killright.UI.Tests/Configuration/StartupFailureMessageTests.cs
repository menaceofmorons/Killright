using Killright.UI.Configuration;
using Xunit;

namespace Killright.UI.Tests.Configuration;

public sealed class StartupFailureMessageTests
{
    [Fact]
    public void ReasonFor_MissingFile_IsUnreadable()
    {
        var result = ApplicationSettingsLoader.Load(@"C:\does-not-exist\settings.json");

        Assert.Equal(StartupFailureReason.SettingsFileUnreadable, StartupFailureMessage.ReasonFor(result));
    }

    [Fact]
    public void ReasonFor_UnreadableFile_IsUnreadable()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{}");

        try
        {
            using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

            var result = ApplicationSettingsLoader.Load(path);

            Assert.Equal(StartupFailureReason.SettingsFileUnreadable, StartupFailureMessage.ReasonFor(result));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("{ \"recentWindowDays\": \"many\" }")]
    public void ReasonFor_InvalidContent_IsInvalid(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);

        try
        {
            var result = ApplicationSettingsLoader.Load(path);

            Assert.Equal(StartupFailureReason.SettingsInvalid, StartupFailureMessage.ReasonFor(result));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReasonFor_InvalidValue_IsInvalid()
    {
        var result = SettingsLoadResult.Failed("x");

        Assert.Equal(StartupFailureReason.SettingsInvalid, StartupFailureMessage.ReasonFor(result));
    }

    [Fact]
    public void Title_IsFixed()
    {
        Assert.Equal("KillRight cannot start", StartupFailureMessage.Title);
    }

    [Theory]
    [InlineData(StartupFailureReason.SettingsFileUnreadable, "The settings file is missing or could not be read.")]
    [InlineData(StartupFailureReason.SettingsInvalid, "The settings file is not valid.")]
    [InlineData(StartupFailureReason.EngineSettingsRejected, "The engine could not load the settings.")]
    public void TextFor_ReasonThenLogPointer(StartupFailureReason reason, string expectedReason)
    {
        Assert.Equal($"{expectedReason} See the log file for details.", StartupFailureMessage.TextFor(reason));
    }
}
