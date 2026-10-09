using System.Data.Common;
using System.Globalization;
using DuckDB.NET.Data;
using Killright.Storage.Database;
using Microsoft.Data.Sqlite;

namespace KillRight.DuckDbMigration;

public sealed record TableReport(string Table, long SourceRows, long TargetRows, long Skipped)
{
    public bool Matches => TargetRows == SourceRows - Skipped;
}

public static class MigrationRunner
{
    public const int SupportedSourceSchemaVersion = 3;

    public static int Run(MigrationOptions options, TextWriter output)
    {
        if (!File.Exists(options.SourcePath))
            return Refuse(output, $"Source database not found: {options.SourcePath}");

        var existingTargetFile = TargetFiles(options.TargetPath).FirstOrDefault(File.Exists);

        if (existingTargetFile is not null)
            return Refuse(output, $"Target already exists: {existingTargetFile}");

        var targetStarted = false;
        var succeeded = false;
        KillRightDatabase? target = null;

        try
        {
            using var source = OpenSource(options.SourcePath);

            var sourceVersion = ReadSourceSchemaVersion(source);

            if (sourceVersion != SupportedSourceSchemaVersion)
                return Refuse(output, $"Source schema_version is {sourceVersion?.ToString(CultureInfo.InvariantCulture) ?? "unreadable"}; {SupportedSourceSchemaVersion} is required.");

            targetStarted = true;
            target = new KillRightDatabase(new KillRightDatabaseOptions { DatabasePath = options.TargetPath });
            target.EnsureCreated();
            target.Open();

            var copied = new List<(TableSpec Table, long SourceRows, long Skipped)>();

            using (var scope = target.BeginWrite())
            {
                foreach (var table in TableSpecs.All)
                {
                    var (sourceRows, skipped) = CopyTable(source, scope, table);
                    copied.Add((table, sourceRows, skipped));
                }

                var (threshold, alphaLock) = CopyMetadata(source, scope);
                output.WriteLine($"metadata: last_qualification_fleet_threshold={threshold?.ToString(CultureInfo.InvariantCulture) ?? "NULL"} alpha_lock={alphaLock}");

                scope.Commit();
            }

            var reports = new List<TableReport>();

            using (var connection = target.OpenConnection())
            {
                foreach (var (table, sourceRows, skipped) in copied)
                    reports.Add(new TableReport(table.Name, sourceRows, ScalarLong(connection, $"SELECT COUNT(*) FROM main.{table.Name};"), skipped));
            }

            WriteReport(output, reports);

            if (reports.Any(report => !report.Matches))
            {
                output.WriteLine("Row counts do not match. The target was removed.");
                return 1;
            }

            target.Checkpoint();
            target.Close();
            succeeded = true;

            output.WriteLine($"Migration complete: {options.TargetPath}");

            return 0;
        }
        catch (Exception exception)
        {
            output.WriteLine($"Migration failed: {exception.Message}");
            return 1;
        }
        finally
        {
            if (targetStarted && !succeeded)
                RemoveTarget(options.TargetPath, target, output);
        }
    }

    private static int Refuse(TextWriter output, string message)
    {
        output.WriteLine(message);

        return 1;
    }

    private static IEnumerable<string> TargetFiles(string targetPath)
    {
        yield return targetPath;
        yield return targetPath + "-wal";
        yield return targetPath + "-shm";
    }

    private static void RemoveTarget(string targetPath, KillRightDatabase? target, TextWriter output)
    {
        try
        {
            target?.Close();

            foreach (var path in TargetFiles(targetPath))
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
        catch (Exception exception)
        {
            output.WriteLine($"The partial target could not be removed: {exception.Message}");
        }
    }

    private static DuckDBConnection OpenSource(string sourcePath)
    {
        var connection = new DuckDBConnection($"Data Source={sourcePath};access_mode=READ_ONLY");

        try
        {
            connection.Open();
        }
        catch
        {
            connection.Dispose();
            throw;
        }

        return connection;
    }

    private static int? ReadSourceSchemaVersion(DuckDBConnection source)
    {
        try
        {
            using var command = source.CreateCommand();
            command.CommandText = "SELECT CAST(schema_version AS BIGINT) FROM main.schema_metadata LIMIT 1;";

            var value = command.ExecuteScalar();

            return value is null or DBNull ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch (DuckDBException)
        {
            return null;
        }
    }

    private static (long SourceRows, long Skipped) CopyTable(DuckDBConnection source, WriteScope scope, TableSpec table)
    {
        const string OrphanPredicate = "FROM main.zkill_killmails k WHERE k.killmail_id = t.killmail_id";

        var sourceRows = ScalarLong(source, $"SELECT COUNT(*) FROM main.{table.Name};");

        var skipped = table.SkipOrphanAttackers
            ? ScalarLong(source, $"SELECT COUNT(*) FROM main.{table.Name} t WHERE NOT EXISTS (SELECT 1 {OrphanPredicate});")
            : 0;

        var selectList = string.Join(", ", table.Columns.Select(SelectExpression));
        var filter = table.SkipOrphanAttackers ? $" WHERE EXISTS (SELECT 1 {OrphanPredicate})" : string.Empty;

        using var read = source.CreateCommand();
        read.CommandText = $"SELECT {selectList} FROM main.{table.Name} t{filter};";

        using var insert = scope.Connection.CreateCommand();
        insert.Transaction = scope.Transaction;
        insert.CommandText = $"INSERT INTO main.{table.Name} ({string.Join(", ", table.Columns.Select(column => column.Name))}) VALUES ({string.Join(", ", table.Columns.Select((_, index) => $"$p{index}"))});";

        var parameters = new SqliteParameter[table.Columns.Count];

        for (var index = 0; index < parameters.Length; index++)
        {
            parameters[index] = insert.CreateParameter();
            parameters[index].ParameterName = $"$p{index}";
            insert.Parameters.Add(parameters[index]);
        }

        using var reader = read.ExecuteReader();

        while (reader.Read())
        {
            for (var index = 0; index < parameters.Length; index++)
                parameters[index].Value = ToTargetValue(reader.GetValue(index), table.Columns[index].Kind);

            insert.ExecuteNonQuery();
        }

        return (sourceRows, skipped);
    }

    private static (long? Threshold, long AlphaLock) CopyMetadata(DuckDBConnection source, WriteScope scope)
    {
        long? threshold;
        long alphaLock;

        using (var read = source.CreateCommand())
        {
            read.CommandText = "SELECT CAST(last_qualification_fleet_threshold AS BIGINT), CAST(alpha_lock AS BIGINT) FROM main.schema_metadata LIMIT 1;";

            using var reader = read.ExecuteReader();

            if (!reader.Read())
                throw new InvalidOperationException("The source schema_metadata table has no row.");

            threshold = reader.IsDBNull(0) ? null : Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture);
            alphaLock = reader.IsDBNull(1) ? 0 : Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture);
        }

        using var update = scope.Connection.CreateCommand();
        update.Transaction = scope.Transaction;
        update.CommandText = "UPDATE main.schema_metadata SET last_qualification_fleet_threshold = $threshold, alpha_lock = $alphaLock;";
        update.Parameters.AddWithValue("$threshold", threshold.HasValue ? threshold.Value : DBNull.Value);
        update.Parameters.AddWithValue("$alphaLock", alphaLock);
        update.ExecuteNonQuery();

        return (threshold, alphaLock);
    }

    private static string SelectExpression(ColumnSpec column)
    {
        return column.Kind switch
        {
            ColumnKind.Integer or ColumnKind.Flag => $"CAST(t.{column.Name} AS BIGINT)",
            ColumnKind.Real => $"CAST(t.{column.Name} AS DOUBLE)",
            ColumnKind.Time => $"CAST(t.{column.Name} AS VARCHAR)",
            _ => $"t.{column.Name}"
        };
    }

    private static object ToTargetValue(object value, ColumnKind kind)
    {
        if (value is DBNull)
            return DBNull.Value;

        return kind switch
        {
            ColumnKind.Integer or ColumnKind.Flag => Convert.ToInt64(value, CultureInfo.InvariantCulture),
            ColumnKind.Real => Convert.ToDouble(value, CultureInfo.InvariantCulture),
            ColumnKind.Time => ParseUnixSeconds(Convert.ToString(value, CultureInfo.InvariantCulture)!),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)!
        };
    }

    private static long ParseUnixSeconds(string text)
    {
        return DateTimeOffset
            .Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
            .ToUnixTimeSeconds();
    }

    private static long ScalarLong(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void WriteReport(TextWriter output, IReadOnlyList<TableReport> reports)
    {
        var width = reports.Max(report => report.Table.Length);

        output.WriteLine($"{"table".PadRight(width)}  {"source",12}  {"target",12}  {"skipped",8}  match");

        foreach (var report in reports)
            output.WriteLine($"{report.Table.PadRight(width)}  {report.SourceRows,12}  {report.TargetRows,12}  {report.Skipped,8}  {(report.Matches ? "yes" : "NO")}");
    }
}
