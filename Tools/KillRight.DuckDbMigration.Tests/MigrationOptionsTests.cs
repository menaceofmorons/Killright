using Xunit;

namespace KillRight.DuckDbMigration.Tests;

public sealed class MigrationOptionsTests
{
    [Fact]
    public void TryParse_NoArguments_UsesTheLocalAppDataDefaults()
    {
        var parsed = MigrationOptions.TryParse([], out var options, out _);

        Assert.True(parsed);
        Assert.Equal(Path.Combine(MigrationOptions.DefaultDirectory, "KillRight.duckdb"), options!.SourcePath);
        Assert.Equal(Path.Combine(MigrationOptions.DefaultDirectory, "KillRight.db"), options.TargetPath);
    }

    [Fact]
    public void TryParse_SourceAndTarget_OverrideTheDefaults()
    {
        var parsed = MigrationOptions.TryParse(["--target", @"C:\temp\out.db", "--source", @"C:\temp\in.duckdb"], out var options, out _);

        Assert.True(parsed);
        Assert.Equal(@"C:\temp\in.duckdb", options!.SourcePath);
        Assert.Equal(@"C:\temp\out.db", options.TargetPath);
    }

    [Fact]
    public void TryParse_OneOverride_KeepsTheOtherDefault()
    {
        var parsed = MigrationOptions.TryParse(["--target", @"C:\temp\out.db"], out var options, out _);

        Assert.True(parsed);
        Assert.Equal(Path.Combine(MigrationOptions.DefaultDirectory, "KillRight.duckdb"), options!.SourcePath);
    }

    [Theory]
    [InlineData("--unknown", "x")]
    [InlineData("--source")]
    [InlineData("--target")]
    public void TryParse_UnknownOrIncompleteArgument_FailsWithUsage(params string[] args)
    {
        var parsed = MigrationOptions.TryParse(args, out var options, out var error);

        Assert.False(parsed);
        Assert.Null(options);
        Assert.Contains("Usage:", error);
    }
}
