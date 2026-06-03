namespace Nuvio.Desktop.Tests.Compliance;

/// <summary>
/// Phase 8 packaging/compliance assertions. These guard the release artifact contract:
/// the legal/compliance file set, the GPL source offer, and the native-dependency
/// provenance documentation. The artifact-contents check is opt-in via NUVIO_ARTIFACT_DIR
/// so the default suite stays green without a built artifact on disk.
/// </summary>
public sealed class Phase8PackagingTests
{
    // Single source of truth for what a packaged artifact must contain at its root.
    // Mirrors scripts/lib/stage-compliance.{sh,ps1} (which stages these into artifacts)
    // and the release.yml artifact-contents gate.
    public static readonly string[] RequiredArtifactFiles =
    {
        "LICENSE",
        "NOTICE",
        "dependency-licenses.md",
        "source-availability.md",
        "native-dependency-provenance.md",
    };

    [Fact]
    public void DependencyLicenses_HasNoUnresolvedPlaceholder()
    {
        var text = ReadRepoFile("docs/dependency-licenses.md");
        Assert.DoesNotContain("TBD", text);
    }

    [Fact]
    public void SourceAvailability_NamesRepoTagAndContact()
    {
        var text = ReadRepoFile("docs/source-availability.md");

        Assert.Contains("github.com/g1mliii/nuviodesktop-port", text);
        Assert.Contains("tag", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("v0.8.0", text); // example release tag matching the default version
        Assert.Contains("info@anchored.site", text);
    }

    [Fact]
    public void NativeProvenance_DocumentsNotBundledAndDropInFolders()
    {
        var text = ReadRepoFile("docs/native-dependency-provenance.md");

        Assert.Contains("not bundled", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NUVIO_MPV_PATH", text);
        Assert.Contains("NUVIO_LIBMPV_PATH", text);
        Assert.Contains("runtimes/", text);   // runtimes/<rid>/native drop-in
        Assert.Contains("./mpv/", text);       // app-managed ./mpv/ drop-in
    }

    [Fact]
    public void DirectoryBuildProps_DerivesAssemblyAndFileVersionsFromReleaseVersion()
    {
        var text = ReadRepoFile("Directory.Build.props");

        Assert.Contains("VersionNumericPrefix", text);
        Assert.Contains("Regex]::Match('$(Version)'", text);
        Assert.Contains("<AssemblyVersion>$(VersionNumericPrefix).0</AssemblyVersion>", text);
        Assert.Contains("<FileVersion>$(VersionNumericPrefix).0</FileVersion>", text);
        Assert.Contains("<InformationalVersion>$(Version)</InformationalVersion>", text);
    }

    [Fact]
    public void ComplianceStagingScript_ListsExactlyTheRequiredArtifactFiles()
    {
        var script = ReadRepoFile("scripts/lib/stage-compliance.sh");

        // The staging script must reference each required file so the artifact-contents
        // expectation and the stager cannot silently drift apart.
        var expectedSources = new[]
        {
            "LICENSE",
            "NOTICE",
            "docs/dependency-licenses.md",
            "docs/source-availability.md",
            "docs/native-dependency-provenance.md",
        };
        foreach (var rel in expectedSources)
        {
            Assert.Contains(rel, script);
        }
    }

    [Fact]
    public void ArtifactContents_WhenArtifactDirSet_ContainsRequiredFiles()
    {
        var artifactDir = Environment.GetEnvironmentVariable("NUVIO_ARTIFACT_DIR");
        if (string.IsNullOrWhiteSpace(artifactDir))
        {
            // No-op by design: keeps the default suite green when no artifact is staged.
            return;
        }

        // A relative NUVIO_ARTIFACT_DIR (e.g. the release.yml gate's "artifacts/publish/linux-x64")
        // must resolve against the repo root, not the test host's working directory — the latter
        // is not guaranteed to be the repo root, which would make this gate non-deterministic.
        if (!Path.IsPathRooted(artifactDir))
        {
            artifactDir = Path.Combine(FindRepositoryRoot(), artifactDir);
        }

        Assert.True(Directory.Exists(artifactDir), $"NUVIO_ARTIFACT_DIR does not exist: {artifactDir}");
        foreach (var file in RequiredArtifactFiles)
        {
            var path = Path.Combine(artifactDir, file);
            Assert.True(File.Exists(path), $"Artifact is missing required file: {file}");
        }
    }

    private static string ReadRepoFile(string relativePath) =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NuvioDesktop.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }
}
