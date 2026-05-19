using Nuvio.Core.Addons;

namespace Nuvio.Core.Diagnostics;

public static class NetworkDiagnosticsExtensions
{
    public static void RecordAddonFailure(
        this INetworkDiagnostics? diagnostics,
        ManagedAddon addon,
        string resourceKind,
        Exception ex,
        TimeProvider timeProvider)
    {
        diagnostics?.Record(new NetworkDiagnosticEvent(
            Timestamp: timeProvider.GetUtcNow(),
            Kind: NetworkEventKind.Failure,
            Host: addon.ManifestUrl.Host,
            StatusCode: null,
            DurationMs: null,
            AddonId: addon.Id,
            ResourceKind: resourceKind,
            Message: $"{addon.Id} {resourceKind} failed: {ex.GetType().Name}"));
    }
}
