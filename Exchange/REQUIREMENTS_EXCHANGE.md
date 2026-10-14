# Requisitos do módulo Exchange

> Documento em construção, complementado a cada resposta da entrevista de levantamento. Itens marcados com **(pendente)** aguardam confirmação.

## Escopo

- O módulo Exchange é o último e mais complexo do simulador: um microsserviço independente (schema `exchange` no PostgreSQL compartilhado, interface `Exchange UI`).
- Recebe ordens do módulo Orders e realiza o **casamento (matching)** entre elas.
- Segue `PATTERNS.md` (Ports & Adapters, Vertical Slices, `Result` do Moonad, erros tipados, ids v7 como `AccountId`, `Quantity`, `EventEnvelope<T>` em toda comunicação assíncrona, idempotência pelo id do envelope) e `UI.md`.

## Integrações conhecidas

- **Orders → Exchange:** envio de ordens, cancelamentos e substituições (detalhes **pendentes**).
- **Exchange → Orders:** *ack* da ordem (PendingNew → New) e relatórios de execução (totais ou parciais), via RabbitMQ com outbox. Substituição faz a ordem perder prioridade no livro. Execuções ocorridas durante um cancelamento/substituição causam a rejeição desse pedido.
- **Exchange → Portfolio:** comandos Deposit/Withdraw na exchange `trading-simulator.portfolio.command` (quem os publica ainda será definido: Exchange ou Orders) **(pendente)**.
- Módulo de cotações (ordens a mercado): trabalho futuro.

## Estrutura criada

- `Exchange/TradingSimulator.Exchange` (API, `net10.0`, porta 5048) com pastas `Api`, `Application`, `Domain` e `Infrastructure`.
- `Exchange/TradingSimulator.Exchange.Tests` (xunit).
- `UI/projects/exchange` (Angular, porta 4204, separada da UI principal e sem o menu comum), com a navegação padrão de `UI.md`.
- Role e schema `exchange` em `database/init-schemas.sql`.

## Cadastro de instrumentos

- Fonte: arquivo B3 `BVBG.028.02_*.xml` (cadastro de instrumentos, ~450 MB, ignorado pelo git), lido em *streaming* (`XmlReader`, DTD proibido).
- Cada `<Instrm>` traz `Mkt` e `Desc` em `FinInstrmAttrCmon` e o `<EqtyInf>` com `TckrSymb`. Importa-se apenas `EqtyInf` com `Mkt = 10` (mercado à vista: ações, units, FIIs, ETFs e BDRs); fracionário (20), leilão (17), termo e derivativos ficam de fora.
- Registro: `InstrumentId` (UUID v7, gerado na primeira importação e estável nas seguintes), `Ticker` (ex.: `PETR4`) e nome (`Desc` com espaços normalizados, ex.: `PETROBRAS PN N2`). Tickers fora do padrão (sufixo `B` de balcão, iniciados por dígito) são ignorados.
- Tabela `instruments` no schema `exchange` (`Infrastructure/Database/schema.sql`), com `ticker` único; reimportar é idempotente (*upsert* por ticker, atualizando só o nome).
- Importação por linha de comando (não é endpoint HTTP): `dotnet run --project Exchange/TradingSimulator.Exchange -- --import-instruments <caminho do XML>`.
- Consulta: `GET /api/instruments?search=&page=` e `GET /api/instruments/{ticker}`.

## Cotações históricas

- Fonte: arquivo B3 `COTAHIST` (`COTACOES_2026.TXT`, ~776 MB, ignorado pelo git): texto de largura fixa, 245 caracteres por linha, Latin1. Registro `00` = cabeçalho, `01` = cotação, `99` = trailer.
- Campos usados (posições 1-based): data 3-10 (`yyyyMMdd`), ticker `CODNEG` 13-24, `TPMERC` 25-27, abertura 57-69, máxima 70-82, mínima 83-95, fechamento (`PREULT`) 109-121. Preços têm 13 dígitos com 2 decimais implícitos.
- Importa-se só `TPMERC = 010` (mercado à vista, o mesmo `Mkt = 10` do cadastro); opções, fracionário, termo etc. ficam de fora. Linhas inválidas (preço ≤ 0, mínima/máxima incoerentes, ticker fora do padrão) são ignoradas.
- Tabela `quotes` (PK `ticker` + `trade_date`, sem FK para `instruments`): abertura, máxima, mínima e fechamento `NUMERIC(13,2)`. Reimportar é idempotente (*upsert*).
- Os preços não são ajustados por desdobramentos/proventos.
- Importação por linha de comando: `dotnet run --project Exchange/TradingSimulator.Exchange -- --import-quotes <caminho do TXT>`.
- Consulta: `GET /api/quotes/{ticker}?from=&to=`.

## Cotações simuladas (tempo real)

- Pregão simulado das 10h às 18h (UTC-3 fixo, fins de semana ignorados), uma cotação por segundo. Antes das 10h publica a última cotação do dia anterior; após as 18h, a última do dia (`Last` = fechamento do dia mais recente da base).
- Só são simulados os tickers com interesse ativo: o módulo Quotes publica `EventEnvelope<QuoteInterestRenewed>` a cada 5 s; o interesse expira após 15 s.
- Parâmetros vindos de `exchange.quotes`: dia mais recente (abertura, fechamento-alvo, máx/mín do dia), fechamento anterior e mínima/máxima de toda a série (limites que jamais são ultrapassados). Estado intradiário só em memória; após reinício a simulação recomeça do pregão.
- Algoritmo: ponte browniana da abertura ao fechamento-alvo, 28.800 passos, volatilidade calibrada pela faixa do dia, preço limitado a [mínima, máxima] da série e arredondado a 1 centavo.
- `Quote(Ticker, Open, Close, High, Low, Last, Variation, Timestamp)`: `Open` = abertura do dia, `Close` = fechamento do dia anterior, `Variation` = `Unchanged|Up|Down` em relação à cotação anterior. Publicada como `EventEnvelope<Quote>` no exchange `trading-simulator.exchange.quote.events` (routing key `quote.{TICKER}`).

## Livro de ofertas e match engine

- Um `OrderBook` em memória por ticker: tick R$ 0,01 (preços decimais), filas FIFO por nível, prioridade preço-tempo; a execução ocorre ao preço da ordem em repouso. Ordens limitadas e a mercado (saldo não executado é cancelado), cancelamento e alteração de preço (perde prioridade; se cruzar, executa). Origem da ordem: `Simulated` ou `User`.
- `OrderBookEngine` único, com `SemaphoreSlim` protegendo todas as operações. O ciclo de 1 s do `QuoteSimulationService` chama `QuoteSimulator.RunCycleAsync` e depois o ciclo do motor; `QuoteSimulator.NextQuoteAsync` devolve a cota em cache para o mesmo segundo (não duplica quotes). Tickers ativos = interesse do OfferBook ∪ tickers com ordens User em repouso; livros ociosos (sem ordens User e sem interesse) são podados após 10 min.
- Liquidez simulada: ~10 níveis por lado em torno do `Last` (spread = max(1 tick, ~0,05% do preço)), 1–3 ofertas simuladas por nível, reabastecimento dos níveis consumidos, 1–3 perturbações aleatórias por segundo (cancela/reinsere, muda quantidade) e remoção das ofertas fora da grade ao recentrar. `Random` é injetado.
- Fases: `PreOpen`/`Closed` congelam o livro (se não existir, é gerado estático em torno do `Last` da fase); `Open` é dinâmico e recentra. Ordens do usuário fora do pregão, sem histórico de cotações ou inválidas geram `OrderRejected`. Cancelamento é aceito em qualquer fase; alteração só em `Open`.
- Orders -> Exchange: exchange `trading-simulator-order-commands`, fila `trading-simulator.exchange.order.commands` (`OrderDispatched`, `OrderCancelRequested`, `OrderReplaceRequested`), ack manual após processar; mensagem malformada é descartada. `OrderDispatched` repetido (outbox at-least-once) é ignorado.
- Exchange -> Orders: exchange `trading-simulator.exchange.order.events` (routing key = tipo do evento, persistente), fila `trading-simulator.orders.order.events`. Eventos: `OrderAcked` (sempre antes das execuções), `OrderRejected`, `OrderPartiallyFilled`/`OrderFilled` (lado em repouso e agressor, só ordens User), `OrderCancelled`, `OrderCancelRejected`, `OrderReplaced`, `OrderReplaceRejected`. Eventos pendentes ficam numa fila em memória e são reenviados se o broker falhar.
- Exchange -> OfferBook: exchange `trading-simulator.exchange.offerbook.events`, routing key `offerbook.{TICKER}`, `OfferBookSnapshot`/`OfferBookDelta` (top-10, sequência por ticker, delta imediato após cada operação que muda a janela, delta vazio como heartbeat se nada mudou no segundo). Só publica para tickers com interesse: `trading-simulator.offerbook.interest.events` -> fila durável `trading-simulator.exchange.offerbook.interest` (TTL 15 s), `OfferBookInterestRenewed` (TTL de 15 s por ticker) e `OfferBookSnapshotRequested` (snapshot sob demanda).
- Limitações: estado só em memória (ordens User em repouso se perdem se o Exchange reiniciar); as execuções não movem o `Last` das cotações.

## Pendente (entrevista)

- Persistência (Event Sourcing ou não), idempotência e consultas.
- Funcionalidades da `Exchange UI`.

## Passos

- [x] Estrutura básica (projetos, UI, requisitos, schema)
- [x] Cadastro de instrumentos (importação BVBG.028 e consulta)
- [x] Cotações históricas (importação COTAHIST e consulta)
- [x] Cotações simuladas e publicação (RabbitMQ)
- [x] Livro de ofertas e match engine
- [ ] Entrevista de requisitos
- [ ] Domínio e aplicação
- [ ] Infraestrutura, API e UI
- [ ] Dockerfile e compose
