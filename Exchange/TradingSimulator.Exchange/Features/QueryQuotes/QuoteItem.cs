using TradingSimulator.Exchange.Domain.ValueObjects;

namespace TradingSimulator.Exchange.Features.QueryQuotes;

public sealed record QuoteItem(string Ticker, DateOnly TradeDate, decimal Open, decimal High, decimal Low, decimal Close);
