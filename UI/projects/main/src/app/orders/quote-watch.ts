import { Injectable, computed, inject, signal } from '@angular/core';
import { Quote, isTicker } from './models';
import { QuotesHub, QuotesHubListener } from './quotes-hub';

export type QuoteFlash = 'up' | 'down' | null;

/** Acompanha a cotação de um único ativo; falhas do serviço nunca bloqueiam a boleta. */
@Injectable()
export class QuoteWatch implements QuotesHubListener {
  private readonly hub = inject(QuotesHub);
  private active = '';
  private started = false;

  private readonly watching = signal(false);
  readonly connected = signal(false);
  readonly failed = signal(false);
  readonly quote = signal<Quote | null>(null);
  readonly flash = signal<QuoteFlash>(null);
  readonly flashTick = signal(0);

  readonly unavailable = computed(() => this.watching() && !this.quote() && (this.failed() || !this.connected()));
  readonly change = computed(() => {
    const quote = this.quote();
    if (!quote) return null;
    const amount = Math.round((quote.last - quote.close) * 100) / 100;
    return { amount, percent: quote.close > 0 ? (amount / quote.close) * 100 : 0 };
  });
  readonly flashClass = computed(() => {
    const flash = this.flash();
    if (!flash) return '';
    const even = this.flashTick() % 2 === 0;
    if (flash === 'up') return even ? 'animate-flash-up-a' : 'animate-flash-up-b';
    return even ? 'animate-flash-down-a' : 'animate-flash-down-b';
  });

  async watch(ticker: string): Promise<void> {
    if (ticker === this.active) return;
    const previous = this.active;
    this.active = '';
    this.watching.set(false);
    this.clear();
    if (previous) await this.safeUnsubscribe(previous);
    if (!isTicker(ticker)) return;

    this.active = ticker;
    this.watching.set(true);
    if (!this.started) {
      this.started = true;
      void this.hub.start(this).catch(() => this.failed.set(true));
    }
    await this.subscribeActive();
  }

  stop(): void {
    const previous = this.active;
    this.active = '';
    this.watching.set(false);
    if (previous) void this.safeUnsubscribe(previous);
    if (this.started) void this.hub.stop();
    this.started = false;
  }

  quoteUpdated(quote: Quote): void {
    if (quote.ticker.toUpperCase() !== this.active) return;
    this.apply(quote, true);
  }

  reconnected(): void {
    if (this.active) void this.subscribeActive();
  }

  connectionChanged(connected: boolean): void {
    this.connected.set(connected);
  }

  private async subscribeActive(): Promise<void> {
    const ticker = this.active;
    if (!this.hub.isConnected) return;
    try {
      const initial = await this.hub.subscribe(ticker);
      if (ticker !== this.active) return;
      this.failed.set(false);
      if (initial) this.apply(initial, false);
    } catch {
      if (ticker === this.active) this.failed.set(true);
    }
  }

  private apply(quote: Quote, allowFlash: boolean): void {
    this.failed.set(false);
    this.quote.set(quote);
    const flash: QuoteFlash = !allowFlash ? null : quote.variation === 'Up' ? 'up' : quote.variation === 'Down' ? 'down' : null;
    this.flash.set(flash);
    if (flash) this.flashTick.update((tick) => tick + 1);
  }

  private clear(): void {
    this.quote.set(null);
    this.flash.set(null);
    this.failed.set(false);
  }

  private async safeUnsubscribe(ticker: string): Promise<void> {
    try {
      await this.hub.unsubscribe(ticker);
    } catch {
      // a assinatura expira junto com a conexão
    }
  }
}
