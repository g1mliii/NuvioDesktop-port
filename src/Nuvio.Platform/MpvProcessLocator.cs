namespace Nuvio.Platform;

public sealed class MpvProcessLocator
{
    private readonly MpvProcessLocatorEnvironment _environment;

    public MpvProcessLocator()
        : this(MpvProcessLocatorEnvironment.Current())
    {
    }

    public MpvProcessLocator(MpvProcessLocatorEnvironment environment)
    {
        _environment = environment;
    }

    public MpvDiscoveryResult Locate()
    {
        var explicitPath = Normalize(_environment.EnvironmentOverridePath);
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            if (IsRealMpvExecutable(explicitPath) && _environment.FileExists(explicitPath))
            {
                return Found(explicitPath, MpvInstallationSource.EnvironmentOverride);
            }

            return MpvDiscoveryResult.NotFound("NUVIO_MPV_PATH is set, but it does not point to a real mpv executable.");
        }

        foreach (var candidate in EnumerateCandidates())
        {
            if (_environment.FileExists(candidate.Path) && IsRealMpvExecutable(candidate.Path))
            {
                return Found(candidate.Path, candidate.Source);
            }
        }

        return MpvDiscoveryResult.NotFound("mpv was not found. Install mpv directly or use MPV Manager as an optional setup helper, then point NUVIO_MPV_PATH at the mpv binary if needed.");
    }

    private MpvDiscoveryResult Found(string executablePath, MpvInstallationSource source) =>
        MpvDiscoveryResult.Found(executablePath, source, _environment.GetVersion(executablePath));

    private IEnumerable<MpvCandidate> EnumerateCandidates()
    {
        foreach (var candidate in AppManagedCandidates())
        {
            yield return candidate;
        }

        foreach (var candidate in MpvManagerCandidates())
        {
            yield return candidate;
        }

        foreach (var candidate in PathCandidates())
        {
            yield return candidate;
        }

        foreach (var candidate in CommonLocationCandidates())
        {
            yield return candidate;
        }
    }

    private IEnumerable<MpvCandidate> AppManagedCandidates()
    {
        var executableName = ExecutableName();
        var root = _environment.AppBaseDirectory;

        yield return new MpvCandidate(Join(root, "mpv", executableName), MpvInstallationSource.AppManaged);
        yield return new MpvCandidate(Join(root, "tools", "mpv", executableName), MpvInstallationSource.AppManaged);
        yield return new MpvCandidate(Join(root, "runtimes", RuntimeFolderName(), "native", executableName), MpvInstallationSource.AppManaged);
    }

    private IEnumerable<MpvCandidate> MpvManagerCandidates()
    {
        foreach (var root in MpvManagerRoots())
        {
            yield return new MpvCandidate(Join(root, "mpv", ExecutableName()), MpvInstallationSource.MpvManager);
            yield return new MpvCandidate(Join(root, "bin", ExecutableName()), MpvInstallationSource.MpvManager);
            yield return new MpvCandidate(Join(root, "current", ExecutableName()), MpvInstallationSource.MpvManager);

            foreach (var path in _environment.EnumerateFiles(root, ExecutableName()))
            {
                yield return new MpvCandidate(path, MpvInstallationSource.MpvManager);
            }
        }
    }

    private IEnumerable<string> MpvManagerRoots()
    {
        foreach (var root in NonEmpty(
            _environment.LocalApplicationDataDirectory,
            _environment.ApplicationDataDirectory,
            _environment.UserProfileDirectory))
        {
            yield return Join(root, "mpv-manager");
            yield return Join(root, "MPV Manager");
            yield return Join(root, ".mpv-manager");
        }
    }

    private IEnumerable<MpvCandidate> PathCandidates()
    {
        if (string.IsNullOrWhiteSpace(_environment.PathVariable))
        {
            yield break;
        }

        var separator = _environment.PlatformFamily == PlatformFamily.Windows ? ';' : ':';
        foreach (var directory in _environment.PathVariable.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return new MpvCandidate(Join(directory, ExecutableName()), MpvInstallationSource.Path);
        }
    }

    private IEnumerable<MpvCandidate> CommonLocationCandidates()
    {
        if (_environment.PlatformFamily == PlatformFamily.Windows)
        {
            foreach (var path in WindowsCommonPaths())
            {
                yield return new MpvCandidate(path, MpvInstallationSource.CommonLocation);
            }

            yield break;
        }

        if (_environment.PlatformFamily == PlatformFamily.MacOS)
        {
            foreach (var path in new[]
            {
                "/opt/homebrew/bin/mpv",
                "/usr/local/bin/mpv",
                "/Applications/mpv.app/Contents/MacOS/mpv"
            })
            {
                yield return new MpvCandidate(path, MpvInstallationSource.CommonLocation);
            }

            yield break;
        }

        if (_environment.PlatformFamily == PlatformFamily.Linux)
        {
            foreach (var path in new[]
            {
                "/usr/bin/mpv",
                "/usr/local/bin/mpv",
                "/snap/bin/mpv",
                "/app/bin/mpv"
            })
            {
                yield return new MpvCandidate(path, MpvInstallationSource.CommonLocation);
            }
        }
    }

    private IEnumerable<string> WindowsCommonPaths()
    {
        yield return @"C:\Program Files\mpv\mpv.exe";
        yield return @"C:\Program Files (x86)\mpv\mpv.exe";
        yield return @"C:\mpv\mpv.exe";

        foreach (var root in NonEmpty(_environment.LocalApplicationDataDirectory, _environment.UserProfileDirectory))
        {
            yield return Join(root, "Programs", "mpv", "mpv.exe");
            yield return Join(root, "scoop", "apps", "mpv", "current", "mpv.exe");
        }

        foreach (var root in NonEmpty(_environment.ProgramDataDirectory))
        {
            yield return Join(root, "chocolatey", "bin", "mpv.exe");

            var chocolateyToolsRoot = Join(root, "chocolatey", "lib", "mpvio.install", "tools");
            yield return Join(chocolateyToolsRoot, "mpv.exe");

            foreach (var path in _environment.EnumerateFiles(chocolateyToolsRoot, "mpv.exe"))
            {
                yield return path;
            }
        }
    }

    private string ExecutableName() => _environment.PlatformFamily == PlatformFamily.Windows ? "mpv.exe" : "mpv";

    private string RuntimeFolderName() => _environment.PlatformFamily switch
    {
        PlatformFamily.Windows => "win",
        PlatformFamily.MacOS => "osx",
        PlatformFamily.Linux => "linux",
        _ => "unknown"
    };

    private bool IsRealMpvExecutable(string path)
    {
        var fileName = FileName(path);
        return _environment.PlatformFamily == PlatformFamily.Windows
            ? string.Equals(fileName, "mpv.exe", StringComparison.OrdinalIgnoreCase)
            : string.Equals(fileName, "mpv", StringComparison.Ordinal);
    }

    private static IEnumerable<string> NonEmpty(params string?[] values) =>
        values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!);

    private static string Normalize(string? path) =>
        string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().Trim('"');

    private static string Join(params string[] parts) => Path.Combine(parts);

    private static string FileName(string path)
    {
        var normalized = path.Replace('\\', '/');
        var index = normalized.LastIndexOf('/');
        return index >= 0 ? normalized[(index + 1)..] : normalized;
    }

    private sealed record MpvCandidate(string Path, MpvInstallationSource Source);
}
