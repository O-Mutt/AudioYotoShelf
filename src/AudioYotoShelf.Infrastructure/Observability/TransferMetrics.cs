using System.Diagnostics.Metrics;

namespace AudioYotoShelf.Infrastructure.Observability;

/// <summary>
/// Counters for the transfer pipeline — the app's core function — so operators can alert on
/// "transfers are failing". Lives on a named <see cref="Meter"/> that the API's OpenTelemetry
/// provider subscribes to and exposes via the Prometheus scrape endpoint. Registered as a singleton.
/// </summary>
public sealed class TransferMetrics : IDisposable
{
    public const string MeterName = "AudioYotoShelf.Transfers";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _completed;
    private readonly Counter<long> _failed;

    public TransferMetrics()
    {
        _completed = _meter.CreateCounter<long>(
            "ays.transfers.completed", unit: "{transfer}", description: "Book transfers that completed successfully");
        _failed = _meter.CreateCounter<long>(
            "ays.transfers.failed", unit: "{transfer}", description: "Book transfers that failed");
    }

    public void RecordCompleted() => _completed.Add(1);
    public void RecordFailed() => _failed.Add(1);

    public void Dispose() => _meter.Dispose();
}
