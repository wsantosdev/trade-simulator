using Microsoft.Extensions.Logging.Abstractions;
using TradeSimulator.Portfolio.Domain;
using TradeSimulator.Portfolio.Features.DepositAsset;

namespace TradeSimulator.Portfolio.Tests.Processing;

public sealed class DepositAssetHandlerTests
{
    private readonly FakeStore _store = new();
    private readonly FakeNotifier _notifier = new();
    private readonly DepositAssetHandler _handler;
    private readonly Guid _account = Guid.CreateVersion7();

    public DepositAssetHandlerTests() =>
        _handler = new(_store, _notifier, new Infrastructure.FakeMetrics(), TimeProvider.System, NullLogger<DepositAssetHandler>.Instance);

    [Fact]
    public async Task First_deposit_opens_the_asset_and_next_ones_add_to_it()
    {
        await _handler.ExecuteAsync(_account, "PETR4", 10, default);
        var result = await _handler.ExecuteAsync(_account, "PETR4", 5, default);

        Assert.False(result.IsError);
        Assert.Equal(15, Assert.Single(_store.Assets).Available.Value);
        Assert.Equal(2, _notifier.Notified.Count);
    }

    [Fact]
    public async Task Invalid_input_is_rejected()
    {
        Assert.True((await _handler.ExecuteAsync(_account, "PETR4", 0, default)).IsError);
        Assert.True((await _handler.ExecuteAsync(_account, " ", 5, default)).IsError);
        Assert.True((await _handler.ExecuteAsync(Guid.Empty, "PETR4", 5, default)).IsError);
        Assert.Empty(_store.Assets);
    }

    [Fact]
    public async Task Notification_failure_does_not_fail_the_deposit()
    {
        _notifier.Fail = true;

        var result = await _handler.ExecuteAsync(_account, "PETR4", 10, default);

        Assert.False(result.IsError);
    }
}
