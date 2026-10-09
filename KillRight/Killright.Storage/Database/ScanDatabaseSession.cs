using Killright.Storage.Diagnostics;
using Microsoft.Data.Sqlite;

namespace Killright.Storage.Database;

public sealed class ScanDatabaseSession : IDisposable
{
    private readonly KillRightDatabase _database;
    private readonly SqliteConnection _connection;

    internal ScanDatabaseSession(KillRightDatabase database, SqliteConnection connection)
    {
        _database = database;
        _connection = connection;
    }

    public SqliteConnection Connection => _connection;

    internal WriteScope BeginWrite(ScanTimings? timings = null, string? tag = null)
    {
        return _database.BeginWrite(_connection, timings, tag);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
