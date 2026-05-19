using System.Collections.Concurrent;
using System.Collections.Generic;
using Nuvio.Core.Security;

namespace Nuvio.Core.Diagnostics;

public enum NetworkEventKind
{
    Success,
    Failure,
    Redirect,
    Throttled,
    Skipped
}

public sealed record NetworkDiagnosticEvent(
    DateTimeOffset Timestamp,
    NetworkEventKind Kind,
    string Host,
    int? StatusCode,
    long? DurationMs,
    string? AddonId,
    string ResourceKind,
    string Message);

public interface INetworkDiagnostics
{
    void Record(NetworkDiagnosticEvent diagnosticEvent);
    IReadOnlyList<NetworkDiagnosticEvent> Snapshot();
    void Clear();
}

public sealed class NetworkDiagnostics : INetworkDiagnostics
{
    public const int DefaultCapacity = 100;

    private readonly int _capacity;
    private readonly object _gate = new();
    private readonly LinkedList<NetworkDiagnosticEvent> _events = new();

    public NetworkDiagnostics(int capacity = DefaultCapacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
    }

    public int Capacity => _capacity;

    public void Record(NetworkDiagnosticEvent diagnosticEvent)
    {
        var sanitized = diagnosticEvent with
        {
            Message = LogRedactor.RedactUrl(diagnosticEvent.Message)
        };

        lock (_gate)
        {
            _events.AddFirst(sanitized);
            while (_events.Count > _capacity)
            {
                _events.RemoveLast();
            }
        }
    }

    public IReadOnlyList<NetworkDiagnosticEvent> Snapshot()
    {
        lock (_gate)
        {
            return _events.ToArray();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _events.Clear();
        }
    }
}
