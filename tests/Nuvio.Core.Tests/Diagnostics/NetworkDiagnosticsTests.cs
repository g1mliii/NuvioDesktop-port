using Nuvio.Core.Diagnostics;

namespace Nuvio.Core.Tests.Diagnostics;

public sealed class NetworkDiagnosticsTests
{
    [Fact]
    public void Record_KeepsMostRecentEventsUpToCapacity()
    {
        var diagnostics = new NetworkDiagnostics(capacity: 3);
        for (var i = 0; i < 10; i++)
        {
            diagnostics.Record(new NetworkDiagnosticEvent(
                Timestamp: DateTimeOffset.UtcNow,
                Kind: NetworkEventKind.Success,
                Host: $"host-{i}.test",
                StatusCode: 200,
                DurationMs: 42,
                AddonId: $"addon-{i}",
                ResourceKind: "manifest",
                Message: $"event {i}"));
        }

        var snapshot = diagnostics.Snapshot();
        Assert.Equal(3, snapshot.Count);
        Assert.Equal("event 9", snapshot[0].Message);
        Assert.Equal("event 8", snapshot[1].Message);
        Assert.Equal("event 7", snapshot[2].Message);
    }

    [Fact]
    public void Record_RedactsQueryStringsInMessages()
    {
        var diagnostics = new NetworkDiagnostics();
        diagnostics.Record(new NetworkDiagnosticEvent(
            Timestamp: DateTimeOffset.UtcNow,
            Kind: NetworkEventKind.Success,
            Host: "addons.example.test",
            StatusCode: 200,
            DurationMs: 1,
            AddonId: null,
            ResourceKind: "manifest",
            Message: "https://addons.example.test/manifest.json?token=secret"));

        var event0 = diagnostics.Snapshot()[0];
        Assert.DoesNotContain("secret", event0.Message);
        Assert.Contains("<redacted>", event0.Message);
    }
}
