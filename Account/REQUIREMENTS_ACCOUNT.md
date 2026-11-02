# Requisitos do módulo de conta corrente

## Saldo inicial

- Uma conta corrente deve ser inicializada com saldo disponível de **$ 0** e valor provisionado de **$ 0**.

## Depósitos

- A conta deve permitir depósitos sem limite para a quantidade de operações.
- Cada depósito deve ter valor maior que zero e não pode exceder **$ 1.000.000**.

## Saques

- A conta deve permitir saques até o limite do saldo disponível, inclusive quando o saque zerar esse saldo.
- Não deve haver limite financeiro máximo por saque; o limite efetivo é o saldo disponível.
- Um saque não pode utilizar valores provisionados.

## Provisionamento

- A conta deve permitir provisionar valores para o envio de ordens de compra.
- O valor provisionado deve ficar indisponível para saques e para o saldo disponível.
- A conta não pode provisionar um valor maior que seu saldo disponível.

## Reservas de saldo (HTTP síncrono)

- A Conta **não conhece ordens**: expõe reservas genéricas identificadas por um `reservationId` opaco (Guid v7) escolhido pelo chamador. Não há consumo nem publicação de mensagens.
- `POST /api/accounts/{id}/reservations` `{reservationId, amount}` reserva o valor (idempotente por `reservationId`; saldo insuficiente → `422`; conta inexistente → `404`; mesmo id em outra conta → `409`). A checagem de saldo e a gravação ocorrem numa única transação Postgres, serializada por conta com `pg_advisory_xact_lock`.
- A fila `trade-simulator.account.order.events` consome os eventos de ordem publicados por Orders (exchange `trade-simulator.orders.events`); o id da ordem é o `reservationId`. `OrderPartiallyFilled` debita quantidade × preço de execução do provisionado (`FundsDebited`), idempotente pelo id do envelope (guardado em `FundsDebited.OperationId`); `OrderFilled` faz o mesmo e devolve a sobra ao disponível (`FundsReleased`); `OrderCancelled`, `OrderRejected` e `OrderScheduleCancelled` liberam o restante provisionado. Reservas desconhecidas ou já encerradas são ignoradas.
- Cada reserva é uma linha em `reservations`, atualizada na mesma transação dos eventos da conta.
- Quem calcula o valor (limitada: `quantidade × preço`; a mercado: `quantidade × melhor ask × 1,05`, arredondado para cima ao centavo) e dirige o ciclo de vida da reserva a partir dos eventos da Exchange é o módulo de Ordens.
- A interface da conta atualiza o saldo automaticamente a cada 2 segundos enquanto uma conta está carregada.

## Invariante de saldo

- O saldo disponível jamais pode ser negativo.
- O valor provisionado também não pode ser negativo.
- O saldo disponível corresponde aos valores da conta que não estão provisionados.
