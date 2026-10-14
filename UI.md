# UI.md – Regras de interface

Complementa o `PATTERNS.md`. Referência visual: telas de Conta Corrente e Ordens em `UI/projects/main`. Todo novo módulo com interface deve seguir estas regras.

## Stack e projeto
- Angular na versão mais recente, standalone components, signals e `inject()`. Tailwind CSS 4 (`@import "tailwindcss"` em `src/styles.css`, `.postcssrc.json` com `@tailwindcss/postcss`). Sem CSS por componente (`app.css` não existe); apenas utilitários Tailwind.
- Todas as UIs vivem em um único workspace Angular na pasta `UI/` (solution folder `UI`), com `package.json`, scripts `build` e `test` e arquivos listados no `TradingSimulator.slnx`. Sem `node_modules`/`dist` versionados.
- Projeto `main` (porta **4200**): app único com uma página por módulo, em rotas lazy `/account`, `/orders`, `/portfolio`, `/quotes` e `/offerbook` (`/` redireciona para `/account`). Cada módulo novo com tela vira uma pasta `projects/main/src/app/<modulo>` com `<modulo>-page.*`, uma rota e um item no menu.
- Menu comum: componente `MainMenu` (`app-main-menu`, em `shared/main-menu`), usado no lugar do `nav` em todas as páginas. É um menu sanduíche (botão à esquerda; painel só com os links; fecha com Esc, clique fora ou ao navegar), seguido da marca (ícone, "Trading Simulator" e subtítulo, sempre visível e levando a `/`); no canto direito, o número da conta atual (discreto, `text-xs text-slate-500`) logo antes da pílula de status, que é projetada. Links para todas as telas, exceto Exchange; a rota ativa é destacada.
- Conta atual: `CurrentAccount` (`shared/current-account.ts`) guarda a conta carregada na Conta corrente (sessionStorage) e vale para todas as telas. Nenhuma outra tela tem campo de número da conta; sem conta carregada, elas orientam a carregar uma na Conta corrente.
- Exchange é um serviço externo: sua UI é o projeto `exchange` (porta **4204**), separado do app principal, sem menu e sem links para as demais telas. Roda no mesmo container `ui` do compose (nginx), em porta distinta.
- Em dev: `npm start` (main) e `npm run start:exchange`. No Docker: `docker compose up -d --build ui` serve `http://localhost:4200` e `http://localhost:4204`; o nginx faz proxy por prefixo (`/api/accounts`, `/api/orders`, `/api/assets`, `/api/instruments`, `/hubs/*`) para os containers de cada módulo.
- Chamadas sempre com URL relativa (`/api`, `/hubs`); em dev, `proxy.main.conf.json` roteia por prefixo para a API de cada módulo (`/hubs` com `ws: true`). O mesmo origin dispensa CORS, mas as APIs liberam `http://localhost:4200`.
- Animações de piscar: o texto/preço usa `animate-flash-*` (700 ms); o fundo do livro de ofertas usa `animate-book-flash-*` (900 ms). Ambos ficam em `projects/main/src/styles.css`.

## Layout (padrão de todas as telas)
- **`index.html`**: `lang="pt-BR"`, `<title>` no formato `<Nome da tela> | Trading Simulator` (ex.: "Conta Corrente | Trading Simulator", "Ordens | Trading Simulator") e `meta viewport`.
- Fundo da página `bg-[#f6f8f7]`, texto `text-slate-900`; fonte Inter/system (definida em `styles.css`, igual à de Conta Corrente).
- Contêiner: `mx-auto max-w-7xl px-4 py-6 sm:px-6 lg:px-8 lg:py-10`.
- **Barra superior (`nav`)**: marca (ícone verde `bg-emerald-700`, `size-10 rounded-xl shadow-sm`, com o SVG de gráfico em alta idêntico em todos os módulos, nunca texto/sigla, + "Trading Simulator" + subtítulo do módulo, ex.: "Painel de ordens") à esquerda e uma *pílula de status* à direita, separada do conteúdo por `border-b border-slate-200`.
- **Pílula de status**: `rounded-full border px-3 py-1.5 text-xs font-semibold` com bolinha `size-2`. Verde (`emerald-50/200/800`, bolinha `emerald-500`) para normal/conectado ("Ambiente de simulação", "Tempo real conectado"); âmbar (`amber-50/200/800`) para degradado/desconectado. Anunciada com `role="status"` e `aria-live="polite"`.
- **Cabeçalho da página (`header`)**: kicker em caixa alta `text-sm font-semibold text-emerald-700`, `h1` `text-3xl font-bold tracking-tight text-slate-950 sm:text-4xl` e descrição `text-sm text-slate-600 sm:text-base`.
- Seções em cartões: `rounded-lg border border-slate-200 bg-white p-6 shadow-sm`, com `h2` `text-lg font-semibold` e `aria-labelledby`.
- Diagramação idêntica em todas as telas: `nav` com `gap-4 border-b pb-5`, `header` com `py-8 lg:py-10` (kicker `mb-2`, descrição `mt-2 max-w-xl leading-6`), cartões sempre `rounded-lg p-6` (sem variações por breakpoint) e `gap-6` entre cartões/seções; só o conteúdo interno muda.
- Largura estável: `html { scrollbar-gutter: stable; }` em todos os `styles.css`, para que a barra de rolagem (presente só em telas longas, como a conta corrente) não desloque nem estreite o conteúdo em relação às demais telas.

## Componentes e estados
- Botão primário: `bg-emerald-700 text-white hover:bg-emerald-800`, anel de foco `focus:ring-2 focus:ring-emerald-600`, `disabled:opacity-60 disabled:cursor-not-allowed`. Ação destrutiva em vermelho (`text-red-700`). Ações secundárias com borda `border-slate-300`.
- Cursor: tudo o que for clicável (links, botões, selects, labels de checkbox/radio, cabeçalhos ordenáveis, linhas/itens com ação, `role="button"`) usa o cursor de link (`cursor: pointer`); a regra global fica no `styles.css` de cada UI e elementos clicáveis novos fora dela usam `cursor-pointer`. Desabilitados mantêm `cursor-not-allowed`.
- Campos: `label` sempre associado (`for`/`id`), borda `slate-300`, foco `ring-emerald-600`; inválido com `border-red-500`, `aria-invalid` e mensagem `text-xs text-red-600`. Campos derivados/somente leitura ficam desabilitados.
- Ícones de ação em tabelas são botões com `title` e `aria-label`; desabilitados em `slate-300` quando a ação não se aplica.
- Diálogos: sobreposição `fixed inset-0 bg-slate-900/50`, `role="dialog"`/`alertdialog`, `aria-modal`, `aria-labelledby`, foco movido para o diálogo ao abrir, fecha com Esc, clique fora ou botão "Voltar"/"Fechar". Ações destrutivas exigem confirmação.
- Tabelas: cabeçalhos ordenáveis por botão, números alinhados à direita, valor ausente exibido como "-", paginação com "Anterior/Próxima" e total.
- Textos em português; datas e números formatados em pt-BR.

## Comunicação com a API
- Todo request inclui `X-Correlation-Id`. Timeout de 10 s e erro visível em região `role="status"`/`role="alert"`; nunca manter carregamento indefinido.
- Botões de envio ficam desabilitados enquanto não houver resposta do servidor; identificadores de idempotência (hash) são gerados no clique.
- Erros da API (`ProblemDetails`) são traduzidos em mensagem legível para o usuário.
- Tempo real via SignalR: conexão com retry, reassinatura ao reconectar e ressincronização consultando apenas o que mudou desde o último timestamp recebido. O estado da conexão é exibido na pílula de status. Mensagens parciais mesclam apenas os campos informados.
- Validação no cliente espelha a do servidor (IDs UUID v7, ticker etc.), sem substituí-la.

## Acessibilidade e responsividade
- Layout responsivo a partir de 320 px (`grid-cols-1` → `sm:`/`lg:`).
- Rótulos em todos os controles, estados anunciados (`aria-live`), operação completa por teclado e foco sempre visível.

## Testes
- Testes unitários (Vitest via `ng test`) para utilitários, stores/serviços e o componente raiz; HTTP e SignalR são substituídos por dependências fake.
- Verificação manual/automática em browser (Playwright) dos fluxos principais antes de entregar.
