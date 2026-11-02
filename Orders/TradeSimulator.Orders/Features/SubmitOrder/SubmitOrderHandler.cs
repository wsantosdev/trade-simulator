using Moonad;
using TradeSimulator.Orders.Common;
using TradeSimulator.Orders.Domain;
using TradeSimulator.Orders.Domain.Entities;
using TradeSimulator.Orders.Domain.Enums;
using TradeSimulator.Orders.Domain.ValueObjects;
using TradeSimulator.Orders.Processors;
using TradeSimulator.Orders.Processors.OrderTransition;

namespace TradeSimulator.Orders.Features.SubmitOrder;

public sealed record SubmitOrderCommand(
    Guid AccountId,
    string? Ticker,
    OrderSide Side,
    OrderType Type,
    long Quantity,
    decimal? Price,
    string? RequestHash);

// Inbound port.
public interface ISubmitOrder
{
    Task<Result<Order, IOrderError>> ExecuteAsync(SubmitOrderCommand command, CancellationToken cancellationToken);
}

public sealed class SubmitOrderHandler(
    IOrderStore store,
    IOrderNotifier notifier,
    IReservationGateway reservations,
    IOrderTransitions transitions,
    TimeProvider clock,
    IOrdersMetrics metrics) : ISubmitOrder
{
    public async Task<Result<Order, IOrderError>> ExecuteAsync(SubmitOrderCommand command, CancellationToken cancellationToken)
    {
        var order = Order.Send(
            OrderId.New(),
            command.AccountId,
            command.Ticker,
            command.Side,
            command.Type,
            command.Quantity,
            command.Price,
            command.RequestHash,
            clock.GetUtcNow());
        if (order.IsError)
        {
            metrics.OperationCompleted("submit", order.ErrorValue);
            return order;
        }

        var stored = await store.SendAsync(order.ResultValue, cancellationToken);
        metrics.OperationCompleted("submit", stored.IsError ? stored.ErrorValue : null);
        if (stored.IsError)
            return stored;

        // A repeated request returns the original order untouched.
        if (stored.ResultValue.Id != order.ResultValue.Id)
            return stored;

        metrics.OrderSubmitted(command.Side.ToString(), command.Type.ToString());

        // Synchronous validation: funds (buy) or position (sell) are reserved before the order is dispatched.
        var reservation = await reservations.ReserveAsync(stored.ResultValue, cancellationToken);
        var source = command.Side == OrderSide.Buy ? "funds" : "assets";
        if (!reservation.Reserved)
        {
            metrics.SubmissionRejected(source, reservation.Reason);
            await transitions.RejectSubmissionAsync(stored.ResultValue.Id, reservation.Reason ?? "ReservationRejected", cancellationToken);
            return reservation.Unavailable
                ? Result<Order, IOrderError>.Error(new ReservationUnavailable())
                : Result<Order, IOrderError>.Error(new SubmissionRejected(reservation.Reason ?? "ReservationRejected"));
        }

        metrics.SubmissionConfirmed(source);
        await notifier.OrderCreatedAsync(OrderView.From(stored.ResultValue), cancellationToken);
        try
        {
            await transitions.DispatchAsync(stored.ResultValue.Id, cancellationToken);
        }
        catch
        {
            await reservations.ReleaseAsync(stored.ResultValue, CancellationToken.None);
            await transitions.RejectSubmissionAsync(stored.ResultValue.Id, "DispatchFailed", CancellationToken.None);
            throw;
        }

        var current = await store.GetAsync(stored.ResultValue.Id, cancellationToken);
        return current.IsError ? stored : current;
    }
}
