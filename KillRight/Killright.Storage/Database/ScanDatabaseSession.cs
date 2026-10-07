using DuckDB.NET.Data;

namespace Killright.Storage.Database;

public sealed class ScanDatabaseSession : IDisposable
{
    private readonly DuckDBConnection _connection;
    private readonly Action<Exception>? _failureHandler;

    internal ScanDatabaseSession(DuckDBConnection connection, Action<Exception>? failureHandler = null)
    {
        _connection = connection;
        _failureHandler = failureHandler;
    }

    public DuckDBConnection Connection => _connection;

    internal void ReportFailure(Exception exception)
    {
        _failureHandler?.Invoke(exception);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
