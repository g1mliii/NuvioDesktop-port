using Nuvio.Core.Addons;
using Nuvio.Core.Validation;

namespace Nuvio.Core.Tests;

public sealed class AddonManifestParserTests
{
    [Fact]
    public void Parse_ValidManifestFixture_MapsStableDto()
    {
        var manifest = AddonManifestParser.Parse(
            "https://addons.example.test/fixture/manifest.json?token=fixture",
            TestData.ReadFixture("addon-valid.json"));

        Assert.Equal("org.nuvio.fixture", manifest.Id);
        Assert.Equal("Fixture Addon", manifest.Name);
        Assert.Equal("1.2.3", manifest.Version);
        Assert.Equal("https://addons.example.test/assets/logo.png", manifest.LogoUrl?.ToString());
        Assert.Equal("https://addons.example.test/fixture/manifest.json?token=fixture", manifest.TransportUrl.ToString());
        Assert.Equal(2, manifest.Resources.Count);
        Assert.Equal("stream", manifest.Resources[0].Name);
        Assert.Equal("movie", Assert.Single(manifest.Resources[0].Types));
        Assert.Equal("meta", manifest.Resources[1].Name);
        Assert.Equal(new[] { "movie", "series" }, manifest.Resources[1].Types);
        Assert.True(manifest.BehaviorHints.Configurable);

        var catalog = Assert.Single(manifest.Catalogs);
        Assert.Equal("Featured", catalog.Name);
        Assert.Equal("genre", Assert.Single(catalog.Extra).Name);
    }

    [Fact]
    public void Parse_InvalidManifestFixture_FailsWithClearError()
    {
        var error = Assert.Throws<NuvioValidationException>(() =>
            AddonManifestParser.Parse(
                "https://addons.example.test/fixture",
                TestData.ReadFixture("addon-invalid.json")));

        Assert.Contains("name", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_OversizedManifest_FailsBeforeJsonParsing()
    {
        var payload = new string('x', (int)AddonFetchPolicy.Default.MaxManifestBytes + 1);

        var error = Assert.Throws<NuvioValidationException>(() =>
            AddonManifestParser.Parse("https://addons.example.test/fixture", payload));

        Assert.Contains("byte limit", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NormalizeManifestUrl_UsesHttpsAndBlocksUnsafeSchemes()
    {
        var normalized = AddonUrlPolicy.NormalizeManifestUrl("stremio://addons.example.test/fixture?x=1");

        Assert.Equal("https://addons.example.test/fixture/manifest.json?x=1", normalized.ToString());
        Assert.Throws<NuvioValidationException>(() =>
            AddonUrlPolicy.NormalizeManifestUrl("javascript:alert(1)"));
    }

    [Fact]
    public void NormalizeManifestUrl_AllowsSchemeLessHostPort()
    {
        var normalized = AddonUrlPolicy.NormalizeManifestUrl("localhost:11470/fixture");

        Assert.Equal("https://localhost:11470/fixture/manifest.json", normalized.ToString());
    }
}
