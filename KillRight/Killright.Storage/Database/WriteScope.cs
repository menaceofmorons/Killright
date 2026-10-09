using Microsoft.Data.Sqlite;

namespace Killright.Storage.Database;

public sealed class WriteScope : IDisposable
{
    private readonly SqliteConnection? _ownedConnection;
    private readonly SemaphoreSlim _gate;
    private bool _committed;
    private bool _disposed;

    internal WriteScope(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SqliteConnection? ownedConnection,
        SemaphoreSlim gate)
    {
        Connection = connection;
        Transaction = transaction;
        _ownedConnection = ownedConnection;
        _gate = gate;
    }

    public SqliteConnection Connection { get; }

    public SqliteTransaction Transaction { get; }

    public void Commit()
    {
        Transaction.Commit();
        _committed = true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            if (!_committed)
                Transaction.Rollback();
        }
        finally
        {
            Transaction.Dispose();
            _ownedConnection?.Dispose();
            _gate.Release();
        }
    }
}
