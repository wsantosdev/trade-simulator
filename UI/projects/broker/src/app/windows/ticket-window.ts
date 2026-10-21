import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MoneyMaskDirective } from '../../../../main/src/app/orders/money-mask.directive';
import { OrderSide, OrderType, isTicker } from '../../../../main/src/app/orders/models';
import { OfferBookWatch } from '../../../../main/src/app/orders/offerbook-watch';
import { OrdersApi, describeError } from '../../../../main/src/app/orders/orders-api';
import { QuoteWatch } from '../../../../main/src/app/orders/quote-watch';
import { computeRequestHash } from '../../../../main/src/app/orders/request-hash';
import { CurrentAccount } from '../../../../main/src/app/shared/current-account';
import { WindowState } from '../workspace/window-model';

const money = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });
const signedPercent = new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2, signDisplay: 'exceptZero' });

@Component({
  selector: 'app-ticket-window',
  imports: [FormsModule, MoneyMaskDirective],
  providers: [QuoteWatch, OfferBookWatch],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form class="flex min-h-full flex-col gap-3 p-3" (ngSubmit)="submit()">
      <div role="group" aria-label="Lado da ordem" class="grid grid-cols-2 overflow-hidden rounded-md border border-slate-300 text-sm font-bold">
        <button
          type="button"
          class="py-2 focus:outline-none focus:ring-2 focus:ring-inset focus:ring-emerald-600"
          [class]="side() === 'Buy' ? 'bg-emerald-700 text-white' : 'bg-white text-slate-700 hover:bg-slate-100'"
          [attr.aria-pressed]="side() === 'Buy'"
          (click)="side.set('Buy')"
        >
          COMPRA
        </button>
        <button
          type="button"
          class="py-2 focus:outline-none focus:ring-2 focus:ring-inset focus:ring-red-600"
          [class]="side() === 'Sell' ? 'bg-red-700 text-white' : 'bg-white text-slate-700 hover:bg-slate-100'"
          [attr.aria-pressed]="side() === 'Sell'"
          (click)="side.set('Sell')"
        >
          VENDA
        </button>
      </div>

      <div class="grid grid-cols-2 gap-2">
        <div class="flex flex-col gap-0.5">
          <label [for]="id() + '-ticker'" class="text-xs font-medium text-slate-600">Ativo</label>
          <input
            [id]="id() + '-ticker'"
            name="ticker"
            type="text"
            maxlength="6"
            autocomplete="off"
            placeholder="PETR4"
            class="rounded-md border bg-white px-2 py-1.5 text-sm uppercase focus:outline-none focus:ring-2 focus:ring-emerald-600"
            [class.border-red-500]="tickerInvalid()"
            [class.border-slate-300]="!tickerInvalid()"
            [attr.aria-invalid]="tickerInvalid()"
            [ngModel]="ticker()"
            (ngModelChange)="onTickerChanged($event)"
          />
        </div>
        <div class="flex flex-col gap-0.5">
          <label [for]="id() + '-type'" class="text-xs font-medium text-slate-600">Tipo</label>
          <select
            [id]="id() + '-type'"
            name="type"
            class="rounded-md border border-slate-300 bg-white px-2 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-emerald-600"
            [ngModel]="type()"
            (ngModelChange)="onTypeChanged($event)"
          >
            <option value="Limit">Limitada</option>
            <option value="Market">A mercado</option>
          </select>
        </div>
        <div class="flex flex-col gap-0.5">
          <label [for]="id() + '-quantity'" class="text-xs font-medium text-slate-600">Quantidade</label>
          <input
            [id]="id() + '-quantity'"
            name="quantity"
            type="number"
            min="1"
            step="1"
            class="rounded-md border border-slate-300 bg-white px-2 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-emerald-600"
            [ngModel]="quantity()"
            (ngModelChange)="quantity.set($event)"
          />
        </div>
        <div class="flex flex-col gap-0.5">
          <label [for]="id() + '-price'" class="text-xs font-medium text-slate-600">Preço limite</label>
          <input
            [id]="id() + '-price'"
            name="price"
            type="text"
            appMoneyMask
            class="rounded-md border border-slate-300 bg-white px-2 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-emerald-600 disabled:bg-slate-100 disabled:text-slate-400"
            [disabled]="type() === 'Market'"
            [ngModel]="price()"
            (ngModelChange)="price.set($event)"
          />
        </div>
      </div>

      <div class="rounded-md border border-slate-200 bg-white p-2 text-xs" aria-live="polite">
        @if (!marketActive()) {
          <p class="text-slate-500">Informe um ativo para ver o mercado.</p>
        } @else if (quotes.quote(); as quote) {
          <div class="flex items-baseline justify-between">
            <span class="font-semibold">{{ quote.ticker }}</span>
            <span class="text-lg font-bold tabular-nums" [class]="quotes.flashClass()">{{ money.format(quote.last) }}</span>
            @if (quotes.change(); as change) {
              <span class="font-semibold tabular-nums" [class]="changeColor(change.amount)">{{ signedPercent.format(change.percent) }}%</span>
            }
          </div>
          <div class="mt-1 grid grid-cols-2 gap-2 tabular-nums">
            <button type="button" class="rounded border border-emerald-200 bg-emerald-50 px-1 py-1 text-left text-emerald-800 hover:bg-emerald-100 disabled:cursor-default" [disabled]="!book.bids().length" title="Usar como preço limite" (click)="usePrice(book.bids()[0]?.price)">
              <span class="block text-[11px]">Melhor compra</span>
              <span class="font-semibold">{{ book.bids()[0] ? money.format(book.bids()[0].price) : '-' }}</span>
            </button>
            <button type="button" class="rounded border border-red-200 bg-red-50 px-1 py-1 text-left text-red-800 hover:bg-red-100 disabled:cursor-default" [disabled]="!book.asks().length" title="Usar como preço limite" (click)="usePrice(book.asks()[0]?.price)">
              <span class="block text-[11px]">Melhor venda</span>
              <span class="font-semibold">{{ book.asks()[0] ? money.format(book.asks()[0].price) : '-' }}</span>
            </button>
          </div>
        } @else if (quotes.unavailable()) {
          <p class="text-amber-700">Cotação indisponível no momento.</p>
        } @else {
          <p class="text-slate-500">Aguardando cotação…</p>
        }
      </div>

      <div role="status" aria-live="polite" class="text-xs">
        @if (errorMessage()) {
          <p class="rounded border border-red-200 bg-red-50 px-2 py-1 text-red-700" role="alert">{{ errorMessage() }}</p>
        }
        @if (successMessage()) {
          <p class="rounded border border-emerald-200 bg-emerald-50 px-2 py-1 text-emerald-800">{{ successMessage() }}</p>
        }
      </div>

      <button
        type="submit"
        class="mt-auto rounded-md py-2 text-sm font-bold text-white focus:outline-none focus:ring-2 focus:ring-offset-1 disabled:cursor-not-allowed disabled:opacity-50"
        [class]="side() === 'Buy' ? 'bg-emerald-700 hover:bg-emerald-800 focus:ring-emerald-600' : 'bg-red-700 hover:bg-red-800 focus:ring-red-600'"
        [disabled]="busy()"
      >
        {{ busy() ? 'Enviando…' : side() === 'Buy' ? 'Enviar compra' : 'Enviar venda' }}
      </button>
    </form>
  `,
})
export class TicketWindow implements OnInit, OnDestroy {
  readonly window = input.required<WindowState>();
  protected readonly quotes = inject(QuoteWatch);
  protected readonly book = inject(OfferBookWatch);
  protected readonly money = money;
  protected readonly signedPercent = signedPercent;
  protected readonly id = computed(() => this.window().id);

  private readonly api = inject(OrdersApi);
  private readonly currentAccount = inject(CurrentAccount);

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

  ngOnInit(): void {
    const ticker = this.window().ticker;
    if (ticker) this.onTickerChanged(ticker);
  }

  ngOnDestroy(): void {
    this.quotes.stop();
    this.book.stop();
  }

  onTickerChanged(value: string): void {
    this.ticker.set(value.toUpperCase().trim());
    void this.quotes.watch(this.ticker());
    void this.book.watch(this.ticker());
  }

  onTypeChanged(value: OrderType): void {
    this.type.set(value);
    if (value === 'Market') this.price.set(null);
  }

  usePrice(value: number | undefined): void {
    if (value === undefined) return;
    this.type.set('Limit');
    this.price.set(value);
  }

  protected changeColor(amount: number): string {
    return amount > 0 ? 'text-emerald-700' : amount < 0 ? 'text-red-700' : 'text-slate-600';
  }

  async submit(): Promise<void> {
    if (this.busy()) return;
    const message = this.validate();
    this.successMessage.set('');
    this.errorMessage.set(message);
    if (message) return;

    this.busy.set(true);
    const accountId = this.currentAccount.accountId()!;
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
          if (response.status === 201) this.successMessage.set(`Ordem enviada (${response.body?.id}).`);
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.errorMessage.set(describeError(error));
        },
      });
  }

  private validate(): string {
    if (!this.currentAccount.accountId()) return 'Carregue uma conta na janela Conta corrente.';
    if (!isTicker(this.ticker())) return 'Informe um ticker válido, como PETR4.';
    const quantity = this.quantity();
    if (quantity === null || !Number.isInteger(quantity) || quantity <= 0) {
      return 'Informe uma quantidade inteira maior que zero.';
    }
    if (this.type() === 'Limit' && !((this.price() ?? 0) > 0)) return 'Informe um preço limite maior que zero.';
    return '';
  }
}
