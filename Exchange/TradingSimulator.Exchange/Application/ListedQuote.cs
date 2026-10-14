namespace TradingSimulator.Exchange.Application;

public sealed record ListedQuote(string Ticker, DateOnly TradeDate, decimal Open, decimal High, decimal Low, decimal Close);
