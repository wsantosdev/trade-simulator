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

## Provisionamento por ordem (mensageria)

- A conta consome `OrderSubmitted` (fila durável `trade-simulator.account.order.submitted`; o binding/fila antiga `trade-simulator.account.order.commands` foi abandonada); ordens de venda são descartadas.
- Compra limitada provisiona `quantidade × preço`; compra a mercado provisiona `quantidade × melhor ask do OfferBook × 1,05`, arredondado para cima ao centavo. Sem preço disponível, a reserva falha com `PriceUnavailable`.
- O resultado é publicado no exchange `trade-simulator.account.reservations` (`funds.reserved` / `funds.reservation.failed`) como `Result` (`isError`, `result`/`error`), via outbox transacional.
- Cada ordem tem uma reserva própria (`order_reservations`), atualizada na mesma transação dos eventos da conta.
- `OrderPartiallyFilled`/`OrderFilled` debitam o valor executado do provisionado (`FundsDebited`); no `OrderFilled` a sobra volta ao disponível (`FundsReleased`).
- `OrderCancelled`/`OrderRejected` liberam o restante provisionado para o disponível.
- Mensagens são idempotentes pelo id do envelope (`processed_messages`, mesma transação).
- Eventos de execução sem reserva são reentregues até 5 vezes e então descartados.
- A interface da conta atualiza o saldo automaticamente a cada 2 segundos enquanto uma conta está carregada.

## Invariante de saldo

- O saldo disponível jamais pode ser negativo.
- O valor provisionado também não pode ser negativo.
- O saldo disponível corresponde aos valores da conta que não estão provisionados.
