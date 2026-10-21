# Requisitos do módulo de ordens

> Documento em construção, complementado a cada resposta da entrevista de levantamento.

## Escopo

- O módulo de Ordens trata do ciclo de vida das ordens de negociação, além da boleta de negociação.

## Tipos de ordem

- Devem existir ordens **a mercado** e ordens **limitadas**.
- Não haverá ordens stop nem stop-limit.

## Lados da ordem

- O módulo deve permitir ordens de **compra** e de **venda**.

## Validade

- Ordens não possuem prazo de validade: todas são *good till cancelled* (GTC).
- Uma ordem permanece ativa até ser totalmente executada ou cancelada.

## Ativos

- Neste momento, qualquer ativo (ticker) pode ser negociado, sem validação de existência.
- Um módulo futuro de cadastro de instrumentos será responsável por validar e descrever os ativos.

## Preços e cotações

- As cotações virão de um módulo de Cotações, a ser implementado no futuro; por ora o módulo de Ordens não depende delas.
- O preço limite é livre: a boleta não o valida, exceto pelo fato de que deve ser **positivo** (maior que zero; zero e negativos são rejeitados).

## Venda coberta

- A venda deve ser **sempre coberta**; venda a descoberto não é permitida.
- O controle de posição (quantidade de ativos em custódia) será responsabilidade de um módulo futuro de Carteira (*Portfolio*).

## Quantidade

- A quantidade é sempre um **número inteiro** maior que zero; não há quantidades fracionárias.
- O lote padrão é **1** para todos os ativos.
- Não há limite máximo de quantidade nem de valor financeiro por ordem neste momento; esses limites serão implementados no futuro.

## Execução

- Uma ordem pode ser **executada parcialmente**; a quantidade executada acumula até a quantidade total.
- Este módulo apenas **registra** as ordens; o *matching* é responsabilidade do módulo **Exchange**.
- O módulo de Ordens deve receber do Exchange as execuções (totais ou parciais) para atualizar a ordem.

## Estados da ordem

- Estados: **PendingDispatching** (Despacho pendente), **PendingNew** (Pendente), **New** (Nova), **Parcialmente executada**, **Executada**, **Cancelada** e **Rejeitada**.
- Agendamento: com o mercado fechado (10:00–18:00 Brasília; deslocável por `Simulation:SessionClockOffset`, igual ao da Exchange) a ordem, após a reserva de fundos/ativos, vai de **PendingDispatching** para **ScheduledDispatching** (*Agendada*) via **OrderScheduled** (não publicado). A Exchange não conhece agendamento e nunca recebe a ordem nesse estado. Um serviço em segundo plano despacha as agendadas ao abrir o mercado (→ **PendingNew**, fluxo normal). Cancelar uma ordem agendada é local: **OrderScheduleCancelled** (publicado; Account e Portfolio o consomem para liberar os bloqueios) leva a **Cancelled**. Substituição não é permitida enquanto agendada.
- Vocabulário: **Submitted = ação do cliente; Dispatched = ação do sistema.** Toda ordem nasce com o evento **OrderSubmitted** em **PendingDispatching** (Despacho pendente); só após a validação interna (Conta/Portfólio) o sistema grava **OrderDispatched** e ela passa a **PendingNew**; vira **New** somente ao receber o *ack* do Exchange.
- Também existem os estados transitórios **PendingCancel** e **PendingReplace**.
- Novos estados poderão ser adicionados no futuro.

## Alteração e cancelamento

- O usuário pode **cancelar** uma ordem.
- O usuário pode **alterar o preço e a quantidade** da parte ainda não executada da ordem.
- É possível **cancelar uma ordem parcialmente executada**; apenas o saldo remanescente é cancelado e o executado é mantido.

## Validação interna: provisionamento de saldo (compra) e bloqueio de ativos (venda)

- O módulo de Ordens **não solicita** o provisionamento: a Conta escuta o evento **OrderSubmitted** (compras; vendas são descartadas) e responde com `EventEnvelope<Result<FundsReserved, FundsReservationError>>` no exchange `trading-simulator.account.reservations` (fila durável `trading-simulator.orders.account.events`, binding `#`).
- O Portfólio escuta **OrderSubmitted** (vendas; compras são descartadas), bloqueia a quantidade do ativo e responde com `EventEnvelope<Result<AssetsBlocked, AssetBlockError>>` no exchange topic durável `trading-simulator.portfolio.reservations` (routing key `assets.blocked` / `assets.block.failed`; fila durável do módulo `trading-simulator.orders.portfolio.events`). `AssetBlockError.kind` (texto) = `InsufficientQuantity`, `AssetNotFound` ou `InvalidOrder`.
- Sucesso (`FundsReserved` ou `AssetsBlocked`) com a ordem em **PendingDispatching**: grava **OrderDispatched** (→ **PendingNew**) e o publica pela outbox no exchange `trading-simulator-order-commands`; só então o Exchange vê a ordem. Sucesso duplicado ou para ordem em outro estado/desconhecida: ignorado com log (ack).
- Erro (`FundsReservationError` ou `AssetBlockError`; `kind` da Conta = `InsufficientFunds`, `AccountNotFound`, `PriceUnavailable` ou `InvalidOrder`) com a ordem em **PendingDispatching**: **Rejected** localmente (motivo = `kind`, gravado em `OrderRejected`). Nada é publicado ao Exchange (a ordem nunca foi despachada) e **não** há OrderCancelRequested. Erro para ordem em outro estado é ignorado com log.
- Ordem **limitada**: o valor a provisionar é **quantidade × preço limite**.
- Ordem **a mercado**: o único parâmetro informado é a **quantidade**; a Conta calcula o valor a partir da melhor oferta de venda do OfferBook com margem de 5%.

## Integração com o Exchange

- A comunicação com o Exchange é feita por **eventos via RabbitMQ**, com outbox, como no módulo de Conta.
- O envio da ordem é **assíncrono**: fica em PendingDispatching até a validação interna, depois em PendingNew até o *ack* do Exchange.

## Dados da ordem

- Quantidade executada, **preço médio** de execução e **timestamps** são dados essenciais da ordem.

## Prevenção de ordem duplicada

- Deve ser calculado um **hash da ordem** a partir de: id da conta, ticker, quantidade, preço e timestamp do envio.
- Uma ordem cujo hash já exista não deve ser registrada novamente.
- O envio de ordem duplicada funciona como **idempotência**: retorna o **resultado da ordem anterior** (se foi 201, responde 201; se foi outro status, responde esse outro).
- O hash é gerado na **boleta, no clique do botão de envio**; o timestamp é fixado nesse momento.
- O botão de envio deve ser **desabilitado** ao enviar a ordem e **reabilitado quando houver resposta do servidor** (sucesso 201 ou erro).

## Alteração (replace) e concorrência

- Ao alterar uma ordem, ela **perde a prioridade** no livro do Exchange.
- A alteração só passa a valer após um ***execution report*** do Exchange.
- Se uma **execução chegar durante um cancelamento**, o cancelamento deve ser evitado; o mesmo vale para o *replace*.
- Enquanto aguardam o Exchange, a ordem fica em **PendingCancel** (cancelamento) ou **PendingReplace** (alteração).
- "Evitar" significa que o pedido de cancelamento/replace é **rejeitado** e a execução **prossegue normalmente**.

## Interface (UI)

- A UI possui a **boleta**, a **lista de ordens** e um **modal de histórico**.
- Na lista de ordens, **à esquerda**, antes do ID, há um ícone para **ver o histórico** da ordem.
- Ao clicar no **ID da ordem**, abre-se um modal com o histórico da ordem.
- Ao final da linha, **à direita**, ficam os ícones das ações possíveis: **cancelar** e **editar** (replace).
- Colunas da lista, em ordem: ícone de histórico, **ID**, **Ticker**, **Lado**, **Tipo**, **Quantidade**, **Preço**, **Preço médio**, **Estado**, **Quantidade restante**, **Timestamp do último negócio**, ícone de cancelamento e ícone de replace.
- Os ícones de **cancelar** e **editar** só ficam habilitados para ordens em **New** ou **Parcialmente executada**; nos demais estados ficam desabilitados.
- A lista mostra **somente as ordens da conta informada na boleta**.
- Filtros: **ativo (ticker)**, **estado**, **lado** e **tipo**.
- Ordenação por **qualquer campo** da lista.
- Paginação de **10 ordens** por página.
- **Cancelar** abre um **diálogo de confirmação**.
- **Editar** carrega na boleta a **quantidade restante** da ordem e o **preço da última execução** da própria ordem (ou, se ainda não houve execução, o **preço original de envio**).
- O modal de histórico mostra os eventos da ordem (criação, ack, execuções parciais, replace, cancelamento) com data/hora, quantidade e preço; o ícone e o clique no ID abrem o **mesmo modal**.
- A lista é atualizada em **tempo real via WebSocket**, usando **SignalR**.
- A UI conecta ao **hub SignalR do módulo de Ordens** (rota sugerida: `/hubs/orders`), que recebe do Exchange, via **eventos RabbitMQ**, as atualizações de estado das ordens e as repassa à UI.

## Identificação da conta

- A conta é **informada na requisição**; a boleta deve ter um **campo para o número da conta**, que por ora pode ser colado do módulo de Conta.
- No futuro o número da conta será obtido do **token JWT**.

## Rejeições

- Por ora **não há rejeição de ordens**: não existe validação (saldo, posição etc.) nem Exchange para rejeitar; toda ordem recebida é **acatada**.
- O estado Rejeitada existe, mas não é utilizado por enquanto.
- Permanecem válidas as validações básicas da boleta (quantidade inteira > 0, preço limite > 0).

## API

- Todas as **ações** (criar, alterar, cancelar) usam **POST**.
- Todas as **consultas** (lista, histórico) usam **GET**.
- A criação responde **201 (Created)**.
- Rotas: `POST /api/orders` (criar), `POST /api/orders/{id}/cancel`, `POST /api/orders/{id}/replace`, `GET /api/orders` (filtros por ticker, estado, lado e tipo; ordenação; paginação de 10) e `GET /api/orders/{id}/history`.
- Códigos: **404** ordem inexistente, **409** conflito, **422** estado que não permite a ação.
- Cada requisição inclui um **CorrelationId**, conforme `PATTERNS.md`.
- Os erros seguem `PATTERNS.md`: cada erro de domínio é tipado e mapeado explicitamente para seu status HTTP.

## Persistência

- **PostgreSQL** com **Event Sourcing** (fonte de verdade) e **CQRS**.
- O *read model* (CQRS) consolida as informações da ordem, inclusive o **timestamp da última ação** executada sobre ela (envio, execução, cancelamento, rejeição, replace etc.).

## Identificador da ordem

- O ID da ordem segue o padrão de identificadores do `PATTERNS.md` (`OrderId`): `readonly struct` com propriedade `Guid` versão 7, factory com `Result` rejeitando GUID vazio ou de outra versão — comportamento idêntico ao `AccountId`.
- Todos os IDs de todos os módulos devem seguir esse padrão.
- O ID é distinto do **hash** de prevenção de duplicidade.

## Exibição de valores na lista

- Ordem **a mercado**: a coluna **Preço** exibe um traço (**-**).
- O **Preço médio** só aparece após a primeira execução.

## Tempo real (SignalR) e reconexão

- A UI **assina as atualizações por conta**.
- A **primeira mensagem** de uma ordem nova leva o **read model completo**; as demais levam **apenas os campos alterados** (ex.: quantidade executada, estado).
- Ao reconectar, a UI envia o **timestamp do último evento recebido** e o servidor retorna do *read model* apenas o que tiver timestamp **superior** a esse valor.

## Edição (replace)

- Ordem **a mercado não pode ter o preço editado**; ordem **limitada** pode.
- A quantidade na edição deve ser **exatamente a quantidade restante** da ordem; não pode ser menor nem maior.
- Na boleta, o campo de quantidade fica **desabilitado**, apenas exibindo a quantidade restante; na prática, a edição altera somente o **preço**.
- Como a ordem a mercado não tem preço editável e a quantidade é fixa, ela **não tem alteração possível** (só cancelamento).

## Eventos e relação com outros módulos

- O módulo **não emite comandos para a Conta**; a Conta e o Portfólio reagem a **OrderSubmitted** e o módulo de Ordens consome as respostas (`FundsReserved`/`FundsReservationError` e `AssetsBlocked`/`AssetBlockError`).
- Eventos **publicados** pelo módulo de Ordens: **OrderSubmitted** (na criação), **OrderDispatched** (no despacho, único que o Exchange consome para abrir ordem), **OrderCancelRequested** e **OrderReplaceRequested**.
- Os demais eventos (ack, rejeição, execução parcial/total, confirmação de cancelamento e de replace) são **eventos do Exchange**, aos quais o módulo **reage** atualizando a ordem e o *read model*.

## Padrões

- Toda implementação (APIs, UIs) deve seguir o `PATTERNS.md` **à risca**.

## Contratos dos eventos

- Todos os eventos do sistema trafegam em JSON dentro de um **envelope genérico** `EventEnvelope<T>` (primeira versão), com:
  - `EventType`: string com o **nome do tipo do evento** (ex.: `"OrderCancelRejected"`);
  - `Event`: instância de `T` com o conteúdo do evento.
- Exchange do RabbitMQ: **`trading-simulator.exchange.order.events`**. Fila do módulo de Ordens: **`trading-simulator.orders.order.events`**.
- Eventos do **Exchange** (aos quais Ordens reage), com payload mínimo:
  - **OrderAcked**: id da ordem.
  - **OrderRejected**: id da ordem.
  - **OrderPartiallyFilled**: id da ordem, quantidade executada, quantidade restante e **preço de execução** (> 0).
  - **OrderFilled**: id da ordem, quantidade executada e **preço de execução** (> 0).
  - **OrderCancelled**: id da ordem, quantidade executada e quantidade restante.
  - **OrderReplaced**: id da ordem, quantidade executada e quantidade restante.
  - **OrderCancelRejected**: id da ordem.
  - **OrderReplaceRejected**: id da ordem.
- Execução com quantidade acima da restante é tratada como **erro**; a validação definitiva ficará no Exchange.

## Cancelamento e replace concorrentes com execução

- Se uma execução chegar durante um pedido de cancelamento ou replace, o Exchange dispara **OrderCancelRejected** ou **OrderReplaceRejected** e a execução prossegue.
- No **replace**, a ordem só muda o seu estado (e o preço) quando houver o **ack do replace** (evento **OrderReplaced**); durante **PendingReplace** a lista continua exibindo o **preço antigo**.

## Exchange

- Os eventos de estado da ordem (Ack, Fill, Cancel, Replace...) vêm do módulo Exchange real; os stubs foram removidos.

## SignalR

- Rota aprovada: **`/hubs/orders`**, com grupo por conta.
- Métodos: `OrderCreated` (read model completo) e `OrderUpdated` (apenas campos alterados).

## Transições de estado (reação a eventos do Exchange)

- OrderDispatched: PendingDispatching → PendingNew. OrderRejected (validação interna): PendingDispatching → Rejected. OrderAcked: PendingNew → New. OrderFilled → Executada. OrderPartiallyFilled → Parcialmente executada. OrderCancelled → Cancelada (mesmo com execução parcial).
- OrderCancelRejected / OrderReplaceRejected: a ordem volta ao **estado anterior** (New ou Parcialmente executada).
- OrderReplaced: New ou Parcialmente executada, conforme a quantidade executada.
- Todos os comandos e eventos usam o **mesmo envelope** `EventEnvelope<T>`.

## Snapshots

- Ordens **não usam snapshots**: a UI carrega o *read model* consolidado e uma ordem possui pouquíssimos eventos.

## Boleta

- Campos: conta, ticker, lado, tipo, quantidade e preço limite.
- O preço fica **desabilitado** na ordem a mercado.
- O ticker é convertido para **caixa alta** e aceita apenas letras e números, no padrão da B3.
- Por ora só trafegam **ações**: **4 letras + 1 dígito**, onde o dígito só pode ser **3, 4 ou 5** (ex.: `PETR4`).

## Comandos publicados por Ordens

- Exchange do RabbitMQ: **`trading-simulator-order-commands`**, recebendo os comandos no mesmo envelope `EventEnvelope<T>`.
- Payloads (confirmados): **OrderSubmitted** e **OrderDispatched** (id, conta, ticker, lado, tipo, quantidade, preço), **OrderCancelRequested** (id), **OrderReplaceRequested** (id, novo preço).
- O preço médio e o preço da última execução vêm do **preço de execução** presente em OrderPartiallyFilled e OrderFilled.

## Ordem de implementação da primeira entrega

1. Domínio e eventos; 2. API de criação; 3. consumo dos eventos do Exchange; 4. read model e SignalR; 5. UI.

## Hash anti-duplicidade (aprovado)

- SHA-256 em hexadecimal (64 caracteres) de `conta|ticker|quantidade|preço|timestamp`, gerado na UI no clique e enviado em `requestHash`. Ordem a mercado entra com preço vazio.
- O servidor apenas valida o formato; índice único no banco. Reenvio devolve a ordem original (idempotência, 201 permanece 201).
- `OrderSubmitted`/`OrderDispatched` publicados também carregam `requestHash` e `occurredAt`; a idempotência por `requestHash` é gravada no `OrderSubmitted`.
- Compatibilidade: eventos antigos `OrderSent` já gravados continuam sendo lidos como abertura (OrderSubmitted + despacho implícito → PendingNew), sem migração.

## Progresso

- Passos 1 e 2 concluídos: domínio, `POST /api/orders`, event store Postgres, outbox e publicação em `trading-simulator-order-commands` (verificado com Postgres/RabbitMQ). Testes de domínio passando.

- Passo 3 concluído: consumidor da fila `trading-simulator.orders.order.events` (exchange `trading-simulator.exchange.order.events`, bind `#`), ack manual, reentrega em falha de infraestrutura; eventos inválidos ou transições proibidas são logados e descartados. Event store com `sequence` por ordem (índice único) para concorrência otimista, com retentativa.
- Domínio já cobre PendingCancel/PendingReplace; os endpoints de cancelar/replace entram no passo 4.

## Decisões de interpretação do passo 3 (revisar)

- Nos eventos de execução, `ExecutedQuantity` é a **quantidade daquela execução** (base do preço médio); a quantidade restante é **conferência**: executada + restante do evento deve ser igual ao restante da ordem, e em OrderFilled a executada deve ser igual ao restante. Divergência é descartada e logada.
- **Confirmado:** `ExecutedQuantity` no evento é por execução; o **read model CQRS acumula** a quantidade executada total (soma das execuções), junto do preço médio e do timestamp do último negócio.
- Execução durante PendingCancel/PendingReplace atualiza as quantidades mas mantém o estado pendente até o CancelRejected/ReplaceRejected da Exchange, que devolve a ordem ao estado anterior (PartiallyFilled). Se a execução for total, a ordem vira Filled e um rejeito tardio apenas é registrado.
- OrderReplaced aplica o preço pedido em OrderReplaceRequested e adota a quantidade restante informada.
- Cancel e replace só podem ser solicitados nos estados New e PartiallyFilled (409 nos demais); replace de ordem a mercado retorna 422.

## Perguntas em aberto

Nenhuma no momento.


## Progresso — Passo 4 (read model, consultas, cancelar/replace, SignalR)
- Read model CQRS `order_read_model` (Postgres), projetado na mesma transação do append de eventos. Consolida estado, quantidade executada acumulada, restante, preço médio, último preço, timestamp do último negócio e `last_action_at`.
- `GET /api/orders?accountId=...` (obrigatório): filtros `ticker`, `state`, `side`, `type`; `sortBy` (qualquer campo da lista) e `sortDirection` (asc/desc); página fixa de 10 (`page`); `updatedSince` retorna apenas ordens com `lastActionAt` posterior (reconexão).
- `GET /api/orders/{id}/history?accountId=...`: histórico de eventos da ordem (404 se a conta não for a dona).
- `POST /api/orders/{id}/cancel` `{accountId}` e `POST /api/orders/{id}/replace` `{accountId, price}` → 202 (PendingCancel/PendingReplace), 404/409/422. A quantidade do replace é sempre a restante, por isso não é enviada.
- SignalR `/hubs/orders`: o cliente chama `SubscribeAccount(accountId)`; mensagens `OrderCreated` (read model completo) e `OrderUpdated` (apenas campos alterados + `lastActionAt`). Ao reconectar a UI assina de novo e chama `GET /api/orders?updatedSince=<último timestamp>`.
- Verificado de ponta a ponta com o Stubs: Ack → parcial → replace → execução durante replace → ReplaceRejected → cancel → Cancelled; preço médio 50,7 correto.

## Passo 5 – UI (UI/projects/main/src/app/orders, Angular + Tailwind)
- Boleta (conta, ticker `^[A-Z]{4}[345]$`, lado, tipo, quantidade, preço desabilitado em Mercado) com hash gerado no clique e botão desabilitado até a resposta do servidor.
- Lista da conta informada: filtros (ativo, estado, lado, tipo), ordenação por qualquer coluna, 10 por página, ícones de histórico/cancelar/replace, "-" para preço de mercado e preço médio antes da 1ª execução.
- Cancelar abre confirmação; replace mostra a quantidade restante desabilitada e preço pré-preenchido com o último preço de execução (ou o original).
- Tempo real via SignalR (`/hubs/orders`); ao reconectar, ressincroniza com `updatedSince`.
- Verificado: build, 6 testes de UI, entrega SignalR (OrderCreated/OrderUpdated) com Stubs. Não verificado: navegação manual no browser e a imagem Docker.
- Teste no browser (Playwright/Chromium headless): boleta, lista em tempo real, replace (quantidade desabilitada, preço pré-preenchido), histórico, cancelamento com confirmação, filtro e ressincronização após queda da API: OK. Corrigidos: diálogos agora recebem foco e fecham com Esc; erro de binding retorna 400 (antes 500). Imagem Docker construída e respondendo na porta 8081.
