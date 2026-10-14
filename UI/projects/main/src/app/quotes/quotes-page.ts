import { MainMenu } from '../shared/main-menu/main-menu';
import { Component, OnDestroy, OnInit, computed, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { formatPercent, formatPrice, formatSignedPrice, formatTime } from './models';
import { QuotesStore } from './quotes-store';

@Component({
  imports: [MainMenu, FormsModule],
  selector: 'app-quotes-page',
  templateUrl: './quotes-page.html',
})
export class QuotesPage implements OnInit, OnDestroy {
  protected readonly store = inject(QuotesStore);
  protected readonly formatPrice = formatPrice;
  protected readonly formatSignedPrice = formatSignedPrice;
  protected readonly formatPercent = formatPercent;
  protected readonly formatTime = formatTime;

  private readonly width = 640;
  private readonly height = 200;

  protected readonly chart = computed(() => {
    const points = this.store.points();
    const quote = this.store.quote();
    if (points.length < 2 || !quote) return null;
    const min = Math.min(...points, quote.low);
    const max = Math.max(...points, quote.high);
    const span = max - min || 1;
    const y = (value: number) => 8 + (1 - (value - min) / span) * (this.height - 16);
    const x = (index: number) => (index / (points.length - 1)) * this.width;
    return {
      path: points.map((value, i) => `${i === 0 ? 'M' : 'L'}${x(i).toFixed(1)},${y(value).toFixed(1)}`).join(' '),
      highY: y(quote.high),
      lowY: y(quote.low),
    };
  });

  protected get viewBox(): string {
    return `0 0 ${this.width} ${this.height}`;
  }

  protected get trendColor(): string {
    const delta = this.store.lastDelta();
    return delta > 0 ? 'text-emerald-700' : delta < 0 ? 'text-red-700' : 'text-slate-600';
  }

  protected changeColor(amount: number): string {
    return amount > 0 ? 'text-emerald-700' : amount < 0 ? 'text-red-700' : 'text-slate-600';
  }

  ngOnInit(): void {
    this.store.start();
  }

  ngOnDestroy(): void {
    this.store.stop();
  }
}
