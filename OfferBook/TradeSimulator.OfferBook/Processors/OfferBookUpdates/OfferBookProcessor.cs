using TradeSimulator.OfferBook.Common;
using System.Text.Json;
using Moonad;
using TradeSimulator.OfferBook.Domain;
using TradeSimulator.OfferBook.Domain.Enums;
using TradeSimulator.OfferBook.Domain.ValueObjects;

namespace TradeSimulator.OfferBook.Processors.OfferBookUpdates;

public sealed class OfferBookProcessor(
    LatestOfferBooks latest,
    IOfferBookNotifier notifier,
    IOfferBookSnapshotRequestPublisher snapshotRequests,
    IOfferBookMetrics metrics)
{
    // A malformed message is reported as an error and must be dropped, never retried.
    public async Task<Result<ProcessOutcome, IOfferBookError>> ProcessAsync(
        string body, JsonSerializerOptions options, CancellationToken cancellationToken)
    {
        var result = await ParseAndApplyAsync(body, options, cancellationToken);
        if (result.IsError)
            metrics.UpdateMalformed();
        else
            metrics.UpdateProcessed(result.ResultValue);
        return result;
    }

    private async Task<Result<ProcessOutcome, IOfferBookError>> ParseAndApplyAsync(
        string body, JsonSerializerOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<EventEnvelope<JsonElement>>(body, options);
            if (envelope is null)
                return new MalformedMessage();

            switch (envelope.EventType)
            {
                case nameof(OfferBookSnapshot):
                    var snapshot = envelope.Event.Deserialize<OfferBookSnapshot>(options);
                    if (snapshot is null || !IsValid(snapshot.Ticker, snapshot.Bids, snapshot.Asks))
                        return new MalformedMessage();
                    return await HandleSnapshotAsync(snapshot, cancellationToken);
                case nameof(OfferBookDelta):
                    var delta = envelope.Event.Deserialize<OfferBookDelta>(options);
                    if (delta is null || delta.Changes is null || Ticker.TryCreate(delta.Ticker).IsError)
                        return new MalformedMessage();
                    return await HandleDeltaAsync(delta, cancellationToken);
                default:
                    return new MalformedMessage();
            }
        }
        catch (JsonException)
        {
            return new MalformedMessage();
        }
    }

    private async Task<ProcessOutcome> HandleSnapshotAsync(OfferBookSnapshot snapshot, CancellationToken cancellationToken)
    {
        latest.ApplySnapshot(snapshot);
        await notifier.SnapshotAsync(snapshot, cancellationToken);
        return ProcessOutcome.SnapshotStored;
    }

    private async Task<ProcessOutcome> HandleDeltaAsync(OfferBookDelta delta, CancellationToken cancellationToken)
    {
        switch (latest.ApplyDelta(delta))
        {
            case DeltaOutcome.Applied:
                await notifier.DeltaAsync(delta, cancellationToken);
                return ProcessOutcome.DeltaForwarded;
            case DeltaOutcome.SnapshotNeeded:
                await snapshotRequests.PublishAsync(
                    EventEnvelope.Of(new OfferBookSnapshotRequested(delta.Ticker)), cancellationToken);
                return ProcessOutcome.SnapshotRequested;
            default:
                return ProcessOutcome.DeltaIgnored;
        }
    }

    private static bool IsValid(string ticker, IReadOnlyList<PriceLevel>? bids, IReadOnlyList<PriceLevel>? asks) =>
        bids is not null && asks is not null && !Ticker.TryCreate(ticker).IsError;
}
