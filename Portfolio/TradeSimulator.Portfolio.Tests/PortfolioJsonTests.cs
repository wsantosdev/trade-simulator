using System.Text.Json;
using Moonad;
using TradeSimulator.Portfolio.Common;
using TradeSimulator.Portfolio.Domain;
using TradeSimulator.Portfolio.Domain.Entities;
using TradeSimulator.Portfolio.Domain.Enums;
using TradeSimulator.Portfolio.Domain.ValueObjects;
using TradeSimulator.Portfolio.Infrastructure.Messaging;
using TradeSimulator.Portfolio.Infrastructure.Serialization;
using TradeSimulator.Portfolio.Processors.PortfolioCommands;

namespace TradeSimulator.Portfolio.Tests;

public sealed class PortfolioJsonTests
{
    [Fact]
    public void Deserializes_identifiers_published_as_guid_objects()
    {
        var orderId = Guid.CreateVersion7();
        var accountId = Guid.CreateVersion7();
        var json = $$"""
            {"orderId":{"guid":"{{orderId}}"},"accountId":"{{accountId}}","ticker":"PETR4","side":"Sell","quantity":10}
            """;

        var message = JsonSerializer.Deserialize<OrderSubmittedMessage>(json, PortfolioJson.Options)!;

        Assert.Equal(orderId, message.OrderId);
        Assert.Equal(accountId, message.AccountId);
        Assert.Equal(OrderSide.Sell, message.Side);
        Assert.Equal(10, message.Quantity);
    }

    [Fact]
    public void Round_trips_successful_response_envelope()
    {
        var result = new PortfolioCommandResult(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "PETR4", 5, 10, 5, 15);
        var envelope = EventEnvelope.Of(Result<PortfolioCommandResult, PortfolioCommandError>.Ok(result));

        var json = JsonSerializer.Serialize(envelope, PortfolioJson.Options);
        var restored = JsonSerializer.Deserialize<EventEnvelope<Result<PortfolioCommandResult, PortfolioCommandError>>>(json, PortfolioJson.Options)!;

        Assert.Equal(envelope.Id, restored.Id);
        Assert.False(restored.Event.IsError);
        Assert.Equal(result, restored.Event.ResultValue);
    }

    [Fact]
    public void Round_trips_failed_response_envelope()
    {
        var error = new PortfolioCommandError(Guid.CreateVersion7(), null, null, "PETR4", 5, null, null, null, "InsufficientAvailableQuantity", "{bad");
        var envelope = EventEnvelope.Of(Result<PortfolioCommandResult, PortfolioCommandError>.Error(error));

        var json = JsonSerializer.Serialize(envelope, PortfolioJson.Options);
        var restored = JsonSerializer.Deserialize<EventEnvelope<Result<PortfolioCommandResult, PortfolioCommandError>>>(json, PortfolioJson.Options)!;

        Assert.True(restored.Event.IsError);
        Assert.Equal(error, restored.Event.ErrorValue);
    }

    [Fact]
    public void Round_trips_asset_events_by_type_name()
    {
        var asset = Asset.Open(
            AssetId.New(), AccountId.TryCreate(Guid.CreateVersion7()).ResultValue,
            Ticker.TryCreate("PETR4").ResultValue, Quantity.TryCreate(7).ResultValue, DateTimeOffset.UtcNow).ResultValue;
        var @event = asset.UncommittedEvents[0];

        var json = AssetEventSerializer.Serialize(@event);
        var restored = AssetEventSerializer.Deserialize(@event.GetType().Name, json);

        Assert.Equal(@event, restored);
    }

    [Fact]
    public void Asset_reservation_result_uses_the_result_wire_format()
    {
        var orderId = Guid.CreateVersion7();
        var ok = EventEnvelope.Of(Moonad.Result<AssetsBlocked, AssetBlockError>.Ok(new AssetsBlocked(orderId, Guid.CreateVersion7(), "PETR4", 10)));
        var failed = EventEnvelope.Of(Moonad.Result<AssetsBlocked, AssetBlockError>.Error(
            new AssetBlockError(orderId, Guid.CreateVersion7(), "PETR4", AssetBlockErrorKind.InsufficientQuantity, "x")));

        var okJson = JsonSerializer.Serialize(ok, PortfolioJson.Options);
        var failedJson = JsonSerializer.Serialize(failed, PortfolioJson.Options);

        Assert.Contains("\"isError\":false", okJson);
        Assert.Contains("\"result\":{", okJson);
        Assert.Contains("\"isError\":true", failedJson);
        Assert.Contains("\"kind\":\"InsufficientQuantity\"", failedJson);
    }
}
