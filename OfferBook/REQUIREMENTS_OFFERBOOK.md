# Módulo OfferBook

Serve o livro de ofertas em tempo real às UIs. Não tem banco: o livro de cada ticker vive em memória.

## Decisões do usuário

- O Exchange simula ofertas em torno do `Last` (spread realista) e executa as ordens reais do Orders contra o livro (prioridade preço-tempo).
- 10 níveis por lado; cada nível tem preço, quantidade total e número de ofertas.
- Transporte: RabbitMQ (`EventEnvelope<T>`) Exchange -> OfferBook -> SignalR -> UI, somente para tickers com assinantes (heartbeat de interesse, como no Quotes).
- Publicação a cada mudança do livro ou a cada 1 s (o que ocorrer primeiro); sem mudança, o Exchange envia delta vazio (heartbeat).
- Snapshot ao iniciar/assinar e deltas durante o dia. Estado só em memória.
- Pregão 10h–18h (UTC-3). Fora do pregão o livro fica congelado e ordens são rejeitadas (`OrderRejected`) — responsabilidade do Exchange.
- UI página `/offerbook` da UI principal (porta 4200): nível alterado pisca verde se a quantidade aumentou, vermelho se diminuiu, sem piscar se igual.

## Fluxo

1. O Exchange publica `EventEnvelope<OfferBookSnapshot|OfferBookDelta>` no topic `trade-simulator.exchange.offerbook.events`
   (routing key `offerbook.{TICKER}`, transiente).
2. Este módulo consome a fila `trade-simulator.offerbook.book.events` (durável, TTL 10 s, máx. 10.000 mensagens, auto-ack, binding `offerbook.*`).
3. `OfferBookProcessor`: snapshot -> guarda em `LatestOfferBooks` e repassa ao grupo SignalR `ticker-{TICKER}`;
   delta -> aplica e repassa se contíguo (`previousSequence` == última sequence).
4. Lacuna de sequência: a base é descartada e um `OfferBookSnapshotRequested` é publicado. Delta antes do primeiro snapshot é ignorado,
   mas pede snapshot (no máximo 1 pedido por ticker a cada 2 s). Delta com `sequence` <= à atual é ignorado (atrasado/duplicado).
5. Assinantes são rastreados por conexão. No primeiro assinante de um ticker, e a cada 5 s, publica `OfferBookInterestRenewed(Tickers)`;
   no primeiro assinante também publica `OfferBookSnapshotRequested(Ticker)`.
   Saída: exchange `trade-simulator.offerbook.interest.events` (routing key `interest`); fila `trade-simulator.exchange.offerbook.interest`
   (durável, TTL 15 s) declarada e ligada (`#`) também por este módulo.

## Contrato (JSON camelCase, enums como string, envelope `{ id, eventType, event }`)

- `OfferBookSnapshot { ticker, sequence, phase (PreOpen|Open|Closed), bids[], asks[], timestamp }`; bids em preço decrescente, asks crescente.
- `OfferBookDelta { ticker, sequence, previousSequence, phase, changes[], timestamp }`; `changes[] = { side (Bid|Ask), price, quantity, orders }`,
  `quantity` 0 remove o nível. `changes` vazio é o heartbeat de 1 s.
- Nível: `{ price, quantity, orders }`. `sequence` cresce de 1 em 1 por ticker; snapshot reinicia a base.

## Interfaces

- SignalR `/hubs/offerbook`: `Subscribe(ticker)` devolve o último `OfferBookSnapshot` ou null (ticker inválido => `HubException("Invalid ticker.")`);
  `Unsubscribe(ticker)`. Eventos para o cliente: `OfferBookSnapshot` (substitui o livro) e `OfferBookDelta`.
  Se `previousSequence` do delta não bater com a sequence local, a UI reassina para obter snapshot.
- `GET /api/offerbook/{ticker}`: último snapshot (404 se não houver, 400 se ticker inválido). `GET /health`.
- Porta local 5050 (compose: host 8085). CORS: http://localhost:4200. RabbitMQ via `RabbitMq:Uri`.
