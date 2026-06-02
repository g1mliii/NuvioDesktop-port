namespace Nuvio.Platform;

/// <summary>
/// Describes an ordered libmpv shared-library candidate produced by <see cref="LibMpvLibraryLocator"/>.
/// A candidate is either an absolute path (verified to exist on disk) or a bare library name that the
/// native loader is expected to resolve through the OS search path.
/// </summary>
public sealed record LibMpvCandidate(string NameOrPath, LibMpvLibrarySource Source, bool ExistsOnDisk);

/// <summary>
/// Result of locating the libmpv shared library on disk. This only reports filesystem presence and the
/// ordered candidate list to attempt; actual load success and the client API version are decided by the
/// native loader in <c>Nuvio.Player</c>, because availability ultimately depends on a working dlopen.
/// </summary>
public sealed record LibMpvDiscoveryResult(
    bool FoundOnDisk,
    string? LibraryPath,
    LibMpvLibrarySource? Source,
    IReadOnlyList<LibMpvCandidate> Candidates,
    string? Message)
{
    public string StatusLabel => FoundOnDisk ? "Found on disk" : "Not found on disk";

    public string SourceLabel => Source switch
    {
        LibMpvLibrarySource.EnvironmentOverride => "Environment override",
        LibMpvLibrarySource.AppManaged => "Nuvio-managed",
        LibMpvLibrarySource.SystemLocation => "System location",
        LibMpvLibrarySource.LoaderResolved => "Loader-resolved",
        _ => "Not detected"
    };

    public static LibMpvDiscoveryResult Found(
        string libraryPath,
        LibMpvLibrarySource source,
        IReadOnlyList<LibMpvCandidate> candidates,
        string? message = null) =>
        new(true, libraryPath, source, candidates, message);

    public static LibMpvDiscoveryResult NotFound(
        IReadOnlyList<LibMpvCandidate> candidates,
        string? message = null) =>
        new(false, null, null, candidates, message);
}
