using TradingSimulator.Exchange.Domain.ValueObjects;

namespace TradingSimulator.Exchange.Domain.Events;

// What happened to an order of the Orders module, in domain terms; the messaging adapter translates it to the wire contract.
public abstract record OrderEvent(OrderId OrderId);
