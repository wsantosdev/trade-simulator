import { Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { MoneyMaskDirective } from '../money-mask.directive';
import { FormsModule } from '@angular/forms';
import { CurrentAccount } from '../../shared/current-account';
import { describeError, OrdersApi } from '../orders-api';
import { OrderSide, OrderType, isTicker } from '../models';
import { computeRequestHash } from '../request-hash';
import { QuoteWatch } from '../quote-watch';
import { OfferBookWatch } from '../offerbook-watch';

const money = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });
const signedMoney = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL', signDisplay: 'exceptZero' });
const signedPercent = new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2, signDisplay: 'exceptZero' });

@Component({
  imports: [FormsModule, MoneyMaskDirective],
  selector: 'app-order-ticket',
  templateUrl: './order-ticket.html',
  providers: [QuoteWatch, OfferBookWatch],
})
export class OrderTicket implements OnDestroy {
  protected readonly quotes = inject(QuoteWatch);
  protected readonly book = inject(OfferBookWatch);
  protected readonly money = money;
  protected readonly integer = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 0 });
  protected readonly signedMoney = signedMoney;
  protected readonly signedPercent = signedPercent;

  private readonly api = inject(OrdersApi);

  private readonly currentAccount = inject(CurrentAccount);
  readonly accountId = this.currentAccount.accountId;
  readonly ticker = signal('');
  readonly side = signal<OrderSide>('Buy');
  readonly type = signal<OrderType>('Limit');
  readonly quantity = signal<number | null>(null);
  readonly price = signal<number | null>(null);
  readonly busy = signal(false);
  readonly errorMessage = signal('');
  readonly successMessage = signal('');

  readonly tickerInvalid = computed(() => this.ticker() !== '' && !isTicker(this.ticker()));

  readonly marketActive = computed(() => this.ticker() !== '' && !this.tickerInvalid());
  readonly bidRows = computed(() => this.fiveRows(this.book.bids()));
  readonly askRows = computed(() => this.fiveRows(this.book.asks()));

  private fiveRows(levels: readonly { price: number; quantity: number }[]): { price: number; quantity: number }[] {
    const source = this.marketActive() ? levels : [];
    return Array.from({ length: 5 }, (_, i) => source[i] ?? { price: 0, quantity: 0 });
  }

  onTickerChanged(value: string): void {
    this.ticker.set(value.toUpperCase().trim());
    void this.quotes.watch(this.ticker());
    void this.book.watch(this.ticker());
  }

  ngOnDestroy(): void {
    this.quotes.stop();
    this.book.stop();
  }

  protected changeColor(amount: number): string {
    return amount > 0 ? 'text-emerald-700' : amount < 0 ? 'text-red-700' : 'text-slate-600';
  }

  onTypeChanged(value: OrderType): void {
    this.type.set(value);
    if (value === 'Market') this.price.set(null);
  }

  async submit(): Promise<void> {
    if (this.busy()) return;
    const message = this.validate();
    if (message) {
      this.errorMessage.set(message);
      this.successMessage.set('');
      return;
    }

    this.busy.set(true);
    this.errorMessage.set('');
    this.successMessage.set('');
    const accountId = this.accountId()!;
    const quantity = this.quantity()!;
    const price = this.type() === 'Limit' ? this.price() : null;
    const requestHash = await computeRequestHash({
      accountId,
      ticker: this.ticker(),
      quantity,
      price,
      sentAt: new Date().toISOString(),
    });

    this.api
      .send({ accountId, ticker: this.ticker(), side: this.side(), type: this.type(), quantity, price, requestHash })
      .subscribe({
        next: (response) => {
          this.busy.set(false);
          if (response.status === 201) {
            this.successMessage.set(`Ordem enviada (${response.body?.id}).`);
          }
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.errorMessage.set(describeError(error));
        },
      });
  }

  private validate(): string {
    if (!this.accountId()) return 'Carregue uma conta na tela Conta corrente.';
    if (!isTicker(this.ticker())) return 'Informe um ticker válido, como PETR4.';
    const quantity = this.quantity();
    if (quantity === null || !Number.isInteger(quantity) || quantity <= 0) {
      return 'Informe uma quantidade inteira maior que zero.';
    }
    if (this.type() === 'Limit' && !((this.price() ?? 0) > 0)) {
      return 'Informe um preço limite maior que zero.';
    }
    return '';
  }
}
