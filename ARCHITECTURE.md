# Arquitetura

Visão geral da solução. As regras de código estão em [`PATTERNS.md`](PATTERNS.md); a visão de produto e a execução em [`README.md`](README.md).

## Visão de contexto

```
                        ┌──────────────────────── UI (Angular) ────────────────────────┐
                        │  app principal :4200 · home broker :4201 · Exchange :4204     │
                        └───────┬───────────────┬──────────────┬───────────────┬────────┘
                          HTTP  │          HTTP │        HTTP  │      SignalR  │
                        ┌───────▼──┐      ┌─────▼────┐   ┌─────▼─────┐   ┌─────▼─────┐ ┌──────────┐
                        │ Account  │      │  Orders  │   │ Portfolio │   │  Quotes   │ │ OfferBook│
                        │  :8080   │      │  :8081   │   │   :8082   │   │  :8084    │ │  :8085   │
                        └───┬──────┘      └─┬──────┬─┘   └─────┬─────┘   └─────▲─────┘ └────▲─────┘
        reserva de fundos   ▲  HTTP síncrono│      │ HTTP      ▲                │ eventos     │
        (gate Redis)        └───────────────┘      └───────────┘                │ de mercado  │
                                               │ comando (RabbitMQ)             │             │
                                          ┌────▼─────────────────────────────────┴─────────────┴──┐
                                          │                  Exchange :8083                        │
                                          │  livro de ofertas · casamento · cotações · pregão 24/7 │
                                          └────────────────────────────────────────────────────────┘

Infraestrutura: PostgreSQL (eventos) · MongoDB (read models) · Redis (gate/idempotência) · RabbitMQ (mensageria)
```

## Serviços

| Serviço | Papel | Persistência |
| --- | --- | --- |
| **Account** | Conta corrente: depósito, saque, reserva/débito/liberação de saldo. | Postgres (schema `account`), Redis |
| **Orders** | Ciclo de vida das ordens e orquestração do fluxo. | Postgres (`orders`), Mongo (read model) |
| **Portfolio** | Posição de ativos por conta (disponível/bloqueada). | Postgres (`portfolio`), Mongo, Redis |
| **Exchange** | A bolsa; **não conhece nenhum outro módulo**. Livro, casamento, instrumentos, cotações. Não usa Event Sourcing. | Postgres (`exchange`) |
| **Quotes / OfferBook** | Retransmitem cotações e livro por SignalR; sem banco. | — |
| **UI** | Três aplicações Angular. | — |

Bibliotecas compartilhadas em `Shared/`: `TradeSimulator.Commons` (envelope de mensagens, conversores JSON, telemetria), `TradeSimulator.EventSourcing` (`IEvent`, `EventSourcedEntity`, `EventStore`) e `TradeSimulator.Testing` (fixtures de integração).

## Estrutura interna de cada serviço

Ports & Adapters com Vertical Slices:

- `Domain/` — agregados, objetos de valor, eventos e erros; sem dependência de infraestrutura.
- `Features/<CasoDeUso>/` — lado API (endpoint + handler).
- `Processors/<CasoDeUso>/` — lado worker (reação a mensagens).
- `Common/` — portas e tipos compartilhados pelos dois lados.
- `Infrastructure/` — adaptadores de saída (Postgres, Mongo, Redis, RabbitMQ, telemetria).

Erros esperados são valores (`Result<T, TError>`); o mapeamento para HTTP fica na infraestrutura.

## Dados

- **PostgreSQL** único, com **um schema por serviço** e um usuário (`tradesimulator`) com acesso a todos. O código usa SQL não qualificado e o `Search Path` do serviço. Nenhum serviço consulta o schema de outro.
- Em Account, Orders e Portfolio, cada schema tem:
  - `events` — log append-only (`id`, `stream_id`, `event_type`, `version`, `occurred_at`, `payload` jsonb), único em `(stream_id, version)`, que dá a concorrência otimista.
  - `snapshots` — estado do agregado para reidratação (Account: a cada 10 eventos), com `version` e `updated_at`.
  - Account: `account_balances` — read model de saldo (conta, saldo livre e bloqueado), gravado na mesma transação dos eventos (imediatamente consistente).
  - `event_outbox` — mensagens pendentes de publicação.
  - Tabelas auxiliares do serviço (`order_requests`, `order_reservations`, `active_assets`, `processed_*`, `response_outbox`).
- Timestamps são sempre UTC (`timestamptz`); só a UI converte para o fuso local.
- **MongoDB** guarda os **read models** (CQRS). Cada evento de um agregado vai para o *outbox* (mesma transação do append) e, pela fila, chega a um *handler* do próprio serviço que reconstrói a visão e a grava com guarda de versão. **Não há polling do stream.** **Nenhuma event store tem métodos de read model.**
- **Redis** serve de gate atômico (script Lua) para reserva de saldo e de chave de idempotência/lease no Portfolio.

## Event Sourcing

`EventSourcedEntity` mantém `Version` e `UncommittedEvents`. O `EventStore` compartilhado serializa o evento em JSON, faz o append na tabela `events` dentro da transação do serviço e relê o stream por versão; os tipos são descobertos por varredura de assembly. Cada serviço grava na mesma transação o evento, a linha de outbox e, periodicamente, o snapshot.

## Comunicação

1. **Síncrona (HTTP)** — validação de ordens: `POST /api/orders` grava a ordem e reserva fundos (Account, gate Redis + Postgres como fonte de verdade) ou ativos (Portfolio, lock otimista por versão do stream). Sem saldo/posição: `422`; serviço indisponível: `503`; falha no despacho libera a reserva. A reserva é idempotente por `orderId`.
2. **Assíncrona (RabbitMQ)** — comandos para a Exchange e eventos de volta, em envelope padrão (`EventEnvelope<T>`) com correlation id e `traceparent`. O **Transactional Outbox** garante entrega *at-least-once*; consumidores são idempotentes.
3. **Tempo real (SignalR)** — Quotes e OfferBook registram interesse por ticker (heartbeat) na Exchange, que publica apenas o que alguém acompanha.

## Fluxo de uma ordem

1. Orders registra a ordem (`OrderSubmitted`, idempotente por `request_hash`) e reserva fundos/ativos de forma síncrona; se falhar, a ordem é rejeitada e não aparece no painel.
2. Orders despacha o comando à Exchange via outbox.
3. A Exchange casa no livro e publica eventos (aceita, executada parcial/total, cancelada, substituída, rejeitada).
4. Orders atualiza o estado; Account debita/libera saldo e Portfolio movimenta ativos a partir dos mesmos eventos.
5. Os handlers de projeção (disparados pelos eventos do outbox) atualizam o MongoDB e a UI consulta os read models.

## Implantação e observabilidade

- `compose.yaml` sobe Postgres, MongoDB, Redis, RabbitMQ, os seis serviços e a UI; `database/init-schemas.sql` cria o usuário e os schemas, e cada serviço aplica seu `schema.sql` embutido (idempotente, com migração) ao iniciar.
- OpenTelemetry: traces propagados pelas mensagens, métricas de negócio e de backlog do outbox, health checks de liveness/readiness; exportação via OTLP quando configurada.
