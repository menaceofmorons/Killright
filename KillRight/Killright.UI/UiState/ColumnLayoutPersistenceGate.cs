namespace Killright.UI.UiState;

public sealed class ColumnLayoutPersistenceGate
{
    private int _previewDepth;

    public bool ShouldPersist => _previewDepth == 0;

    public IDisposable BeginPreview()
    {
        _previewDepth++;
        return new Scope(this);
    }

    private sealed class Scope(ColumnLayoutPersistenceGate owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            owner._previewDepth--;
        }
    }
}
