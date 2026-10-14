# Padrões de desenvolvimento

As diretrizes a seguir devem ser observadas durante o desenvolvimento do projeto.

## Arquitetura e organização

- A solution principal deve se chamar `TradingSimulator.slnx`. Organize a solução em **solution folders por módulo de negócio**; cada pasta de módulo representa uma fronteira de microsserviço e contém o projeto executável desse microsserviço, seus projetos de teste e os projetos/arquivos de interface relacionados. Exemplo: `/Account/` contém `TradingSimulator.Account`, `TradingSimulator.Account.Tests` e `Account UI`. Ao iniciar um novo módulo, crie uma nova solution folder e seu projeto de microsserviço independente, sem agrupar projetos de módulos distintos na mesma pasta.
- Utilize **Ports & Adapters (Hexagonal)** de forma estrita (referência: módulo Orders). Regras:
  - **Domain** é puro: sem referência a framework, serialização, banco ou mensageria.
  - **Portas de entrada (inbound)** são interfaces que expõem um manipulador (handler) de comando ou consulta (`ISubmitOrder`, `ICancelOrder`, `IQueryOrders`, `IQueryOrderHistory`), recebem comandos/valores tipados e retornam `Result<T, IError>`. Ficam no mesmo arquivo do caso de uso que as implementa.
  - **Portas de saída (driven)** (`IOrderStore`, `IOrderNotifier`, `IOrderSearch`, `IOrderHistory`) são interfaces pertencentes à aplicação: em `Common/` quando usadas por Features e Processors, ou na fatia que as usa quando só uma a usa. Contratos internos da entrega de mensagens (`IOutbox`, `ICommandPublisher`, `PendingEvent`) não são portas da aplicação e ficam em `Infrastructure/Messaging`. As implementações ficam somente em `Infrastructure/`.
  - **Handlers** (aplicação) não referenciam ASP.NET, SignalR, `System.Text.Json`, Npgsql nem RabbitMQ, e nunca dependem de `Infrastructure/`.
  - **Adaptadores de entrada** traduzem o protocolo em chamadas às portas de entrada, sem regra de negócio: endpoints HTTP (`*Endpoint.cs`, dentro da fatia) e consumidores de mensagens (`Infrastructure/Messaging`, que desserializam o `EventEnvelope<T>`, descartam mensagens malformadas e chamam a porta com valores tipados). Contratos de mensagens (`*Message`) pertencem a `Infrastructure/Messaging`.
  - **Adaptadores de saída** (`Infrastructure/Database`, `Messaging`, `Realtime`) implementam as portas de saída. Detalhes de protocolo (JSON, hub SignalR, mapeamento erro→HTTP e DTOs de resposta HTTP em `Infrastructure/Http`) nunca vazam para o domínio ou para os casos de uso.
  - O `Program.cs` é a raiz de composição: único lugar que liga portas a adaptadores.
- Implemente funcionalidades seguindo **Vertical Slices**. Uma **feature é exclusivamente um caso de uso**, e um caso de uso é um **comando** (altera estado) ou uma **consulta** (apenas lê); nada que não seja um comando ou consulta é uma feature (consumidores de mensagens, publicadores, hubs e demais integrações são adaptadores em `Infrastructure/`). Cada feature fica em sua própria pasta em `Features/`, na raiz do projeto, contendo tudo de que precisa para funcionar (endpoint, request/command, caso de uso e sua porta de entrada, portas de saída específicas e tipos de leitura). Não existem pastas separadas `Api/` e `Application/`. Não concentre vários casos de uso em um arquivo/classe genérico; e não transforme integração (ex.: eventos da bolsa) em "feature": feature é caso de uso.
- Esta organização vale para **todos os módulos** (Account, Exchange, OfferBook, Orders, Portfolio, Quotes). Organização na raiz de cada projeto de módulo:
  - `Features/<CasoDeUso>/`: fatias (`SubmitOrder`, `CancelOrder`, `ReplaceOrder`, `QueryOrders`, `QueryOrderHistory`...).
  - `Common/` (somente quando houver algo compartilhado): portas de saída e tipos de contrato usados tanto por `Features/` quanto por `Processors/` (`IOrderStore`, `IOrderNotifier`, `OrderView`; no Quotes, `Quote` e `LatestQuotes`). Nunca depende de `Features/` nem de `Processors/`.
  - `Features/Shared/`: somente o que é compartilhado entre features (`OrderModifier`, `IAccountStore`, `InstrumentItem`). Uma feature nunca depende de outra. Cada caso de uso tem seu handler e sua porta de entrada; o endpoint só faz a tradução HTTP.
  - `Domain/Entities`, `Domain/ValueObjects`, `Domain/Enums`, `Domain/Events` (o tipo-base `OrderEvent` e todos os eventos que o estendem) e os erros de domínio.
  - `Processors/<CasoDeUso>/` (o lado worker: reações a mensagens recebidas de outros módulos; Orders `FundsReservation`, `AssetReservation`, `OrderTransition`; Account `OrderFunds`; Portfolio `PortfolioCommands`; Exchange `OrderCommands`, `OfferBook`, `Quotes`; Quotes `QuoteUpdates`, `QuoteInterest`; OfferBook `OfferBookUpdates`, `OfferBookInterest`): cada pasta é um caso de uso acionado por mensagem, com seu processor, suas portas e os tipos neutros que ele recebe. Os consumidores e os `*MessageHandler` em `Infrastructure/Messaging` fazem o parse (envelope, JSON, contratos `*Message`), descartam mensagens malformadas e entregam valores tipados ao processor; os adaptadores de saída montam os envelopes. `Processors` nunca importa `Infrastructure` nem `Features`. Serviços usados só pelos processors (como `OrderTransitions`) ficam na pasta do caso de uso que os usa.
  - `Simulation/` (quando houver): geração de dados sintéticos (como a simulação de pregão do Exchange); não é domínio nem infraestrutura.
  - `Infrastructure/Database`, `Infrastructure/Messaging`, `Infrastructure/Realtime`, `Infrastructure/Http`, `Infrastructure/Serialization` (e outras pastas por função, como `Sources`, `Simulation` ou `Idempotency`, quando houver): agrupados pelo que cada componente faz.
  - Namespaces seguem as pastas (`TradingSimulator.Orders.Features.SubmitOrder`).
- Mantenha cada evento de domínio em um arquivo próprio em `Domain/Events`, com namespace `<Projeto>.Domain.Events`.
- Mantenha cada porta em seu próprio arquivo, nomeado pelo tipo da porta, quando ela for compartilhada (`IOrderStore.cs`, `IOutbox.cs`); tipos de contrato usados por essas portas (como `PendingEvent`) também têm arquivo próprio. Não agrupe múltiplas portas em um arquivo `*Ports.cs`.
- Utilize **agregados** para definir fronteiras transacionais e proteger as invariantes do domínio.
- **Nenhum módulo conhece o banco dos demais.** Apesar de haver um único container PostgreSQL, cada módulo tem seu próprio schema e seu próprio usuário (ver `database/init-schemas.sql`), com acesso apenas ao próprio schema. É proibido consultar, referenciar (chave estrangeira, view, `JOIN`) ou qualificar tabelas de outro módulo; o SQL de um módulo nunca prefixa schema alheio e a connection string de um módulo usa somente o seu usuário. Dados de outros módulos só chegam por mensagens/eventos ou chamadas a API, e, se necessários, são mantidos como cópia própria no schema do módulo. Todo novo módulo com banco deve ganhar schema e usuário próprios em `init-schemas.sql`.
- **O módulo Exchange é um serviço externo (a bolsa) e jamais pode conhecer os demais módulos** (Orders, Account, Portfolio, Quotes, OfferBook etc.): sem referência a projetos, nomes, filas, exchanges ou eventos de outros módulos, sem chamadas a suas APIs e sem ler seus bancos. Os demais módulos é que se plugam nas exchanges do Exchange, e **somente** para (a) enviar ordens/comandos e (b) receber eventos, como uma API assíncrona. O Exchange só expõe um contrato próprio e neutro (comandos de entrada e eventos de saída) e mantém todo o seu estado (livro, ordens abertas) por conta própria; qualquer necessidade de reconciliação com outro módulo (por exemplo, restaurar ordens abertas) é feita pelo consumidor via esse contrato, nunca pelo Exchange consultando-o.

Estrutura de referência (módulo Orders):

```text
TradingSimulator.Orders/
├── Program.cs
├── Common/ (IOrderStore, IOrderNotifier, OrderView e OrderChanges)
├── Domain/
│   ├── Entities/Order.cs
│   ├── ValueObjects/ (OrderId, AccountId, Ticker, RequestHash)
│   ├── Enums/ (OrderSide, OrderState, OrderType)
│   ├── Events/ (OrderEvent e eventos derivados)
│   └── OrderErrors.cs
├── Features/
│   ├── SubmitOrder/ (SendOrderEndpoint, SendOrderRequest, SubmitOrderHandler)
│   ├── CancelOrder/ (CancelOrderEndpoint, CancelOrderHandler)
│   ├── ReplaceOrder/ (ReplaceOrderEndpoint, ReplaceOrderHandler)
│   ├── QueryOrders/ (SearchOrdersEndpoint, QueryOrdersHandler, IOrderSearch)
│   ├── QueryOrderHistory/ (QueryOrderHistoryEndpoint, QueryOrderHistoryHandler, IOrderHistory)
│   └── Shared/ (OrderModifier)
├── Processors/
│   ├── FundsReservation/ (FundsReservationProcessor)
│   ├── AssetReservation/ (AssetReservationProcessor)
│   └── OrderTransition/ (OrderTransitionProcessor, OrderTransitions, ExchangeEvent)
└── Infrastructure/
    ├── Database/ (PostgresOrderStore, PostgresOrderSearch, PostgresOrderHistory, schema.sql...)
    ├── Messaging/ (consumidores/publicadores RabbitMQ, *MessageHandler, outbox, contratos *Message)
    ├── Realtime/ (OrdersHub, SignalROrderNotifier)
    ├── Http/ (OrderResponse, OrderErrorHttpResults)
    └── Serialization/
```

## Domínio e persistência

- Utilize **Event Sourcing** como estratégia de persistência.
- Não use exceções para representar erros de validação ou regras de negócio. Use a mônada `Result<T, IAccountError>` do pacote NuGet **Moonad** e propague falhas como notificações tipadas, sem persistir alterações quando o resultado for erro.
- Cada erro do domínio deve ser um `readonly struct` sem propriedades, implementando `IAccountError`, com um tipo distinto para cada validação/invariante. A API deve mapear explicitamente cada erro tipado para seu status HTTP (por exemplo, identificador inválido `400`, conta inexistente `404`, conflito `409`, violação de regra `422` e corrupção de estado `500`), sem inferir status pelo texto da mensagem.
- Exceções ficam reservadas a falhas inesperadas de infraestrutura/runtime; registre-as e retorne `500`, sem convertê-las em sucesso. Erros de parsing de protocolos nas fronteiras podem seguir o contrato do framework/serializador.
- Crie um snapshot do estado do agregado a cada 1.000 eventos/operações por conta (o limite de cada módulo pode ser diferente e é definido em seu documento de requisitos; por exemplo, 100 eventos por ativo no Portfólio). Armazene o identificador da conta, a posição do último evento incluído e os saldos disponíveis/provisionados; ao carregar a conta, reidrate o snapshot mais recente e aplique apenas eventos posteriores. Grave a atualização do snapshot na mesma transação dos eventos e do outbox que atingem o limite. O log de eventos continua sendo a fonte de verdade e não deve ser removido pelo snapshot.
- Dispare eventos de domínio por meio de uma **driven port** para notificar mudanças de estado.
- Comunique eventos de domínio por meio de um adaptador que utilize **RabbitMQ**, executado em um container Docker. A comunicação deve usar JSON.
- Toda comunicação assíncrona (eventos, comandos e respostas) deve trafegar em `EventEnvelope<T>` (o mesmo tipo criado no módulo de Ordens, com `EventType` e `Event`), nunca payloads soltos. Quando a mensageria exigir idempotência, o envelope carrega o identificador único da mensagem.
- Utilize **PostgreSQL** para persistir os eventos e o outbox. O schema de criação deve ser mantido em script SQL versionável, reutilizado pela inicialização local da aplicação e pelo container oficial do PostgreSQL.
- Persista o evento de abertura e sua entrada no outbox atomicamente em uma única instrução SQL, sem consulta prévia de existência: o identificador é gerado pela aplicação e a unicidade da abertura é protegida por uma restrição no banco. Evite round-trips redundantes nos casos de uso, sem prometer metas de latência abaixo do que medições ponta a ponta comprovem.
- Use **um único container PostgreSQL** para todos os módulos, com **um schema por módulo** (`account`, `orders`, `portfolio`, ...) e **um usuário por schema**, com leitura e escrita somente no seu próprio schema (sem acesso aos demais). Os schemas e usuários são criados por script SQL versionado (`database/init-schemas.sql`) executado na inicialização do container; cada módulo conecta com seu usuário e `Search Path` do seu schema, e seu `schema.sql` embutido usa nomes de tabela não qualificados. Módulos não compartilham tabelas: a integração entre eles ocorre somente por mensageria/API.
- Execute a API, PostgreSQL e RabbitMQ por meio do Docker Compose para o ambiente integrado; a API deve aguardar a saúde dos serviços dependentes e executar o schema SQL versionado (embutido no assembly) durante a inicialização.

## Enums

- Todo `enum` deve herdar explicitamente de `byte` (`public enum OrderSide : byte`), inclusive os privados e os de contratos de mensagens. Mantenha no máximo 256 valores por enum.

## Identificadores

- Utilize identificadores fortemente tipados em vez de `Guid` diretamente.
- Cada tipo de identificador deve ser um `readonly struct` que contenha uma propriedade `Guid`, também chamada `Guid`.
- Crie novos valores de `Guid` sempre na versão 7 (`Guid.CreateVersion7()`). A conversão de valores externos deve ser feita por uma factory que retorna `Result<Id, IAccountError>` e rejeita GUID vazio ou de qualquer versão diferente de 7; não use operadores de conversão que lancem exceções para validação.
- **Todos** os identificadores de todos os módulos (por exemplo `AccountId`, `OrderId`, `AssetId` e o identificador de envelope de mensagens) devem seguir exatamente o modelo de `AccountId`: `readonly struct`/`record struct` com propriedade `Guid`, construtor privado, factory `TryCreate(Guid)` retornando `Result<Id, I<Modulo>Error>` que rejeita GUID vazio ou diferente da versão 7, e criação de novos valores com `Guid.CreateVersion7()`.
- Não exponha uma propriedade/método `IsValid`: a validade é garantida no ponto de criação/conversão. Como `readonly struct` admite `default(T)`, adaptadores de entrada/serialização devem rejeitar explicitamente o valor padrão antes de persistir ou emitir o identificador.

## Quantidades

- Quantidades de ativos são sempre números inteiros e devem ser representadas pelo tipo `Quantity` (`readonly struct` com propriedade `long Value`), nunca por `long`/`int` soltos em domínio, comandos, eventos e contratos. A criação ocorre por factory `TryCreate` que retorna `Result<Quantity, I<Modulo>Error>` e rejeita valores menores ou iguais a zero; saldos usam `Quantity.Zero` (o `default`), e as operações do domínio rejeitam `default` como quantidade informada; limites de negócio adicionais (por exemplo, teto por operação) são validados pela regra do caso de uso, com erro tipado próprio.

## Observabilidade

- Use **OpenTelemetry** (traces e métricas). A infraestrutura compartilhada fica em `Shared/TradingSimulator.Commons/Telemetry`; cada módulo declara apenas `Infrastructure/Telemetry/<Modulo>Telemetry.cs` (`ServiceTelemetry`, `ActivitySource` e `Meter` próprios) e `<Modulo>Metrics.cs`.
- `Domain`, `Features`, `Processors` e `Common` não importam `Infrastructure` nem OpenTelemetry. Métricas de negócio são emitidas por uma porta de saída `I<Modulo>Metrics` implementada em `Infrastructure/Telemetry`; `ActivitySource` e `Meter` (BCL) são usados apenas na infraestrutura.
- Propague o `traceparent` nas mensagens: o publisher cria um span `Producer` e grava o header; o consumer cria um span `Consumer` a partir dele. Em módulos com outbox, a coluna `traceparent` guarda o contexto até a publicação. O `correlation.id` vira tag do span da requisição.
- Rótulos de métricas são **conjuntos fechados** (resultado, tipo, fila). Nunca use ids, tickers, preços, quantidades, saldos ou dados pessoais como rótulo ou tag; textos vindos de outro serviço só viram rótulo após whitelist.
- Meça backlog do outbox (`IOutboxBacklogReader` + `OutboxBacklogMonitor`), mensagens consumidas por resultado (`acked`, `requeued`, `dropped`, `failed`) e falhas de publicação.
- Cada serviço expõe `/health/live` (processo no ar) e `/health/ready` (dependências: banco, Redis, RabbitMQ, via `AddProbe`/`AddRabbitMqProbe`). O `/health` simples é mantido por compatibilidade.
- O exportador OTLP só é ativado quando `OTEL_EXPORTER_OTLP_ENDPOINT` está configurado.

## Comunicação e interface

- Desenvolva as interfaces com o usuário usando **Angular**, sempre na versão mais recente disponível.
- Siga as regras de layout, componentes e acessibilidade documentadas em `UI.md`.
- Mantenha o frontend Angular em uma pasta de projeto própria e inclua seus arquivos-fonte, testes e `package.json` na solution folder do respectivo módulo, para navegação e execução dos scripts npm pela IDE. Disponibilize scripts npm de build e testes unitários executáveis pela IDE. Não inclua `node_modules` nem artefatos de build na solução.
- Em desenvolvimento local, use uma URL relativa (`/api`) no frontend e configure o proxy do Angular para o endereço/porta do perfil de API usado pela IDE. Configure timeout e erro visível para chamadas sem resposta, sem manter indefinidamente o estado de carregamento.
- Use **Tailwind CSS** para compor a interface Angular com utilitários de estilo, mantendo layouts responsivos, estados de foco/erro/carregamento e acessibilidade básica (rótulos, estados anunciados e controles por teclado).
- Apresente claramente o saldo disponível separado do valor provisionado, mantendo visível o resumo de valores provisionados para ordens. A interface permite depósitos e saques, respeitando os limites de `REQUIREMENTS_ACCOUNT.md`; não deve oferecer formulário ou comando de provisionamento. O módulo de Ordens solicita provisionamentos por comando via mensageria, e valores provisionados não são disponíveis para saque.
- A comunicação entre o front-end e o back-end deve usar **JSON**.
- Inclua um **CorrelationId** nas requisições às APIs.
