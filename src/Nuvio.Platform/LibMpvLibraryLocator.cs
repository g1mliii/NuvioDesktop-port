namespace Nuvio.Platform;

/// <summary>
/// Discovers the embedded <c>libmpv</c> shared library for the embedded player engine. Mirrors
/// <see cref="MpvProcessLocator"/> (which finds the mpv executable) but targets the dynamic library and
/// produces an ordered candidate list for the native loader. Discovery order:
/// <list type="number">
/// <item><c>NUVIO_LIBMPV_PATH</c> environment override.</item>
/// <item>Nuvio-managed paths next to the app and under <c>runtimes/&lt;rid&gt;/native</c>.</item>
/// <item>Common per-OS system library locations.</item>
/// <item>Bare library names left for the OS loader to resolve.</item>
/// </list>
/// No libmpv binaries are bundled in this phase; bundling is deferred to packaging (Phase 8).
/// </summary>
public sealed class LibMpvLibraryLocator
{
    private readonly LibMpvLocatorEnvironment _environment;

    public LibMpvLibraryLocator()
        : this(LibMpvLocatorEnvironment.Current())
    {
    }

    public LibMpvLibraryLocator(LibMpvLocatorEnvironment environment)
    {
        _environment = environment;
    }

    /// <summary>
    /// Bare library names for the current platform, most-preferred first. Exposed for the native loader's
    /// by-name fallback and for filename-selection regression tests.
    /// </summary>
    public IReadOnlyList<string> CandidateLibraryNames() => _environment.PlatformFamily switch
    {
        PlatformFamily.Windows => ["libmpv-2.dll", "mpv-2.dll", "libmpv.dll", "mpv-1.dll"],
        PlatformFamily.MacOS => ["libmpv.2.dylib", "libmpv.dylib"],
        PlatformFamily.Linux => ["libmpv.so.2", "libmpv.so.1", "libmpv.so"],
        _ => []
    };

    public LibMpvDiscoveryResult Locate()
    {
        var candidates = BuildCandidates().ToList();

        var firstOnDisk = candidates.FirstOrDefault(candidate => candidate.ExistsOnDisk);
        if (firstOnDisk is not null)
        {
            return LibMpvDiscoveryResult.Found(firstOnDisk.NameOrPath, firstOnDisk.Source, candidates);
        }

        if (candidates.Count == 0)
        {
            return LibMpvDiscoveryResult.NotFound(
                candidates,
                "Embedded libmpv is not supported on this platform.");
        }

        var explicitOverride = Normalize(_environment.EnvironmentOverridePath);
        if (!string.IsNullOrWhiteSpace(explicitOverride))
        {
            return LibMpvDiscoveryResult.NotFound(
                candidates,
                "NUVIO_LIBMPV_PATH is set, but it does not point to an existing file. The native loader will still try the OS library search path.");
        }

        return LibMpvDiscoveryResult.NotFound(
            candidates,
            "No libmpv file was found next to the app or in common locations. The native loader will try the OS library search path; install libmpv or set NUVIO_LIBMPV_PATH if embedded playback is unavailable.");
    }

    private IEnumerable<LibMpvCandidate> BuildCandidates()
    {
        var names = CandidateLibraryNames();
        if (names.Count == 0)
        {
            yield break;
        }

        var explicitOverride = Normalize(_environment.EnvironmentOverridePath);
        if (!string.IsNullOrWhiteSpace(explicitOverride))
        {
            yield return new LibMpvCandidate(
                explicitOverride,
                LibMpvLibrarySource.EnvironmentOverride,
                _environment.FileExists(explicitOverride));
        }

        foreach (var directory in AppManagedDirectories())
        {
            foreach (var name in names)
            {
                var path = Combine(directory, name);
                yield return new LibMpvCandidate(path, LibMpvLibrarySource.AppManaged, _environment.FileExists(path));
            }
        }

        foreach (var directory in SystemDirectories())
        {
            foreach (var name in names)
            {
                var path = Combine(directory, name);
                yield return new LibMpvCandidate(path, LibMpvLibrarySource.SystemLocation, _environment.FileExists(path));
            }
        }

        // Bare names: let the OS loader resolve through its own search path (PATH, ld.so cache, @rpath).
        foreach (var name in names)
        {
            yield return new LibMpvCandidate(name, LibMpvLibrarySource.LoaderResolved, ExistsOnDisk: false);
        }
    }

    private IEnumerable<string> AppManagedDirectories()
    {
        var root = _environment.AppBaseDirectory;
        yield return root;
        yield return Path.Combine(root, "mpv");
        yield return Path.Combine(root, "tools", "mpv");

        if (!string.IsNullOrWhiteSpace(_environment.RuntimeIdentifier))
        {
            yield return Path.Combine(root, "runtimes", _environment.RuntimeIdentifier, "native");
        }
    }

    private IEnumerable<string> SystemDirectories()
    {
        switch (_environment.PlatformFamily)
        {
            case PlatformFamily.Windows:
                yield return @"C:\Program Files\mpv";
                yield return @"C:\mpv";
                foreach (var profileRoot in NonEmpty(_environment.LocalApplicationDataDirectory, _environment.UserProfileDirectory))
                {
                    yield return Path.Combine(profileRoot, "Programs", "mpv");
                    yield return Path.Combine(profileRoot, "scoop", "apps", "mpv", "current");
                }

                break;

            case PlatformFamily.MacOS:
                yield return "/opt/homebrew/lib";
                yield return "/usr/local/lib";
                yield return "/Applications/mpv.app/Contents/Frameworks";
                break;

            case PlatformFamily.Linux:
                yield return "/usr/lib";
                yield return "/usr/lib/x86_64-linux-gnu";
                yield return "/usr/lib/aarch64-linux-gnu";
                yield return "/usr/local/lib";
                yield return "/lib";
                yield return "/lib/x86_64-linux-gnu";
                break;
        }
    }

    // Use the target platform's separator so candidate paths stay valid even when discovery runs on a
    // different host OS (e.g. cross-platform unit tests on Windows verifying Linux/macOS layouts).
    private string Combine(string directory, string name)
    {
        var separator = _environment.PlatformFamily == PlatformFamily.Windows ? '\\' : '/';
        return $"{directory.TrimEnd('/', '\\')}{separator}{name}";
    }

    private static IEnumerable<string> NonEmpty(params string?[] values) =>
        values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!);

    private static string Normalize(string? path) =>
        string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().Trim('"');
}
