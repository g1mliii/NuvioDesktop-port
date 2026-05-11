namespace Nuvio.Desktop.Tests;

public sealed class ComplianceFilesTests
{
    [Theory]
    [InlineData("LICENSE")]
    [InlineData("NOTICE")]
    [InlineData("docs/legal-compliance.md")]
    [InlineData("docs/dependency-licenses.md")]
    [InlineData("docs/mpv-setup.md")]
    public void RequiredComplianceFilesExist(string relativePath)
    {
        var root = FindRepositoryRoot();

        Assert.True(File.Exists(Path.Combine(root, relativePath)), $"{relativePath} is required for release compliance.");
    }

    [Fact]
    public void MpvSetupDocs_StateManagerPolicy()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "docs/mpv-setup.md"));

        Assert.Contains("MPV Manager", text);
        Assert.Contains("optional setup helper", text);
        Assert.Contains("not a runtime dependency", text);
    }

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
