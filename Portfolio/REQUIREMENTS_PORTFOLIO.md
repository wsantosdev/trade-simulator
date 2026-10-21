# Requisitos do módulo de Portfólio

> Documento em construção, complementado a cada resposta da entrevista de levantamento. Itens marcados com **(pendente)** aguardam confirmação.

## Escopo

- O módulo de Portfólio é um microsserviço independente (schema `portfolio` no PostgreSQL compartilhado, interface `Portfolio UI`).
- A carteira do cliente é formada pelos ativos que ele possui. Não há cadastro de carteiras: o **Id da conta corrente é o agregador dos ativos**, e a carteira passa a existir no primeiro depósito.
- Segue `PATTERNS.md` (Ports & Adapters, Vertical Slices, Event Sourcing, `Result` do Moonad, erros tipados) e `UI.md`.
- Fora de escopo nesta versão: preço médio e valor de mercado.

## Asset

- `Asset` contém: `AssetId`, `AccountId` (conta corrente), `Ticker`, `Available` (quantidade disponível) e `Blocked` (quantidade bloqueada para ordens de venda).
- `Total` é propriedade computada: `Available + Blocked`.
- Um ativo é único por **conta + ticker** (restrição no banco para ativos existentes).
- Todos os ids (`AssetId`, `AccountId`, id de envelope) seguem o modelo de `AccountId` (GUID v7, factory `TryCreate` com `Result`).
- Quantidades são inteiras e representadas pelo tipo `Quantity` (`readonly record struct` com `long Value`, `TryCreate` rejeita ≤ 0, `Quantity.Zero` para saldos). O teto de 1.000.000 é validado pelo agregado em cada operação (`QuantityAboveLimit`); o total acumulado não tem limite. O ticker é validado como em Ordens (`^[A-Z]{4}[345]$`, normalizado para maiúsculas).
- Quando um ativo é removido e o cliente volta a depositá-lo, é criado um **novo `AssetId`**.

## Operações (agregado `Asset`)

Todas exigem quantidade maior que zero e no máximo 1.000.000 (teto universal). Uma operação afeta somente um ativo.

| Operação | Efeito | Regras / erros tipados |
|---|---|---|
| Depósito | Cria o ativo (se a conta ainda não o possui) ou soma à `Available`. | Quantidade ≤ 1.000.000. |
| Bloqueio | Transfere a quantidade de `Available` para `Blocked`. | Quantidade ≤ `Available`; ativo inexistente. |
| Retirada | Subtrai a quantidade de `Blocked`. Se `Available` e `Blocked` ficarem zerados (`Total` = 0), o ativo é removido da carteira. | Quantidade ≤ `Blocked`; ativo inexistente. |
| Desbloqueio | Inverso do bloqueio: transfere de `Blocked` para `Available` (ordem cancelada). | Quantidade ≤ `Blocked`; ativo inexistente. |

- Depósitos e retiradas ocorrem somente a partir de execuções (quantidade executada) de ordens: compra gera depósito, venda gera retirada. Podem ocorrer retiradas parciais sucessivas sobre o mesmo bloqueio.
- Erros são `readonly struct` sem propriedades, implementando `IPortfolioError`, sem exceções e sem persistência quando houver falha.

## Persistência

- **Event Sourcing por ativo** (sem agregado de carteira, para evitar carga excessiva em memória em posições grandes). Cada ativo tem seu stream.
- Snapshot a cada **100 eventos** do ativo (id do ativo, posição do último evento e quantidades), na mesma transação dos eventos e do outbox.
- Eventos de domínio (um arquivo por evento, junto à fatia): `AssetDeposited`, `AssetBlocked`, `AssetWithdrawn` e `AssetUnblocked`. Não existe `AssetRemoved`: a retirada que zera o ativo gera apenas `AssetWithdrawn`, e a mensagem/consulta informa `Total = 0`, o que faz a UI remover o ativo da lista. O ativo com `Total = 0` deixa de existir para novos comandos e um novo depósito cria outro `AssetId`.
- Como o comando de depósito informa conta + ticker (e não o `AssetId`), uma tabela de ativos ativos com índice único (conta, ticker) localiza o ativo e protege contra dois primeiros depósitos concorrentes.
- Controle de concorrência otimista por versão do stream, com retry.
- PostgreSQL compartilhado: schema `portfolio` e usuário `portfolio` (ver `PATTERNS.md`). Schema SQL versionado e embutido no assembly.

## Mensageria (RabbitMQ, JSON)

Toda a comunicação assíncrona usa `EventEnvelope<T>` (o mesmo de Ordens), que deve carregar um **id de envelope** (GUID v7) único por operação (será adicionado `Id` ao `EventEnvelope` de Ordens, mantendo um único tipo de envelope).

- Exchange de comandos: `trade-simulator.portfolio.command` (topic, durável). Fila única de comandos: `trade-simulator.portfolio.asset.commands` (sugestão, seguindo `trade-simulator.<consumidor>.<assunto>`), vinculada com `#`.
- O produtor dos comandos (Exchange ou Ordens) ainda será definido.
- Respostas: exchange `trade-simulator.portfolio.events` (topic), routing keys `asset.command.succeeded` e `asset.command.failed`.
- Mensagem inválida (JSON malformado, id/ticker/quantidade inválidos) gera resposta de erro tipada, equivalente ao 400 da API HTTP; `PortfolioCommandError` tem uma propriedade com o conteúdo bruto (JSON malformado) recebido.

Comandos recebidos (todos com `AccountId`, `Ticker` e `Quantity`):

- `Deposit`: soma à quantidade disponível.
- `Withdraw`: subtrai da quantidade bloqueada.
- `Block`: transfere de disponível para bloqueada.
- `Unblock`: inverso de `Block`.

Integração com Ordens:

- Ao receber `OrderSubmitted` (antes `OrderSent`): se a ordem for de **venda**, bloqueia a quantidade da ordem; se for de **compra**, o evento é descartado.
- A resposta é `EventEnvelope<Result<AssetsBlocked, AssetBlockError>>` (com `OrderId`) publicada, via outbox, na exchange `trade-simulator.portfolio.reservations` (routing keys `assets.blocked` / `assets.block.failed`). `AssetBlockError.Kind`: `InsufficientQuantity`, `AssetNotFound` ou `InvalidOrder`.
- A reserva por ordem fica na tabela `order_reservations` (`order_id`, conta, ticker, bloqueado, restante, fechada), gravada na mesma transação do `Block`.
- Eventos do Exchange (exchange `trade-simulator.exchange.order.events`, fila `trade-simulator.portfolio.exchange.events`): `OrderPartiallyFilled`/`OrderFilled` → `Withdraw` da quantidade executada (limitada ao restante); fill final, `OrderCancelled` e `OrderRejected` → `Unblock` da sobra e fechamento da reserva. Idempotência por `processed_order_events` + lease Redis.
- Evento do Exchange sem reserva ainda gravada: reentrega curta (exceção, até 5 tentativas) e depois descarte com log.
- Compra: execução de compra (`Deposit`) não faz parte deste fluxo; a compra é descartada.

Resposta a cada comando: `EventEnvelope<Result<PortfolioCommandResult, PortfolioCommandError>>`, publicada tanto no sucesso quanto na falha.

- `PortfolioCommandResult`: id do envelope do comando, `AssetId`, `AccountId`, `Ticker`, `RequestedQuantity`, `Available`, `Blocked` e `Total`.
- `PortfolioCommandError`: `AssetId` (ausente quando o ativo não existe), `AccountId`, `Ticker`, `RequestedQuantity`, `Available`, `Blocked`, `Total` e `ErrorType<T>`, onde `T : IPortfolioError` é o tipo do erro.

Rastreamento: depósito, retirada e desbloqueio **não** são correlacionados por `OrderId` na persistência; o rastreio é feito pelo `X-Correlation-Id`/CorrelationId nos logs.

### Idempotência

- Redis (container no Docker Compose): a chave é o id do envelope, o valor é `1` e o TTL é de 1 hora. Em caso de *hit* a mensagem é rejeitada.
- **Lock distribuído:** antes de processar, o consumidor adquire `lock:{id}` (`SET NX PX 30s`, valor = token do dono, liberado por compare-and-delete em Lua). Se outro worker detém o lock, a mensagem sofre *requeue* (não é descartada).
- A chave `{id}` (valor `1`, TTL 1h) só é gravada **após** o resultado ficar durável (estado persistido ou resposta de falha de negócio publicada). Falhas de infraestrutura nunca gravam a chave, então o reprocessamento é permitido sem apagar nada; erros de negócio mantêm a chave.
- **Guarda durável:** `portfolio_outbox.envelope_id` tem índice único; se a chave Redis se perder após o commit, o reprocessamento é rejeitado pelo banco (`DuplicateEnvelope`) sem aplicar duas vezes. Por isso as linhas do outbox não devem ser expurgadas.

## CQRS (modelo de leitura)

- Escrita: eventos por ativo (`asset_events`/`asset_snapshots`) e `active_assets`, que guarda apenas a versão (concorrência otimista) e a unicidade conta + ticker.
- Leitura: tabela `asset_read_model` (`asset_id`, `account_id`, `ticker`, `available`, `blocked`, `total`), projetada na mesma transação dos eventos; ativo com total zero é removido dela.
- Modelo trafegado para a UI (HTTP e SignalR): `AssetItem` com apenas `ticker`, `available`, `blocked` e `total`. Os ids ficam somente no banco.

## API HTTP (somente consulta)

- Comandos não são expostos por HTTP.
- Listagem de ativos de uma conta, **paginada em 50 itens**, ordenável por ticker, quantidade total, disponível e bloqueada.
- Consulta de um ativo por conta + ticker.
- Inclui `X-Correlation-Id`, `ProblemDetails` e erros tipados mapeados explicitamente para status HTTP.

## Interface (página `/portfolio` da UI principal, porta 4200)

- Segue `UI.md`. A conta é informada em um campo de texto (UUID v7), como em Ordens.
- Tabela de ativos com ticker, total, disponível e bloqueada; cabeçalhos ordenáveis, números à direita, paginação (50 por página, "Anterior/Próxima" e total).
- Sem formulários de operação.
- Tempo real via SignalR (hub `/hubs/portfolio`): somente sucessos são publicados, com o ativo atualizado, mesclado por `Ticker`; ativo com `Total = 0` é removido da lista. Falhas (assíncronas) não vão para a UI. Hub, com a pílula de status refletindo a conexão, retry, reassinatura e ressincronização.

## Não funcionais

- Sem limite de quantidade por ativo nem de ativos por conta (apenas o teto universal de 1.000.000 por operação).
- Operações concorrentes na mesma conta/ativo resolvidas por concorrência otimista.

## Pontos em aberto

1. Produtor dos comandos (Exchange ou Ordens), incluindo `Unblock` em rejeição e em substituição que altere a quantidade.

## Decisões da camada de aplicação

- `PortfolioCommandError.ErrorType` é o nome do tipo do erro de domínio (`IPortfolioError`); não foi criado o `ErrorType<T>` genérico porque o contrato JSON precisa de um tipo único.
- Respostas de sucesso saem pelo outbox, gravadas na mesma transação dos eventos; respostas de falha são publicadas diretamente.
- O processador tem duas entradas: comandos (`ProcessCommandAsync`, comando desconhecido gera `UnsupportedCommand`) e eventos de Ordens (`ProcessOrderEventAsync`, só `OrderSubmitted` de venda gera `Block` e resposta de reserva) e eventos do Exchange (`ProcessExchangeEventAsync`, liquidação da reserva).
- Conflito de concorrência e `AssetAlreadyExists` são repetidos até 5 vezes; depois disso é falha de infraestrutura.
- Falha ao notificar a UI não desfaz o comando.

## Plano de implementação (do mais interno ao mais externo)

1. [x] Domínio: `Quantity`, `Ticker`, `AccountId`, `AssetId`, erros, eventos e agregado `Asset` com testes.
2. [x] Aplicação: portas (`IAssetStore`, `IOutbox`, `IEventPublisher`, `IIdempotencyStore`, consultas) e casos de uso de comando.
3. [x] `Id` no `EventEnvelope` e contratos de mensagens (`Deposit`, `Withdraw`, `Block`, `Unblock`, `PortfolioCommandResult`, `PortfolioCommandError`).
4. [x] Infraestrutura: PostgreSQL (eventos, snapshot a cada 100, outbox, ativos ativos), Redis, RabbitMQ.
5. [x] API de consulta e hub SignalR.
6. [x] `Portfolio UI`, Dockerfile e Docker Compose (Redis e API).
