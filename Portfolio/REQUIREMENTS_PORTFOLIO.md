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
- Sem snapshots: o ativo é reidratado sempre pelos eventos.
- Eventos de domínio (um arquivo por evento, junto à fatia): `AssetDeposited`, `AssetBlocked`, `AssetWithdrawn` e `AssetUnblocked`. Não existe `AssetRemoved`: a retirada que zera o ativo gera apenas `AssetWithdrawn`, e a mensagem/consulta informa `Total = 0`, o que faz a UI remover o ativo da lista. O ativo com `Total = 0` deixa de existir para novos comandos e um novo depósito cria outro `AssetId`.
- Como o comando de depósito informa conta + ticker (e não o `AssetId`), uma tabela de ativos ativos com índice único (conta, ticker) localiza o ativo e protege contra dois primeiros depósitos concorrentes.
- Controle de concorrência otimista por versão do stream, com retry.
- PostgreSQL compartilhado: schema `portfolio` e usuário `portfolio` (ver `PATTERNS.md`). Schema SQL versionado e embutido no assembly.

## Operações (HTTP síncrono)

O Portfólio não recebe comandos por fila. Toda escrita é HTTP síncrona, uma fatia por operação (`Features/`), e o Portfólio desconhece ordens: quem reserva usa um `reservationId` opaco.

- `POST /api/assets/deposits` `{accountId, ticker, quantity}` soma à quantidade disponível (o primeiro depósito abre o ativo). Dados inválidos → `400`. Conflitos de versão são repetidos até 5 vezes.
- `POST /api/assets/reservations` `{reservationId, accountId, ticker, quantity}` bloqueia a quantidade (`Block`), sob lock consultivo por ativo. Idempotente por `reservationId`; quantidade insuficiente → `422` (`reason`); ativo inexistente → `404`; dados inválidos → `400`; mesmo id com outros dados → `409`.
- A fila `trade-simulator.portfolio.order.events` consome os eventos de ordem publicados por Orders (exchange `trade-simulator.orders.events`); o id da ordem é o `reservationId`. `OrderPartiallyFilled` retira (`Withdraw`) a quantidade executada (limitada ao restante), idempotente pelo id do envelope (pelo estado da entidade); `OrderFilled` faz o mesmo e desbloqueia a sobra (`Unblock`); `OrderCancelled`, `OrderRejected` e `OrderScheduleCancelled` liberam o restante bloqueado. Reservas desconhecidas ou já encerradas são ignoradas.
- Cada reserva é o evento `AssetBlocked` (com `reservationId`); o restante bloqueado é recalculado pela entidade ao reaplicar os eventos, e a reserva é localizada por consulta JSONB. O Portfólio não consome eventos de Ordens nem do Exchange.
- Não há retirada direta: a venda passa por reserva → liquidação.

## Mensageria interna (RabbitMQ)

- Cada evento do ativo é gravado no `event_outbox` na mesma transação e publicado no exchange `trade-simulator.portfolio.events` (routing key `asset.changed`). Um consumidor do próprio módulo (`AssetProjectionConsumer`) atualiza o read model no Mongo e notifica a UI.
- Idempotência: a entidade `Asset` a deriva dos próprios eventos (reserva repetida e `operationId` já liquidado são ignorados). Lock consultivo por ativo (`pg_advisory_xact_lock`) nas escritas. Não há Redis neste módulo.

## CQRS (modelo de leitura)

- Escrita: apenas os eventos por ativo (`events` e `event_outbox`, schema `portfolio`). O id do ativo é derivado de conta + ticker (`AssetId.For`), então a unicidade vem do índice `(stream_id, version)`. O repositório só lê e grava eventos; as regras de reserva e liquidação ficam na entidade `Asset`.
- Leitura: coleção `assets` do **MongoDB** (`_id`, `accountId`, `ticker`, `available`, `blocked`, `total`), alimentada pelo `AssetProjectionHandler`, acionado por cada evento do outbox (`asset.changed`) (eventualmente consistente); ativo com total zero é removido dela.
- Modelo trafegado para a UI (HTTP e SignalR): `AssetItem` com apenas `ticker`, `available`, `blocked` e `total`. Os ids ficam somente no banco.

## API HTTP (consulta)

- Listagem de ativos de uma conta, **paginada em 50 itens**, ordenável por ticker, quantidade total, disponível e bloqueada.
- Consulta de um ativo por conta + ticker.
- Inclui `X-Correlation-Id`, `ProblemDetails` e erros tipados mapeados explicitamente para status HTTP.

## Interface (página `/portfolio` da UI principal, porta 4200)

- Segue `UI.md`. A conta é informada em um campo de texto (UUID v7), como em Ordens.
- Tabela de ativos com ticker, total, disponível e bloqueada; cabeçalhos ordenáveis, números à direita, paginação (50 por página, "Anterior/Próxima" e total).
- Sem formulários de operação.
- Tempo real via SignalR (hub `/hubs/portfolio`): somente sucessos são publicados, com o ativo atualizado, mesclado por `Ticker`; ativo com `Total = 0` é removido da lista.  Hub, com a pílula de status refletindo a conexão, retry, reassinatura e ressincronização.

## Não funcionais

- Sem limite de quantidade por ativo nem de ativos por conta (apenas o teto universal de 1.000.000 por operação).
- Operações concorrentes na mesma conta/ativo resolvidas por concorrência otimista.

## Pontos em aberto

Nenhum.

## Decisões da camada de aplicação

- Cada operação é um handler com sua porta de entrada: `DepositAsset` e `ReserveAssets` são features HTTP; `SettleReservation` e `ReleaseReservation` são processors acionados pelos eventos de ordem; nenhuma depende de outra.
- Conflito de concorrência e `AssetAlreadyExists` são repetidos até 5 vezes; depois disso é falha de infraestrutura.
- Falha ao notificar a UI não desfaz a operação.

## Plano de implementação (do mais interno ao mais externo)

1. [x] Domínio: `Quantity`, `Ticker`, `AccountId`, `AssetId`, erros, eventos e agregado `Asset` com testes.
2. [x] Aplicação: portas (`IAssetStore`, `IAssetNotifier`, consultas) e casos de uso de comando.
3. [x] Operações HTTP: depósito e reservas.
4. [x] Infraestrutura: PostgreSQL (eventos e outbox), RabbitMQ.
5. [x] API de consulta e hub SignalR.
6. [x] `Portfolio UI`, Dockerfile e Docker Compose.
