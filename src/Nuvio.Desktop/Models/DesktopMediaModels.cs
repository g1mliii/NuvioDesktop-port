using Nuvio.Core.Models;

namespace Nuvio.Desktop.Models;

public sealed record FixtureHomeSection(string Title, IReadOnlyList<CatalogItem> Items);

public sealed record FixtureDetailState(
    MediaDetails Details,
    IReadOnlyList<StreamItem> Streams,
    IReadOnlyList<string>? FailedProviders = null);
