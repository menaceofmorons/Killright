namespace Killright.UI.Scan;

public static class BoundedConcurrentRunner
{
    public static async Task<TResult[]> RunAsync<TItem, TResult>(
        IReadOnlyList<TItem> items,
        int maxConcurrency,
        Func<TItem, int, CancellationToken, Task<TResult>> work,
        Func<TItem, Exception, TResult> onFault,
        CancellationToken cancellationToken = default)
    {
        var results = new TResult[items.Count];
        using var gate = new SemaphoreSlim(Math.Max(1, maxConcurrency));
        var tasks = new Task[items.Count];

        for (var index = 0; index < items.Count; index++)
            tasks[index] = RunOneAsync(index);

        await Task.WhenAll(tasks);

        return results;

        async Task RunOneAsync(int index)
        {
            await gate.WaitAsync(cancellationToken);

            try
            {
                results[index] = await work(items[index], index, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                results[index] = onFault(items[index], exception);
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
