namespace Nuvio.Core.Services;

internal static class AddonFanOut
{
    public const int DefaultMaxConcurrentRequests = 8;

    public static async Task<TResult[]> WhenAllAsync<TSource, TResult>(
        IReadOnlyList<TSource> sources,
        Func<TSource, CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken,
        int maxConcurrency = DefaultMaxConcurrentRequests)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(operation);
        if (maxConcurrency < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
        }

        if (sources.Count == 0)
        {
            return [];
        }

        var results = new TResult[sources.Count];
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Min(maxConcurrency, sources.Count),
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(
                Enumerable.Range(0, sources.Count),
                options,
                async (index, token) =>
                {
                    results[index] = await operation(sources[index], token).ConfigureAwait(false);
                })
            .ConfigureAwait(false);

        return results;
    }
}
