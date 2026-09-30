using DuckDB.NET.Data;

namespace Killright.Storage.Database;

public sealed class ScanDatabaseSession : IDisposable
{
    private readonly DuckDBConnection _connection;

    internal ScanDatabaseSession(DuckDBConnection connection)
    {
        _connection = connection;
    }

    public DuckDBConnection Connection => _connection;

    public void Dispose()
    {
        _connection.Dispose();
    }
}
