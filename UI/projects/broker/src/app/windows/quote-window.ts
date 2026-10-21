import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, computed, inject, input } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { QuotesHub } from '../../../../main/src/app/quotes/quotes-hub';
import { formatPercent, formatPrice, formatSignedPrice, formatTime } from '../../../../main/src/app/quotes/models';
import { QuotesStore } from '../../../../main/src/app/quotes/quotes-store';
import { WindowManager } from '../workspace/window-manager';
import { WindowState } from '../workspace/window-model';
import { LiveBadge } from './live-badge';

const width = 320;
const height = 90;

@Component({
  selector: 'app-quote-window',
  imports: [FormsModule, LiveBadge],
  providers: [QuotesHub, QuotesStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex min-h-full flex-col gap-2 p-3">
      <div class="flex items-end gap-2">
        <div class="flex min-w-0 flex-1 flex-col gap-0.5">
          <label [for]="id() + '-ticker'" class="text-xs font-medium text-slate-600">Ativo</label>
          <input
            [id]="id() + '-ticker'"
            type="text"
            maxlength="6"
            autocomplete="off"
            placeholder="PETR4"
            class="w-full rounded-md border bg-white px-2 py-1 text-sm uppercase focus:outline-none focus:ring-2 focus:ring-emerald-600"
            [class.border-red-500]="store.tickerInvalid()"
            [class.border-slate-300]="!store.tickerInvalid()"
            [attr.aria-invalid]="store.tickerInvalid()"
            [ngModel]="store.ticker()"
            (ngModelChange)="store.setTicker($event)"
          />
        </div>
        <button
          type="button"
          class="rounded-md bg-emerald-700 px-2.5 py-1 text-sm font-semibold text-white hover:bg-emerald-800 focus:outline-none focus:ring-2 focus:ring-emerald-600 disabled:cursor-not-allowed disabled:opacity-50"
          [disabled]="!manager.canOpen('ticket') || !store.ticker()"
          [title]="manager.canOpen('ticket') ? 'Abrir boleta com este ativo' : 'Limite de 3 boletas abertas'"
          (click)="manager.open('ticket', { ticker: store.ticker() })"
        >
          Negociar
        </button>
      </div>
      @if (store.tickerInvalid()) {
        <p class="text-xs text-red-600">Informe um ticker válido, como PETR4.</p>
      }
      <div role="status" aria-live="polite" class="text-xs">
        @if (store.error()) {
          <p class="text-red-600">{{ store.error() }}</p>
        } @else if (store.waiting()) {
          <p class="rounded border border-amber-200 bg-amber-50 px-2 py-1 text-amber-700">Aguardando cotação de {{ store.ticker() }}…</p>
        }
      </div>

      @if (store.quote(); as quote) {
        <div>
          <p class="truncate text-xs text-slate-600">
            <span class="font-semibold text-slate-900">{{ quote.ticker }}</span>
            @if (store.name()) { · {{ store.name() }} }
          </p>
          <div class="flex flex-wrap items-baseline gap-x-3">
            <span class="text-3xl font-bold tabular-nums" [class]="store.flashClass()" data-testid="last">{{ formatPrice(quote.last) }}</span>
            <span class="text-sm font-semibold tabular-nums" [class]="trend()">
              {{ store.lastDelta() > 0 ? '▲' : store.lastDelta() < 0 ? '▼' : '■' }} {{ formatSignedPrice(store.lastDelta()) }}
            </span>
            @if (store.change(); as change) {
              <span class="text-sm font-semibold tabular-nums" [class]="color(change.amount)" data-testid="day-change">
                {{ formatPercent(change.percent) }}
              </span>
            }
          </div>
        </div>
        <dl class="grid grid-cols-4 gap-1 text-center text-[11px]">
          <div class="rounded border border-slate-200 bg-white px-1 py-1"><dt class="text-slate-500">Abert.</dt><dd class="font-semibold tabular-nums">{{ formatPrice(quote.open) }}</dd></div>
          <div class="rounded border border-slate-200 bg-white px-1 py-1"><dt class="text-slate-500">Fech.</dt><dd class="font-semibold tabular-nums">{{ formatPrice(quote.close) }}</dd></div>
          <div class="rounded border border-slate-200 bg-white px-1 py-1"><dt class="text-slate-500">Máx.</dt><dd class="font-semibold tabular-nums text-emerald-700">{{ formatPrice(quote.high) }}</dd></div>
          <div class="rounded border border-slate-200 bg-white px-1 py-1"><dt class="text-slate-500">Mín.</dt><dd class="font-semibold tabular-nums text-red-700">{{ formatPrice(quote.low) }}</dd></div>
        </dl>
        <div class="min-h-14 flex-1">
          @if (chart(); as chart) {
            <svg [attr.viewBox]="'0 0 ' + w + ' ' + h" class="h-full min-h-14 w-full" preserveAspectRatio="none" role="img" aria-label="Gráfico das últimas cotações">
              <line x1="0" [attr.x2]="w" [attr.y1]="chart.highY" [attr.y2]="chart.highY" stroke="#059669" stroke-dasharray="4 4" vector-effect="non-scaling-stroke" />
              <line x1="0" [attr.x2]="w" [attr.y1]="chart.lowY" [attr.y2]="chart.lowY" stroke="#dc2626" stroke-dasharray="4 4" vector-effect="non-scaling-stroke" />
              <path [attr.d]="chart.path" fill="none" class="stroke-slate-900" stroke-width="2" stroke-linejoin="round" vector-effect="non-scaling-stroke" />
            </svg>
          } @else {
            <p class="text-xs text-slate-500">O gráfico aparece após algumas cotações.</p>
          }
        </div>
        <p class="text-[11px] text-slate-500">Última cotação às {{ formatTime(quote.timestamp) }}</p>
      } @else if (!store.error() && !store.waiting()) {
        <p class="text-sm text-slate-600">Informe um ativo para acompanhar a cotação.</p>
      }
      <div class="mt-auto"><app-live-badge [connected]="store.connected()" /></div>
    </div>
  `,
})
export class QuoteWindow implements OnInit, OnDestroy {
  readonly window = input.required<WindowState>();
  protected readonly store = inject(QuotesStore);
  protected readonly manager = inject(WindowManager);
  protected readonly formatPrice = formatPrice;
  protected readonly formatSignedPrice = formatSignedPrice;
  protected readonly formatPercent = formatPercent;
  protected readonly formatTime = formatTime;
  protected readonly w = width;
  protected readonly h = height;
  protected readonly id = computed(() => this.window().id);

  protected readonly chart = computed(() => {
    const points = this.store.points();
    const quote = this.store.quote();
    if (points.length < 2 || !quote) return null;
    const min = Math.min(...points, quote.low);
    const max = Math.max(...points, quote.high);
    const span = max - min || 1;
    const y = (value: number) => 4 + (1 - (value - min) / span) * (height - 8);
    const x = (index: number) => (index / (points.length - 1)) * width;
    return {
      path: points.map((value, i) => `${i === 0 ? 'M' : 'L'}${x(i).toFixed(1)},${y(value).toFixed(1)}`).join(' '),
      highY: y(quote.high),
      lowY: y(quote.low),
    };
  });

  protected readonly trend = computed(() => this.color(this.store.lastDelta()));

  protected color(amount: number): string {
    return amount > 0 ? 'text-emerald-700' : amount < 0 ? 'text-red-700' : 'text-slate-600';
  }

  ngOnInit(): void {
    this.store.start();
    const ticker = this.window().ticker;
    if (ticker) void this.store.setTicker(ticker);
  }

  ngOnDestroy(): void {
    this.store.stop();
  }
}
