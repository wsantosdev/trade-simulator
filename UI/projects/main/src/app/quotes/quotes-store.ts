import { Injectable, computed, inject, signal } from '@angular/core';
import { Quote, dayChange, isTicker } from './models';
import { InstrumentNames } from './instrument-names';
import { QuotesHub, QuotesHubListener } from './quotes-hub';

export const MAX_POINTS = 3600;
export const WAIT_MS = 5000;

export type Flash = 'up' | 'down' | null;

export const flashFor = (quote: Quote): Flash =>
  quote.variation === 'Up' ? 'up' : quote.variation === 'Down' ? 'down' : null;

export const appendPoint = (points: readonly number[], value: number): number[] => {
  const next = [...points, value];
  return next.length > MAX_POINTS ? next.slice(next.length - MAX_POINTS) : next;
};

@Injectable({ providedIn: 'root' })
export class QuotesStore implements QuotesHubListener {
  private readonly hub = inject(QuotesHub);
  private readonly names = inject(InstrumentNames);
  private active = '';
  private waitTimer?: ReturnType<typeof setTimeout>;

  readonly connected = signal(false);
  readonly ticker = signal('');
  readonly name = signal('');
  readonly quote = signal<Quote | null>(null);
  readonly points = signal<number[]>([]);
  readonly flash = signal<Flash>(null);
  readonly flashTick = signal(0);
  readonly lastDelta = signal(0);
  readonly error = signal('');
  readonly waiting = signal(false);

  readonly tickerInvalid = computed(() => this.ticker() !== '' && !isTicker(this.ticker()));
  readonly change = computed(() => {
    const quote = this.quote();
    return quote ? dayChange(quote) : null;
  });
  readonly flashClass = computed(() => {
    const flash = this.flash();
    if (!flash) return '';
    const even = this.flashTick() % 2 === 0;
    if (flash === 'up') return even ? 'animate-flash-up-a' : 'animate-flash-up-b';
    return even ? 'animate-flash-down-a' : 'animate-flash-down-b';
  });

  start(): void {
    void this.hub.start(this);
  }

  stop(): void {
    this.clearWait();
    void this.hub.stop();
  }

  async setTicker(raw: string): Promise<void> {
    const ticker = raw.toUpperCase().trim();
    this.ticker.set(ticker);
    if (ticker === this.active) return;

    const previous = this.active;
    this.active = '';
    this.reset();
    if (previous) await this.safeUnsubscribe(previous);
    if (!isTicker(ticker)) return;

    this.active = ticker;
    void this.names.nameOf(ticker).then((name) => {
      if (ticker === this.active) this.name.set(name);
    });
    await this.subscribeActive();
  }

  quoteUpdated(quote: Quote): void {
    if (quote.ticker.toUpperCase() !== this.active) return;
    this.clearWait();
    this.waiting.set(false);
    this.error.set('');
    this.apply(quote);
  }

  reconnected(): void {
    if (this.active) void this.subscribeActive();
  }

  connectionChanged(connected: boolean): void {
    this.connected.set(connected);
  }

  private async subscribeActive(): Promise<void> {
    const ticker = this.active;
    this.clearWait();
    try {
      const initial = await this.hub.subscribe(ticker);
      if (ticker !== this.active) return;
      this.error.set('');
      if (initial) {
        this.apply(initial, false);
      } else if (!this.quote()) {
        this.waitTimer = setTimeout(() => {
          if (ticker === this.active && !this.quote()) this.waiting.set(true);
        }, WAIT_MS);
      }
    } catch (error) {
      if (ticker !== this.active) return;
      const message = error instanceof Error ? error.message : '';
      this.error.set(
        /HubException|inv[aá]lid/i.test(message) ? 'Ativo inválido.' : 'Não foi possível assinar as cotações.',
      );
    }
  }

  private apply(quote: Quote, allowFlash = true): void {
    const previous = this.quote();
    this.lastDelta.set(previous ? Math.round((quote.last - previous.last) * 100) / 100 : 0);
    this.quote.set(quote);
    this.points.update((points) => appendPoint(points, quote.last));
    const flash = allowFlash ? flashFor(quote) : null;
    this.flash.set(flash);
    if (flash) this.flashTick.update((tick) => tick + 1);
  }

  private reset(): void {
    this.clearWait();
    this.name.set('');
    this.quote.set(null);
    this.points.set([]);
    this.flash.set(null);
    this.lastDelta.set(0);
    this.error.set('');
    this.waiting.set(false);
  }

  private clearWait(): void {
    if (this.waitTimer) clearTimeout(this.waitTimer);
    this.waitTimer = undefined;
  }

  private async safeUnsubscribe(ticker: string): Promise<void> {
    try {
      await this.hub.unsubscribe(ticker);
    } catch {
      // desconectado: a assinatura some junto com a conexão
    }
  }
}
