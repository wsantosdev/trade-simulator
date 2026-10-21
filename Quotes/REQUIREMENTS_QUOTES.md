# Quotes module

Serves real-time quotes to the UIs. It has no database: the last quote of each ticker lives in memory.

## Flow

1. The Exchange simulates one quote per second for each ticker that has subscribers (see `Exchange/REQUIREMENTS_EXCHANGE.md`)
   and publishes `EventEnvelope<Quote>` to the topic exchange `trade-simulator.exchange.quote.events`
   (routing key `quote.{TICKER}`, transient).
2. This module consumes queue `trade-simulator.quotes.quote.events` (durable queue (RabbitMQ 4 forbids transient shared queues) with TTL 10 s, max 10,000 messages,
   auto-ack: quotes are perishable), keeps the last quote per ticker and pushes `QuoteUpdated` to SignalR group `ticker-{TICKER}`.
3. Subscribers are tracked per connection. When a ticker gets its first subscriber, and every 5 s, the module publishes
   `EventEnvelope<QuoteInterestRenewed(Tickers)>` to `trade-simulator.quotes.interest.events`.
   The Exchange stops simulating a ticker 15 s after the last renewal.

## Contract

`Quote(Ticker, Open, Close, High, Low, Last, Variation, Timestamp)`, camelCase JSON, enums as strings.

- `Open`: opening price of the day. `Close`: previous day's closing price. `High`/`Low`: extremes so far today. `Last`: current price.
- `Variation`: `Unchanged`, `Up` or `Down` against the immediately preceding quote. `Unchanged` must not flash in the UI.

## Interfaces

- SignalR hub `/hubs/quotes`: `Subscribe(ticker)` (returns the last known `Quote` or null; invalid ticker raises `HubException`),
  `Unsubscribe(ticker)`; server event `QuoteUpdated(Quote)`. Subscriptions are released when the connection drops.
- `GET /api/quotes/{ticker}/latest`: last quote received (404 if none).
- UI: página `/quotes` da UI principal (`UI/projects/main`, port 4200). The Orders ticket also subscribes to show the last price and day change.
