using Nuvio.Core.Net;
using Nuvio.Core.Validation;

namespace Nuvio.Core.Tests.Net;

public sealed class RedirectPolicyTests
{
    [Fact]
    public void Validate_SameHostHttps_Allowed()
    {
        RedirectPolicy.Validate(
            new Uri("https://addons.example.test/manifest.json"),
            new Uri("https://addons.example.test/new/manifest.json"),
            "Addon manifest");
    }

    [Fact]
    public void Validate_AllowsCrossHostHttpsRedirect()
    {
        // CDN/Cloudflare-backed addons redirect to a different host over HTTPS; this is now allowed.
        // The redirect target is independently validated by AddonUrlPolicy before reaching here.
        RedirectPolicy.Validate(
            new Uri("https://addons.example.test/manifest.json"),
            new Uri("https://cdn.example.net/manifest.json"),
            "Addon manifest");
    }

    [Fact]
    public void Validate_RejectsHttpsToHttpDowngrade()
    {
        var error = Assert.Throws<NuvioValidationException>(() =>
            RedirectPolicy.Validate(
                new Uri("https://addons.example.test/manifest.json"),
                new Uri("http://addons.example.test/manifest.json"),
                "Addon manifest"));

        Assert.Contains("HTTPS", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(301, true)]
    [InlineData(302, true)]
    [InlineData(303, true)]
    [InlineData(307, true)]
    [InlineData(308, true)]
    [InlineData(200, false)]
    [InlineData(404, false)]
    public void IsRedirect_OnlyTrueForRedirectCodes(int status, bool expected) =>
        Assert.Equal(expected, RedirectPolicy.IsRedirect(status));
}
