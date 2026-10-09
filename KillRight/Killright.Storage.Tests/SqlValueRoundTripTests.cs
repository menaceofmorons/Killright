using Killright.Shared.Data;
using Killright.Storage.Database;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Killright.Storage.Tests;

public sealed class SqlValueRoundTripTests
{
    [Fact]
    public void Bool_WritesOneAndZero()
    {
        Assert.Equal("1", SqlValueFormatter.Bool(true));
        Assert.Equal("0", SqlValueFormatter.Bool(false));
    }

    [Fact]
    public void Date_DateTimeOffset_WritesUnixSecondsAndNullAsNull()
    {
        var value = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal("1790553600", SqlValueFormatter.Date(value));
        Assert.Equal("1790553600", SqlValueFormatter.Date((DateTimeOffset?)value));
        Assert.Equal("NULL", SqlValueFormatter.Date((DateTimeOffset?)null));
    }

    [Fact]
    public void Date_DateTimeOffsetWithOffset_WritesTheSameInstant()
    {
        var utc = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
        var local = new DateTimeOffset(2026, 9, 28, 14, 0, 0, TimeSpan.FromHours(2));

        Assert.Equal(SqlValueFormatter.Date(utc), SqlValueFormatter.Date(local));
    }

    [Fact]
    public void Date_DateTime_WritesUnixSecondsForUtcAndConvertsLocal()
    {
        var utc = new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal("1790553600", SqlValueFormatter.Date(utc));
        Assert.Equal("1790553600", SqlValueFormatter.Date(utc.ToLocalTime()));
    }

    [Fact]
    public void Date_DateOnly_WritesMidnightUtc()
    {
        Assert.Equal("1790553600", SqlValueFormatter.Date((DateOnly?)new DateOnly(2026, 9, 28)));
        Assert.Equal("NULL", SqlValueFormatter.Date((DateOnly?)null));
    }

    [Fact]
    public void RoundTrip_ThroughAStrictTable_PreservesEveryTypeToTheSecond()
    {
        var database = CreateDatabase();
        var instant = new DateTimeOffset(2026, 9, 28, 13, 45, 59, 750, TimeSpan.Zero);
        var birthday = new DateOnly(2008, 3, 14);

        using var connection = database.OpenConnection();

        Execute(connection, "CREATE TABLE main.value_probe (at INTEGER, at_null INTEGER, born INTEGER, born_null INTEGER, flag INTEGER, flag_null INTEGER) STRICT;");
        Execute(connection, $"""
            INSERT INTO main.value_probe (at, at_null, born, born_null, flag, flag_null)
            VALUES ({SqlValueFormatter.Date(instant)}, {SqlValueFormatter.Date((DateTimeOffset?)null)}, {SqlValueFormatter.Date((DateOnly?)birthday)}, {SqlValueFormatter.Date((DateOnly?)null)}, {SqlValueFormatter.Bool(true)}, NULL);
            """);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT at, at_null, born, born_null, flag, flag_null FROM main.value_probe;";

        using var reader = command.ExecuteReader();

        Assert.True(reader.Read());
        Assert.Equal(new DateTimeOffset(2026, 9, 28, 13, 45, 59, TimeSpan.Zero), reader.GetUtcDateTimeOffset(0));
        Assert.Equal(new DateTimeOffset(2026, 9, 28, 13, 45, 59, TimeSpan.Zero), reader.GetNullableDateTimeOffset(0));
        Assert.Null(reader.GetNullableDateTimeOffset(1));
        Assert.Equal(birthday, reader.GetDateOnly(2));
        Assert.Equal(birthday, reader.GetNullableDateOnly(2));
        Assert.Null(reader.GetNullableDateOnly(3));
        Assert.True(reader.GetBoolean(4));
        Assert.True(reader.GetNullableBoolean(4));
        Assert.Null(reader.GetNullableBoolean(5));
    }

    [Fact]
    public void RoundTrip_FalseFlag_ReadsBackFalse()
    {
        var database = CreateDatabase();

        using var connection = database.OpenConnection();

        Execute(connection, "CREATE TABLE main.flag_probe (flag INTEGER NOT NULL CHECK (flag IN (0, 1))) STRICT;");
        Execute(connection, $"INSERT INTO main.flag_probe (flag) VALUES ({SqlValueFormatter.Bool(false)});");

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT flag FROM main.flag_probe;";

        using var reader = command.ExecuteReader();

        Assert.True(reader.Read());
        Assert.False(reader.GetBoolean(0));
    }

    private static KillRightDatabase CreateDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"values.{Guid.NewGuid():N}.db");
        var database = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = path });
        database.EnsureCreated();

        return database;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
