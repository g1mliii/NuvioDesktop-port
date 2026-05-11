namespace Nuvio.Core.Models;

public sealed record AddonManifest(
    string Id,
    string Name,
    string Description,
    string Version,
    Uri? LogoUrl,
    IReadOnlyList<AddonResource> Resources,
    IReadOnlyList<string> Types,
    IReadOnlyList<string> IdPrefixes,
    IReadOnlyList<AddonCatalog> Catalogs,
    AddonBehaviorHints BehaviorHints,
    Uri TransportUrl);

public sealed record AddonResource(
    string Name,
    IReadOnlyList<string> Types,
    IReadOnlyList<string> IdPrefixes);

public sealed record AddonCatalog(
    string Type,
    string Id,
    string Name,
    IReadOnlyList<AddonExtraProperty> Extra);

public sealed record AddonExtraProperty(
    string Name,
    bool IsRequired,
    IReadOnlyList<string> Options,
    int? OptionsLimit);

public sealed record AddonBehaviorHints(
    bool Configurable = false,
    bool ConfigurationRequired = false,
    bool Adult = false,
    bool P2p = false);
