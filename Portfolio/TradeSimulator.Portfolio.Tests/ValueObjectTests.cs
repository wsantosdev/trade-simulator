using TradeSimulator.Portfolio.Domain;
using TradeSimulator.Portfolio.Domain.ValueObjects;

namespace TradeSimulator.Portfolio.Tests;

public sealed class ValueObjectTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Quantity_rejects_zero_and_negative(long value) =>
        Assert.IsType<InvalidQuantity>(Quantity.TryCreate(value).ErrorValue);

    [Fact]
    public void Quantity_accepts_positive_values() =>
        Assert.Equal(7, Quantity.TryCreate(7).ResultValue.Value);

    [Theory]
    [InlineData("petr4", "PETR4")]
    [InlineData(" VALE3 ", "VALE3")]
    [InlineData("ITUB5", "ITUB5")]
    public void Ticker_is_normalized(string input, string expected) =>
        Assert.Equal(expected, Ticker.TryCreate(input).ResultValue.Value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("PETR")]
    [InlineData("PETR6")]
    [InlineData("PET4")]
    [InlineData("PETR44")]
    public void Ticker_rejects_invalid_values(string? input) =>
        Assert.IsType<InvalidTicker>(Ticker.TryCreate(input).ErrorValue);

    [Fact]
    public void Ids_accept_only_guid_v7()
    {
        Assert.False(AccountId.TryCreate(Guid.CreateVersion7()).IsError);
        Assert.False(AssetId.TryCreate(Guid.CreateVersion7()).IsError);
        Assert.IsType<InvalidAccountId>(AccountId.TryCreate(Guid.Empty).ErrorValue);
        Assert.IsType<InvalidAccountId>(AccountId.TryCreate(Guid.NewGuid()).ErrorValue);
        Assert.IsType<InvalidAssetId>(AssetId.TryCreate(Guid.Empty).ErrorValue);
        Assert.IsType<InvalidAssetId>(AssetId.TryCreate(Guid.NewGuid()).ErrorValue);
    }
}
