using Killright.Storage.Diagnostics;
using Xunit;

namespace Killright.Storage.Tests.Diagnostics;

public sealed class ScanTimingsTests
{
    [Fact]
    public void Measure_AccumulatesOneRowPerScopeWithTagAndCharacter()
    {
        var timings = new ScanTimings { PilotCount = 2 };

        using (var scope = timings.Measure(ScanTimings.PilotLevel, "identity", null, "cache_hit"))
        {
            scope.Tag = "esi_resolve";
            scope.CharacterId = 95465499;
        }

        timings.Add(ScanTimings.PilotLevel, "identity", 5.0, 95465499);

        var rows = timings.Rows;

        Assert.Equal(2, rows.Count);
        Assert.Equal("identity", rows[0].Phase);
        Assert.Equal("esi_resolve", rows[0].Tag);
        Assert.Equal(95465499, rows[0].CharacterId);
        Assert.Equal(ScanTimings.PilotLevel, rows[0].Level);
        Assert.True(rows[0].Milliseconds >= 0);
        Assert.Equal(5.0, rows[1].Milliseconds);
    }

    [Fact]
    public void NullSession_MeasureAddAndAddEngine_AreSafeNoOps()
    {
        ScanTimings? timings = null;

        using (var scope = timings.Measure(ScanTimings.ScanLevel, "scan_total"))
        {
            scope.Tag = "ignored";
            scope.CharacterId = 1;
        }

        timings.Add(ScanTimings.ScanLevel, "x", 1.0);
        timings.AddEngine(1, new Dictionary<string, double> { ["pilot_total"] = 1.0 }, new Dictionary<string, long> { ["rows"] = 1 });

        using var second = timings.Measure(ScanTimings.ScanLevel, "other");
        Assert.Null(second.Tag);
    }

    [Fact]
    public void AddEngine_WritesPhasesAndPrefixesCounters()
    {
        var timings = new ScanTimings();

        timings.AddEngine(
            null,
            new Dictionary<string, double> { ["group_total"] = 12.5 },
            new Dictionary<string, long> { ["scanned_pilots"] = 40 });

        var rows = timings.Rows;

        Assert.Contains(rows, row => row.Level == ScanTimings.EngineLevel && row.Phase == "group_total" && row.Milliseconds == 12.5);
        Assert.Contains(rows, row => row.Phase == "count_scanned_pilots" && row.Milliseconds == 40 && row.CharacterId is null);
    }

    [Fact]
    public void FormatLine_ProducesInvariantCsvRow()
    {
        var row = new TimingRow(
            new DateTimeOffset(2026, 9, 29, 10, 11, 12, 345, TimeSpan.Zero),
            "abcd1234",
            20,
            ScanTimings.PilotLevel,
            95465499,
            "zkill_statistics",
            1234.5678,
            "live_fetch");

        Assert.Equal("2026-09-29 10:11:12.345,abcd1234,20,pilot,95465499,zkill_statistics,1234.568,live_fetch", ScanTimingLog.FormatLine(row));
    }

    [Fact]
    public void FormatLine_BlankCharacterAndTag_LeavesEmptyFields()
    {
        var row = new TimingRow(DateTimeOffset.UnixEpoch, "s", 1, ScanTimings.ScanLevel, null, "scan_total", 2, null);

        Assert.Equal("1970-01-01 00:00:00.000,s,1,scan,,scan_total,2,", ScanTimingLog.FormatLine(row));
    }

    [Fact]
    public void Flush_WritesHeaderOnceAndAppendsEveryScanRowInOneAppend()
    {
        var path = TempPath();

        try
        {
            var first = new ScanTimings();
            first.Add(ScanTimings.ScanLevel, "a", 1);
            first.Add(ScanTimings.ScanLevel, "b", 2);
            first.PilotCount = 7;
            first.Flush(path);

            var second = new ScanTimings();
            second.Add(ScanTimings.ScanLevel, "c", 3);
            second.Flush(path);

            var lines = File.ReadAllLines(path);

            Assert.Equal(ScanTimingLog.Header, lines[0]);
            Assert.Equal(4, lines.Length);
            Assert.Equal(1, lines.Count(line => line == ScanTimingLog.Header));
            Assert.Contains(",7,scan,,a,", lines[1]);
            Assert.Contains(",7,scan,,b,", lines[2]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Flush_UnwritablePath_DoesNotThrow()
    {
        var timings = new ScanTimings();
        timings.Add(ScanTimings.ScanLevel, "a", 1);

        timings.Flush(Path.Combine(Path.GetTempPath(), $"missing-directory.{Guid.NewGuid():N}", "killright-timing.csv"));
    }

    [Fact]
    public void Flush_NoRows_DoesNotCreateFile()
    {
        var path = TempPath();

        new ScanTimings().Flush(path);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void DefaultLogPath_UsesSessionFileNameInKillRightFolder()
    {
        var path = ScanTimingLogPaths.GetDefaultLogPath();

        Assert.StartsWith(ScanTimingLogPaths.FilePrefix, Path.GetFileName(path));
        Assert.EndsWith(".csv", path);
        Assert.Equal(path, ScanTimingLogPaths.GetDefaultLogPath());
    }

    private static string TempPath()
    {
        return Path.Combine(Path.GetTempPath(), $"killright-timing.{Guid.NewGuid():N}.csv");
    }
}
