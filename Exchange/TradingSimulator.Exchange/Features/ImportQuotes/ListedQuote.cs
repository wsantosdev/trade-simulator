using TradingSimulator.Exchange.Domain.ValueObjects;

namespace TradingSimulator.Exchange.Features.ImportQuotes;

public sealed record ListedQuote(string Ticker, DateOnly TradeDate, decimal Open, decimal High, decimal Low, decimal Close);
