import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, inject, input } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  BookLevel,
  Side,
  formatPercent,
  formatPrice,
  formatQuantity,
  formatTime,
  phaseLabel,
} from '../../../../main/src/app/offerbook/models';
import { OfferBookStore } from '../../../../main/src/app/offerbook/offerbook-store';
import { WindowState } from '../workspace/window-model';
import { LiveBadge } from './live-badge';

@Component({
  selector: 'app-offerbook-window',
  imports: [FormsModule, LiveBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex min-h-full flex-col gap-2 p-3">
      <div class="flex items-end justify-between gap-2">
        <div class="flex w-32 flex-col gap-0.5">
          <label for="book-ticker" class="text-xs font-medium text-slate-600">Ativo</label>
          <input
            id="book-ticker"
            type="text"
            maxlength="6"
            autocomplete="off"
            placeholder="PETR4"
            class="rounded-md border bg-white px-2 py-1 text-sm uppercase focus:outline-none focus:ring-2 focus:ring-emerald-600"
            [class.border-red-500]="store.tickerInvalid()"
            [class.border-slate-300]="!store.tickerInvalid()"
            [attr.aria-invalid]="store.tickerInvalid()"
            [ngModel]="store.ticker()"
            (ngModelChange)="store.setTicker($event)"
          />
        </div>
        @if (store.hasBook()) {
          <span
            class="rounded-full border px-2 py-0.5 text-[11px] font-semibold"
            [class]="store.frozen() ? 'border-amber-200 bg-amber-50 text-amber-800' : 'border-emerald-200 bg-emerald-50 text-emerald-800'"
            role="status"
          >
            Pregão: {{ store.phase() ? phaseLabel(store.phase()!) : '-' }}
          </span>
        }
      </div>
      @if (store.tickerInvalid()) {
        <p class="text-xs text-red-600">Informe um ticker válido, como PETR4.</p>
      }
      <div role="status" aria-live="polite" class="text-xs">
        @if (store.error()) {
          <p class="text-red-600">{{ store.error() }}</p>
        } @else if (store.waiting()) {
          <p class="rounded border border-amber-200 bg-amber-50 px-2 py-1 text-amber-700">Aguardando o livro de {{ store.ticker() }}…</p>
        }
      </div>

      @if (store.hasBook()) {
        @if (store.name()) {
          <p class="truncate text-xs text-slate-600"><span class="font-semibold text-slate-900">{{ store.ticker() }}</span> · {{ store.name() }}</p>
        }
        @if (store.frozen()) {
          <p class="rounded border border-amber-200 bg-amber-50 px-2 py-1 text-xs text-amber-700">Fora do pregão: livro congelado no último estado.</p>
        }
        <dl class="grid grid-cols-3 gap-1 text-center text-xs">
          <div class="rounded border border-emerald-200 bg-emerald-50 p-1.5">
            <dt class="text-emerald-800">Melhor compra</dt>
            <dd class="font-semibold tabular-nums text-emerald-800">{{ store.bestBid() ? formatPrice(store.bestBid()!.price) : '-' }}</dd>
          </div>
          <div class="rounded border border-red-200 bg-red-50 p-1.5">
            <dt class="text-red-800">Melhor venda</dt>
            <dd class="font-semibold tabular-nums text-red-800">{{ store.bestAsk() ? formatPrice(store.bestAsk()!.price) : '-' }}</dd>
          </div>
          <div class="rounded border border-slate-200 bg-white p-1.5">
            <dt class="text-slate-600">Spread</dt>
            <dd class="font-semibold tabular-nums">
              @if (store.spread(); as spread) { {{ formatPrice(spread.amount) }} ({{ formatPercent(spread.percent) }}) } @else { - }
            </dd>
          </div>
        </dl>
        <div class="grid grid-cols-2 gap-2">
          <table class="w-full table-fixed text-xs tabular-nums">
            <caption class="pb-1 text-left font-semibold text-emerald-800">Compras</caption>
            <thead>
              <tr class="border-b border-slate-200 text-slate-600">
                <th scope="col" class="w-1/5 px-1 py-1 text-right font-medium">Ofer.</th>
                <th scope="col" class="w-2/5 px-1 py-1 text-right font-medium">Qtd.</th>
                <th scope="col" class="w-2/5 px-1 py-1 text-right font-medium">Preço</th>
              </tr>
            </thead>
            <tbody>
              @for (level of store.bids(); track level.price; let first = $first) {
                <tr class="border-b border-slate-100" [class.font-bold]="first" [class]="store.flashClass('Bid', level.price)" [style.background-image]="barStyle('Bid', level)" data-testid="bid-row">
                  <td class="px-1 py-1 text-right text-slate-700">{{ level.orders }}</td>
                  <td class="px-1 py-1 text-right">{{ formatQuantity(level.quantity) }}</td>
                  <td class="px-1 py-1 text-right text-emerald-800">{{ formatPrice(level.price) }}</td>
                </tr>
              } @empty {
                <tr><td colspan="3" class="px-1 py-2 text-center text-slate-600">Sem ofertas.</td></tr>
              }
            </tbody>
          </table>
          <table class="w-full table-fixed text-xs tabular-nums">
            <caption class="pb-1 text-left font-semibold text-red-800">Vendas</caption>
            <thead>
              <tr class="border-b border-slate-200 text-slate-600">
                <th scope="col" class="w-2/5 px-1 py-1 text-left font-medium">Preço</th>
                <th scope="col" class="w-2/5 px-1 py-1 text-right font-medium">Qtd.</th>
                <th scope="col" class="w-1/5 px-1 py-1 text-right font-medium">Ofer.</th>
              </tr>
            </thead>
            <tbody>
              @for (level of store.asks(); track level.price; let first = $first) {
                <tr class="border-b border-slate-100" [class.font-bold]="first" [class]="store.flashClass('Ask', level.price)" [style.background-image]="barStyle('Ask', level)" data-testid="ask-row">
                  <td class="px-1 py-1 text-left text-red-800">{{ formatPrice(level.price) }}</td>
                  <td class="px-1 py-1 text-right">{{ formatQuantity(level.quantity) }}</td>
                  <td class="px-1 py-1 text-right text-slate-700">{{ level.orders }}</td>
                </tr>
              } @empty {
                <tr><td colspan="3" class="px-1 py-2 text-center text-slate-600">Sem ofertas.</td></tr>
              }
            </tbody>
          </table>
        </div>
        <p class="text-[11px] text-slate-500">Atualizado às {{ formatTime(store.timestamp()) }}</p>
      } @else if (!store.error() && !store.waiting()) {
        <p class="text-sm text-slate-600">Informe um ativo para ver as melhores ofertas.</p>
      }
      <div class="mt-auto"><app-live-badge [connected]="store.connected()" /></div>
    </div>
  `,
})
export class OfferBookWindow implements OnInit, OnDestroy {
  readonly window = input.required<WindowState>();
  protected readonly store = inject(OfferBookStore);
  protected readonly formatPrice = formatPrice;
  protected readonly formatQuantity = formatQuantity;
  protected readonly formatPercent = formatPercent;
  protected readonly formatTime = formatTime;
  protected readonly phaseLabel = phaseLabel;

  protected barStyle(side: Side, level: BookLevel): string {
    const percent = this.store.barPercent(level);
    const color = side === 'Bid' ? 'var(--book-bid-bar)' : 'var(--book-ask-bar)';
    const direction = side === 'Bid' ? 'to left' : 'to right';
    return `linear-gradient(${direction}, ${color} ${percent}%, transparent ${percent}%)`;
  }

  ngOnInit(): void {
    this.store.start();
    const ticker = this.window().ticker;
    if (ticker && !this.store.ticker()) void this.store.setTicker(ticker);
  }

  ngOnDestroy(): void {
    this.store.stop();
  }
}
