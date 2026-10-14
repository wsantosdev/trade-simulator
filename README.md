# Trading Simulator

Simulador de negociação de ações da bolsa brasileira (B3). O usuário abre uma conta corrente, deposita dinheiro, envia ordens de compra e venda, acompanha cotações e o livro de ofertas em tempo real e vê sua carteira de ativos mudar conforme as ordens são executadas.

O projeto também é um **laboratório de arquitetura**: um sistema pequeno o bastante para ser lido por inteiro, mas construído como um conjunto de microsserviços orientados a eventos, com os padrões que se esperaria de um sistema real.

## Módulos

Cada módulo é um microsserviço .NET 10 independente, com seu próprio projeto de testes, Dockerfile e (quando precisa de persistência) seu próprio schema no PostgreSQL.

| Módulo | Porta | Responsabilidade |
| --- | --- | --- |
| **Account** | 8080 | Conta corrente: abertura, depósitos, saques, reserva (provisionamento) de saldo para ordens de compra e débito/liberação conforme as execuções. |
| **Orders** | 8081 | Ciclo de vida das ordens (envio, cancelamento, substituição, execução parcial e total) e seu histórico. |
| **Portfolio** | 8082 | Carteira de ativos por conta: posição disponível e bloqueada (ordens de venda). Usa Redis para idempotência. |
| **Exchange** | 8083 | A bolsa. Livro de ofertas com casamento de ordens, cadastro de instrumentos, histórico de cotações e simulação de pregão. Serviço externo: **não conhece nenhum outro módulo**. |
| **Quotes** | 8084 | Cotações em tempo real, via SignalR. |
| **OfferBook** | 8085 | Livro de ofertas em tempo real, via SignalR. |
| **UI** | 4200 / 4204 | Angular. App principal em `4200` (conta, ordens, carteira, cotações, livro); UI da bolsa em `4204`. |

`Shared/TradingSimulator.Commons` contém apenas o que é comum aos serviços na comunicação: `EventEnvelope<T>` e conversores JSON.

## Como funciona

1. O usuário abre uma conta e deposita fundos (**Account**).
2. Ao enviar uma ordem, **Orders** a registra como pendente e publica um comando de reserva: de **fundos** em Account (compra) ou de **ativos** em Portfolio (venda).
3. Account/Portfolio respondem com sucesso ou falha. Em caso de falha a ordem é rejeitada; em caso de sucesso, Orders a despacha para a **Exchange**.
4. A **Exchange** casa as ordens no livro e publica eventos (aceita, parcialmente executada, executada, cancelada, substituída, rejeitada).
5. **Orders** aplica os eventos ao estado da ordem; **Account** debita/libera saldo e **Portfolio** movimenta ativos a partir desses mesmos eventos.
6. A Exchange publica cotações e livro de ofertas só para tickers com interesse registrado; **Quotes** e **OfferBook** renovam periodicamente esse interesse (heartbeat) enquanto houver clientes conectados e retransmitem as atualizações por SignalR para a UI.

A comunicação entre serviços é assíncrona, por RabbitMQ.

## Padrões e técnicas

As regras completas estão em [`PATTERNS.md`](PATTERNS.md) (e as de interface em [`UI.md`](UI.md)). Em resumo:

- **Microsserviços por módulo de negócio**, com banco lógico próprio: um único PostgreSQL, mas um schema e um usuário por módulo, sem acesso cruzado.
- **Ports & Adapters (Hexagonal)** estrito: o domínio e os handlers não conhecem ASP.NET, Npgsql, RabbitMQ, SignalR nem JSON. Adaptadores de entrada (endpoints HTTP, consumidores de mensagens) chamam portas de entrada; adaptadores de saída em `Infrastructure/` implementam as portas de saída.
- **Vertical Slices**: cada caso de uso (comando ou consulta) vive em `Features/<CasoDeUso>/`, com tudo de que precisa (o lado API). As reações a mensagens de outros módulos ficam em `Processors/<CasoDeUso>/` (o lado worker), também uma pasta por caso de uso. Portas e tipos usados pelos dois lados ficam em `Common/`.
- **Domain-Driven Design**: agregados como fronteira transacional, objetos de valor com ids fortemente tipados e criação validada (`TryCreate`), eventos de domínio.
- **Event Sourcing** (Account, Orders, Portfolio): o estado é reconstruído a partir dos eventos, com snapshots; **CQRS** com read models para consultas.
- **Transactional Outbox**: eventos e respostas são gravados na mesma transação do estado e publicados por um serviço em segundo plano, garantindo entrega *at-least-once*.
- **Idempotência e concorrência otimista**: repetição de mensagens é tolerada (chave no Redis no Portfolio) e conflitos de versão são reprocessados.
- **Railway-oriented programming**: erros esperados são valores (`Result<T, TError>`, biblioteca Moonad), não exceções; cada módulo tem seu catálogo de erros e o mapeamento para HTTP fica na infraestrutura.
- **Mensageria** com envelope padrão (`EventEnvelope<T>`) e correlation id propagado do HTTP até os eventos.
- **Tempo real** com SignalR e registro de interesse sob demanda, para a bolsa publicar apenas o que alguém está acompanhando.
- **Observabilidade** com OpenTelemetry: traces propagados pelas mensagens (`traceparent`), métricas de negócio e de mensageria sem dados sensíveis, backlog do outbox e health checks de liveness/readiness. Exportação via OTLP quando `OTEL_EXPORTER_OTLP_ENDPOINT` está definido.
- Enums sempre `: byte`; namespaces seguem as pastas.

## Estrutura de um módulo

```text
TradingSimulator.<Modulo>/
├── Program.cs            raiz de composição
├── Common/               portas e contratos usados por features e processors
├── Domain/               Entities, ValueObjects, Enums, Events, erros
├── Features/<CasoDeUso>/ endpoint, handler, portas e tipos da feature (API)
├── Processors/<CasoDeUso>/ processor e tipos neutros de uma reação a mensagens (worker)
└── Infrastructure/       Database, Messaging, Realtime, Http, Serialization, Telemetry...
```

## Executar localmente

### Pré-requisitos

- Docker com Docker Compose
- Node.js e npm (somente para rodar a UI em modo de desenvolvimento)
- .NET 10 SDK (somente para compilar e testar fora do Docker)

### Subir tudo com Docker

Na raiz do repositório:

```sh
docker compose up --build
```

Isso inicia PostgreSQL, Redis, RabbitMQ, os seis serviços e a UI. Os schemas e usuários do PostgreSQL são criados por `database/init-schemas.sql` e as tabelas são criadas pelos próprios serviços na inicialização.

| Serviço | URL |
| --- | --- |
| UI principal | http://localhost:4200 |
| UI da Exchange | http://localhost:4204 |
| Account / Orders / Portfolio | http://localhost:8080 / 8081 / 8082 |
| Exchange / Quotes / OfferBook | http://localhost:8083 / 8084 / 8085 |
| RabbitMQ (gestão) | http://localhost:15672 (`account` / `account-local`) |

As credenciais acima são **somente para desenvolvimento local**. Cada serviço expõe `GET /health`, `GET /health/live` e `GET /health/ready`.

### Carregar os dados da bolsa

A Exchange precisa de instrumentos e cotações históricas da B3. Os arquivos não são versionados; baixe-os do site da B3 e importe-os pela linha de comando (de propósito não existe endpoint HTTP para isso):

- **Instrumentos** (arquivo `BVBG.028`, XML): `dotnet run --project Exchange/TradingSimulator.Exchange -- --import-instruments <caminho do XML>`
- **Cotações históricas** (arquivo `COTAHIST`, TXT): `dotnet run --project Exchange/TradingSimulator.Exchange -- --import-quotes <caminho do TXT>`

Para executar a importação contra o PostgreSQL do Docker, use a string de conexão do Exchange (`ConnectionStrings__Exchange`) apontando para `localhost:5432`.

Para simular o pregão fora do horário de mercado, defina `EXCHANGE_SESSION_CLOCK_OFFSET` (ex.: `EXCHANGE_SESSION_CLOCK_OFFSET=-06:00:00 docker compose up`) para deslocar o relógio da sessão.

### UI em modo de desenvolvimento

Com os serviços de pé no Docker:

```sh
cd UI
npm install
npm start                  # app principal em http://localhost:4200
npm run start:exchange     # UI da Exchange em http://localhost:4204
```

O servidor de desenvolvimento faz proxy de `/api` e `/hubs` para os serviços (veja `proxy.main.conf.json`).

### Testes

```sh
dotnet test TradingSimulator.slnx     # serviços .NET
cd UI && npm test                     # UI
```

## API (resumo)

| Módulo | Endpoints |
| --- | --- |
| Account | `POST /api/accounts`, `GET /api/accounts/{id}`, `POST /api/accounts/{id}/deposits`, `/withdrawals`, `/reservations` |
| Orders | `POST /api/orders`, `GET /api/orders`, `POST /api/orders/{id}/cancel`, `/replace`, `GET /api/orders/{id}/history` |
| Portfolio | `GET /api/assets`, `GET /api/assets/{ticker}` |
| Exchange | `GET /api/instruments`, `GET /api/instruments/{ticker}`, `GET /api/quotes/{ticker}` |
| Quotes | `GET /api/quotes/{ticker}/latest` (+ hub SignalR) |
| OfferBook | `GET /api/offerbook/{ticker}` (+ hub SignalR) |
