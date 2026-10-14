namespace TradingSimulator.Exchange.Application;

public sealed record QuoteItem(string Ticker, DateOnly TradeDate, decimal Open, decimal High, decimal Low, decimal Close);
