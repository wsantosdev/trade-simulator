import { Injectable, computed, inject, signal } from '@angular/core';
import { BookChange, BookLevel, OfferBookDelta, OfferBookSnapshot, Phase, Side, isTicker } from './models';
import { InstrumentNames } from './instrument-names';
import { OfferBookHub, OfferBookHubListener } from './offerbook-hub';

export const MAX_LEVELS = 10;
export const WAIT_MS = 5000;

export type FlashDirection = 'up' | 'down';
interface Flash {
  direction: FlashDirection;
  count: number;
}

const byPriceDesc = (a: BookLevel, b: BookLevel) => b.price - a.price;
const byPriceAsc = (a: BookLevel, b: BookLevel) => a.price - b.price;
const sortFor = (side: Side) => (side === 'Bid' ? byPriceDesc : byPriceAsc);
const key = (side: Side, price: number) => `${side}:${price}`;
const round = (value: number) => Math.round(value * 100) / 100;

@Injectable({ providedIn: 'root' })
export class OfferBookStore implements OfferBookHubListener {
  private readonly hub = inject(OfferBookHub);
  private readonly names = inject(InstrumentNames);
  private active = '';
  private lastSequence: number | null = null;
  private resyncing = false;
  private waitTimer?: ReturnType<typeof setTimeout>;

  readonly connected = signal(false);
  readonly ticker = signal('');
  readonly name = signal('');
  readonly bids = signal<BookLevel[]>([]);
  readonly asks = signal<BookLevel[]>([]);
  readonly phase = signal<Phase | null>(null);
  readonly timestamp = signal('');
  readonly hasBook = signal(false);
  readonly flashes = signal<Record<string, Flash>>({});
  readonly error = signal('');
  readonly waiting = signal(false);

  readonly tickerInvalid = computed(() => this.ticker() !== '' && !isTicker(this.ticker()));
  readonly frozen = computed(() => this.phase() !== null && this.phase() !== 'Open');
  readonly bestBid = computed(() => this.bids()[0] ?? null);
  readonly bestAsk = computed(() => this.asks()[0] ?? null);
  readonly spread = computed(() => {
    const bid = this.bestBid();
    const ask = this.bestAsk();
    if (!bid || !ask) return null;
    const amount = round(ask.price - bid.price);
    return { amount, percent: ask.price > 0 ? (amount / ask.price) * 100 : 0 };
  });
  readonly maxQuantity = computed(() =>
    Math.max(0, ...this.bids().map((l) => l.quantity), ...this.asks().map((l) => l.quantity)),
  );

  barPercent(level: BookLevel): number {
    const max = this.maxQuantity();
    return max > 0 ? Math.round((level.quantity / max) * 100) : 0;
  }

  flashClass(side: Side, price: number): string {
    const flash = this.flashes()[key(side, price)];
    if (!flash) return '';
    const even = flash.count % 2 === 0;
    if (flash.direction === 'up') return even ? 'animate-book-flash-up-a' : 'animate-book-flash-up-b';
    return even ? 'animate-book-flash-down-a' : 'animate-book-flash-down-b';
  }

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

  snapshotReceived(snapshot: OfferBookSnapshot): void {
    if (snapshot.ticker.toUpperCase() !== this.active) return;
    this.clearWait();
    this.waiting.set(false);
    this.error.set('');
    this.applySnapshot(snapshot);
  }

  deltaReceived(delta: OfferBookDelta): void {
    if (delta.ticker.toUpperCase() !== this.active || this.resyncing) return;
    if (!this.hasBook() || delta.previousSequence !== this.lastSequence) {
      void this.resync();
      return;
    }

    const flashes = { ...this.flashes() };
    this.bids.set(this.applyChanges('Bid', delta.changes, flashes));
    this.asks.set(this.applyChanges('Ask', delta.changes, flashes));
    this.flashes.set(flashes);
    this.lastSequence = delta.sequence;
    this.phase.set(delta.phase);
    this.timestamp.set(delta.timestamp);
  }

  reconnected(): void {
    if (this.active) void this.resync();
  }

  connectionChanged(connected: boolean): void {
    this.connected.set(connected);
  }

  private async resync(): Promise<void> {
    if (this.resyncing) return;
    this.resyncing = true;
    try {
      await this.subscribeActive();
    } finally {
      this.resyncing = false;
    }
  }

  private async subscribeActive(): Promise<void> {
    const ticker = this.active;
    this.clearWait();
    try {
      const initial = await this.hub.subscribe(ticker);
      if (ticker !== this.active) return;
      this.error.set('');
      if (initial) {
        this.waiting.set(false);
        this.applySnapshot(initial);
      } else if (!this.hasBook()) {
        this.waitTimer = setTimeout(() => {
          if (ticker === this.active && !this.hasBook()) this.waiting.set(true);
        }, WAIT_MS);
      }
    } catch (error) {
      if (ticker !== this.active) return;
      const message = error instanceof Error ? error.message : '';
      this.error.set(
        /HubException|inv[aá]lid/i.test(message) ? 'Ativo inválido.' : 'Não foi possível assinar o livro de ofertas.',
      );
    }
  }

  private applySnapshot(snapshot: OfferBookSnapshot): void {
    const flashes: Record<string, Flash> = this.hasBook() ? { ...this.flashes() } : {};
    const compare = this.hasBook();
    const bids = [...snapshot.bids].sort(byPriceDesc).slice(0, MAX_LEVELS);
    const asks = [...snapshot.asks].sort(byPriceAsc).slice(0, MAX_LEVELS);
    if (compare) {
      this.flashDiff('Bid', this.bids(), bids, flashes);
      this.flashDiff('Ask', this.asks(), asks, flashes);
    }
    this.bids.set(bids);
    this.asks.set(asks);
    this.flashes.set(flashes);
    this.lastSequence = snapshot.sequence;
    this.phase.set(snapshot.phase);
    this.timestamp.set(snapshot.timestamp);
    this.hasBook.set(true);
  }

  private flashDiff(side: Side, before: BookLevel[], after: BookLevel[], flashes: Record<string, Flash>): void {
    const old = new Map(before.map((l) => [l.price, l.quantity]));
    for (const level of after) this.markFlash(side, level.price, old.get(level.price), level.quantity, flashes);
  }

  private applyChanges(side: Side, changes: BookChange[], flashes: Record<string, Flash>): BookLevel[] {
    const levels = new Map((side === 'Bid' ? this.bids() : this.asks()).map((l) => [l.price, l]));
    for (const change of changes) {
      if (change.side !== side) continue;
      const before = levels.get(change.price)?.quantity;
      if (change.quantity <= 0) {
        levels.delete(change.price);
        delete flashes[key(side, change.price)];
        continue;
      }
      levels.set(change.price, { price: change.price, quantity: change.quantity, orders: change.orders });
      this.markFlash(side, change.price, before, change.quantity, flashes);
    }
    return [...levels.values()].sort(sortFor(side)).slice(0, MAX_LEVELS);
  }

  private markFlash(
    side: Side,
    price: number,
    before: number | undefined,
    after: number,
    flashes: Record<string, Flash>,
  ): void {
    if (before === after) return;
    const k = key(side, price);
    flashes[k] = {
      direction: before === undefined || after > before ? 'up' : 'down',
      count: (flashes[k]?.count ?? 0) + 1,
    };
  }

  private reset(): void {
    this.clearWait();
    this.name.set('');
    this.bids.set([]);
    this.asks.set([]);
    this.phase.set(null);
    this.timestamp.set('');
    this.hasBook.set(false);
    this.flashes.set({});
    this.lastSequence = null;
    this.resyncing = false;
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
