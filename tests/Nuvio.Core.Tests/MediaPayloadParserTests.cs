using Nuvio.Core.Media;
using Nuvio.Core.Addons;
using Nuvio.Core.Validation;

namespace Nuvio.Core.Tests;

public sealed class MediaPayloadParserTests
{
    [Fact]
    public void ParseCatalogItems_MapsFixtureCatalog()
    {
        var items = MediaPayloadParser.ParseCatalogItems(TestData.ReadFixture("catalog-valid.json"));

        var item = Assert.Single(items);
        Assert.Equal("tt0000001", item.Id);
        Assert.Equal("movie", item.Type);
        Assert.Equal("Fixture Movie", item.Name);
        Assert.Equal("https://images.example.test/poster.jpg", item.PosterUrl?.ToString());
    }

    [Fact]
    public void ParseMediaDetails_MapsFixtureMetadata()
    {
        var details = MediaPayloadParser.ParseMediaDetails(TestData.ReadFixture("media-valid.json"));

        Assert.Equal("tt0000002", details.Id);
        Assert.Equal("series", details.Type);
        Assert.Equal(new[] { "Drama", "Mystery" }, details.Genres);
        Assert.Equal("imdb", Assert.Single(details.ExternalRatings).Source);
        Assert.Equal("Ada Example", Assert.Single(details.Cast).Name);
        Assert.Equal("Fixture Studio", Assert.Single(details.ProductionCompanies).Name);
        Assert.Equal("trailer-1", Assert.Single(details.Trailers).Id);
        Assert.Equal("Homepage", Assert.Single(details.Links).Name);

        var video = Assert.Single(details.Videos);
        Assert.Equal(1, video.Season);
        Assert.Equal(1, video.Episode);
        Assert.Equal(45, video.RuntimeMinutes);
    }

    [Fact]
    public void ParseCatalogItems_OversizedPayload_FailsBeforeJsonParsing()
    {
        var payload = new string('x', (int)AddonFetchPolicy.Default.MaxCatalogBytes + 1);

        var error = Assert.Throws<NuvioValidationException>(() =>
            MediaPayloadParser.ParseCatalogItems(payload));

        Assert.Contains("byte limit", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseMediaDetails_OversizedPayload_FailsBeforeJsonParsing()
    {
        var payload = new string('x', (int)AddonFetchPolicy.Default.MaxCatalogBytes + 1);

        var error = Assert.Throws<NuvioValidationException>(() =>
            MediaPayloadParser.ParseMediaDetails(payload));

        Assert.Contains("byte limit", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
