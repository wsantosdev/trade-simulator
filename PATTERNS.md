# Padrões de desenvolvimento

As diretrizes a seguir devem ser observadas durante o desenvolvimento do projeto.

## Arquitetura e organização

- A solution principal deve se chamar `TradingSimulator.slnx`. Organize a solução em **solution folders por módulo de negócio**; cada pasta de módulo representa uma fronteira de microsserviço e contém o projeto executável desse microsserviço, seus projetos de teste e os projetos/arquivos de interface relacionados. Exemplo: `/Account/` contém `TradingSimulator.Account`, `TradingSimulator.Account.Tests` e `Account UI`. Ao iniciar um novo módulo, crie uma nova solution folder e seu projeto de microsserviço independente, sem agrupar projetos de módulos distintos na mesma pasta.
- Utilize **Ports & Adapters (Hexagonal)** para a comunicação com o mundo externo. As dependências externas devem ser acessadas por meio de portas e adaptadores.
- Implemente funcionalidades seguindo **Vertical Slices**: cada caso de uso da API deve ficar em sua própria pasta, agrupando endpoint, contrato de entrada e lógica específica da funcionalidade. Não concentre endpoints de vários casos de uso em um arquivo/classe genérico.
- Mantenha operações distintas em fatias distintas. Por exemplo, depósitos, saques e provisionamentos devem ficar em pastas separadas (como `Api/Features/Deposits`, `Api/Features/Withdrawals` e `Api/Features/Reservations`), cada uma com seu próprio endpoint e request.
- Mantenha cada evento de domínio em um arquivo próprio e junto à fatia que origina a mudança: `AccountOpened` em `Api/Features/AccountCreation`, `MoneyDeposited` em `Api/Features/Deposits`, `MoneyWithdrawn` em `Api/Features/Withdrawals` e `FundsReserved` em `Api/Features/Reservations`. Use namespaces de domínio por funcionalidade; o tipo-base compartilhado `AccountEvent` permanece em `Domain/AccountEvent.cs`.
- Compartilhe somente contratos/utilitários realmente comuns entre fatias, mantendo-os fora das pastas de funcionalidades (por exemplo, o DTO de resposta de conta em `Api/AccountResponse.cs`). O ponto de composição registra cada fatia sem absorver sua implementação.
- Em `Application`, mantenha cada porta em seu próprio arquivo, nomeado pelo tipo da porta (por exemplo, `IAccountStore.cs`, `IOutbox.cs` e `IEventPublisher.cs`). Tipos de contrato compartilhados usados por essas portas, como `PendingEvent`, também devem ter arquivo próprio; não agrupe múltiplas portas em um arquivo `*Ports.cs`.
- Utilize **agregados** para definir fronteiras transacionais e proteger as invariantes do domínio.
- **Nenhum módulo conhece o banco dos demais.** Apesar de haver um único container PostgreSQL, cada módulo tem seu próprio schema e seu próprio usuário (ver `database/init-schemas.sql`), com acesso apenas ao próprio schema. É proibido consultar, referenciar (chave estrangeira, view, `JOIN`) ou qualificar tabelas de outro módulo; o SQL de um módulo nunca prefixa schema alheio e a connection string de um módulo usa somente o seu usuário. Dados de outros módulos só chegam por mensagens/eventos ou chamadas a API, e, se necessários, são mantidos como cópia própria no schema do módulo. Todo novo módulo com banco deve ganhar schema e usuário próprios em `init-schemas.sql`.
- **O módulo Exchange é um serviço externo (a bolsa) e jamais pode conhecer os demais módulos** (Orders, Account, Portfolio, Quotes, OfferBook etc.): sem referência a projetos, nomes, filas, exchanges ou eventos de outros módulos, sem chamadas a suas APIs e sem ler seus bancos. Os demais módulos é que se plugam nas exchanges do Exchange, e **somente** para (a) enviar ordens/comandos e (b) receber eventos, como uma API assíncrona. O Exchange só expõe um contrato próprio e neutro (comandos de entrada e eventos de saída) e mantém todo o seu estado (livro, ordens abertas) por conta própria; qualquer necessidade de reconciliação com outro módulo (por exemplo, restaurar ordens abertas) é feita pelo consumidor via esse contrato, nunca pelo Exchange consultando-o.

Estrutura de referência para as fatias HTTP do módulo de conta:

```text
TradingSimulator.Account/Api/
├── AccountResponse.cs
└── Features/
    ├── AccountCreation/
    │   ├── AccountOpened.cs
    │   └── OpenAccountEndpoint.cs
    ├── AccountQuery/
    │   └── GetAccountEndpoint.cs
    ├── Deposits/
    │   ├── DepositEndpoint.cs
    │   └── MoneyDeposited.cs
    ├── Reservations/
    │   ├── ReserveFundsEndpoint.cs
    │   └── FundsReserved.cs
    └── Withdrawals/
        ├── WithdrawFundsEndpoint.cs
        └── MoneyWithdrawn.cs

TradingSimulator.Account/Domain/
└── AccountEvent.cs

TradingSimulator.Account/Application/
├── IAccountStore.cs
├── IEventPublisher.cs
├── IOutbox.cs
└── PendingEvent.cs
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

## Identificadores

- Utilize identificadores fortemente tipados em vez de `Guid` diretamente.
- Cada tipo de identificador deve ser um `readonly struct` que contenha uma propriedade `Guid`, também chamada `Guid`.
- Crie novos valores de `Guid` sempre na versão 7 (`Guid.CreateVersion7()`). A conversão de valores externos deve ser feita por uma factory que retorna `Result<Id, IAccountError>` e rejeita GUID vazio ou de qualquer versão diferente de 7; não use operadores de conversão que lancem exceções para validação.
- **Todos** os identificadores de todos os módulos (por exemplo `AccountId`, `OrderId`, `AssetId` e o identificador de envelope de mensagens) devem seguir exatamente o modelo de `AccountId`: `readonly struct`/`record struct` com propriedade `Guid`, construtor privado, factory `TryCreate(Guid)` retornando `Result<Id, I<Modulo>Error>` que rejeita GUID vazio ou diferente da versão 7, e criação de novos valores com `Guid.CreateVersion7()`.
- Não exponha uma propriedade/método `IsValid`: a validade é garantida no ponto de criação/conversão. Como `readonly struct` admite `default(T)`, adaptadores de entrada/serialização devem rejeitar explicitamente o valor padrão antes de persistir ou emitir o identificador.

## Quantidades

- Quantidades de ativos são sempre números inteiros e devem ser representadas pelo tipo `Quantity` (`readonly struct` com propriedade `long Value`), nunca por `long`/`int` soltos em domínio, comandos, eventos e contratos. A criação ocorre por factory `TryCreate` que retorna `Result<Quantity, I<Modulo>Error>` e rejeita valores menores ou iguais a zero; saldos usam `Quantity.Zero` (o `default`), e as operações do domínio rejeitam `default` como quantidade informada; limites de negócio adicionais (por exemplo, teto por operação) são validados pela regra do caso de uso, com erro tipado próprio.

## Comunicação e interface

- Desenvolva as interfaces com o usuário usando **Angular**, sempre na versão mais recente disponível.
- Siga as regras de layout, componentes e acessibilidade documentadas em `UI.md`.
- Mantenha o frontend Angular em uma pasta de projeto própria e inclua seus arquivos-fonte, testes e `package.json` na solution folder do respectivo módulo, para navegação e execução dos scripts npm pela IDE. Disponibilize scripts npm de build e testes unitários executáveis pela IDE. Não inclua `node_modules` nem artefatos de build na solução.
- Em desenvolvimento local, use uma URL relativa (`/api`) no frontend e configure o proxy do Angular para o endereço/porta do perfil de API usado pela IDE. Configure timeout e erro visível para chamadas sem resposta, sem manter indefinidamente o estado de carregamento.
- Use **Tailwind CSS** para compor a interface Angular com utilitários de estilo, mantendo layouts responsivos, estados de foco/erro/carregamento e acessibilidade básica (rótulos, estados anunciados e controles por teclado).
- Apresente claramente o saldo disponível separado do valor provisionado, mantendo visível o resumo de valores provisionados para ordens. A interface permite depósitos e saques, respeitando os limites de `REQUIREMENTS_ACCOUNT.md`; não deve oferecer formulário ou comando de provisionamento. O módulo de Ordens solicita provisionamentos por comando via mensageria, e valores provisionados não são disponíveis para saque.
- A comunicação entre o front-end e o back-end deve usar **JSON**.
- Inclua um **CorrelationId** nas requisições às APIs.
