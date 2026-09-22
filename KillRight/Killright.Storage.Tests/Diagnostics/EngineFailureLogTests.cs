using Killright.Storage.Diagnostics;
using Xunit;

namespace Killright.Storage.Tests.Diagnostics;

public sealed class EngineFailureLogTests
{
    [Fact]
    public void FormatLine_ProducesTimestampedMessage()
    {
        var utcNow = new DateTimeOffset(2026, 9, 22, 13, 45, 0, TimeSpan.Zero);

        var line = EngineFailureLog.FormatLine(utcNow, "engine analysis failed for character 123: repository_read_error");

        Assert.Equal("2026-09-22 13:45:00 UTC | engine analysis failed for character 123: repository_read_error", line);
    }

    [Fact]
    public void Record_AppendsLineToFile()
    {
        var path = TempLogPath();

        try
        {
            EngineFailureLog.Record(path, "first failure", DateTimeOffset.UtcNow);
            EngineFailureLog.Record(path, "second failure", DateTimeOffset.UtcNow);

            var lines = File.ReadAllLines(path);

            Assert.Equal(2, lines.Length);
            Assert.Contains("first failure", lines[0]);
            Assert.Contains("second failure", lines[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Record_FileExceedsMaximumSize_RestartsFileInsteadOfGrowingForever()
    {
        var path = TempLogPath();

        try
        {
            File.WriteAllText(path, new string('x', (int)EngineFailureLog.MaximumSizeBytes + 1));

            EngineFailureLog.Record(path, "after size cap", DateTimeOffset.UtcNow);

            var lines = File.ReadAllLines(path);

            Assert.Single(lines);
            Assert.Contains("after size cap", lines[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Record_UnwritablePath_DoesNotThrow()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-directory.{Guid.NewGuid():N}", "killright-engine.log");

        EngineFailureLog.Record(path, "should not throw", DateTimeOffset.UtcNow);
    }

    private static string TempLogPath()
    {
        return Path.Combine(Path.GetTempPath(), $"killright-engine.{Guid.NewGuid():N}.log");
    }
}
