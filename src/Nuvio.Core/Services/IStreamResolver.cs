using Nuvio.Core.Models;

namespace Nuvio.Core.Services;

public sealed record ResolvedStreamGroup(
    string AddonId,
    string AddonName,
    IReadOnlyList<StreamSource> Streams,
    int FilteredCount,
    string? Error);

public interface IStreamResolver
{
    Task<IReadOnlyList<ResolvedStreamGroup>> ResolveAsync(string type, string id, CancellationToken cancellationToken);
}
