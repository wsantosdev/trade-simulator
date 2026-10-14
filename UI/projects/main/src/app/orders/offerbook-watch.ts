import { Injectable, computed, inject, signal } from '@angular/core';
import { BookChange, BookLevel, BookPhase, BookSide, OfferBookDelta, OfferBookSnapshot, isTicker } from './models';
import { OfferBookHub, OfferBookHubListener } from './offerbook-hub';

export const BOOK_DEPTH = 5;

const sortFor = (side: BookSide) => (a: BookLevel, b: BookLevel) => (side === 'Bid' ? b.price - a.price : a.price - b.price);

/** Acompanha o livro de ofertas de um único ativo (aplica snapshot/delta); falhas nunca bloqueiam a boleta. */
@Injectable()
export class OfferBookWatch implements OfferBookHubListener {
  private readonly hub = inject(OfferBookHub);
  private active = '';
  private started = false;
  private resyncing = false;
  private lastSequence: number | null = null;
  private bidLevels: BookLevel[] = [];
  private askLevels: BookLevel[] = [];

  private readonly watching = signal(false);
  readonly connected = signal(false);
  readonly failed = signal(false);
  readonly hasBook = signal(false);
  readonly phase = signal<BookPhase | null>(null);
  readonly bids = signal<BookLevel[]>([]);
  readonly asks = signal<BookLevel[]>([]);

  readonly unavailable = computed(() => this.watching() && !this.hasBook() && (this.failed() || !this.connected()));
  readonly frozen = computed(() => this.phase() !== null && this.phase() !== 'Open');

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

  snapshotReceived(snapshot: OfferBookSnapshot): void {
    if (snapshot.ticker.toUpperCase() !== this.active) return;
    this.applySnapshot(snapshot);
  }

  deltaReceived(delta: OfferBookDelta): void {
    if (delta.ticker.toUpperCase() !== this.active || this.resyncing) return;
    if (!this.hasBook() || delta.previousSequence !== this.lastSequence) {
      void this.resync();
      return;
    }
    this.bidLevels = this.applyChanges('Bid', this.bidLevels, delta.changes);
    this.askLevels = this.applyChanges('Ask', this.askLevels, delta.changes);
    this.lastSequence = delta.sequence;
    this.phase.set(delta.phase);
    this.publish();
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
    if (!this.hub.isConnected) return;
    try {
      const initial = await this.hub.subscribe(ticker);
      if (ticker !== this.active) return;
      this.failed.set(false);
      if (initial) this.applySnapshot(initial);
    } catch {
      if (ticker === this.active) this.failed.set(true);
    }
  }

  private applySnapshot(snapshot: OfferBookSnapshot): void {
    // O servidor limita o livro a 10 níveis; a boleta mantém a janela completa para aplicar deltas.
    this.bidLevels = [...snapshot.bids].sort(sortFor('Bid'));
    this.askLevels = [...snapshot.asks].sort(sortFor('Ask'));
    this.lastSequence = snapshot.sequence;
    this.phase.set(snapshot.phase);
    this.failed.set(false);
    this.hasBook.set(true);
    this.publish();
  }

  private applyChanges(side: BookSide, current: BookLevel[], changes: BookChange[]): BookLevel[] {
    const levels = new Map(current.map((l) => [l.price, l]));
    for (const change of changes) {
      if (change.side !== side) continue;
      if (change.quantity <= 0) levels.delete(change.price);
      else levels.set(change.price, { price: change.price, quantity: change.quantity, orders: change.orders });
    }
    return [...levels.values()].sort(sortFor(side)).slice(0, 10);
  }

  private publish(): void {
    this.bids.set(this.bidLevels.slice(0, BOOK_DEPTH));
    this.asks.set(this.askLevels.slice(0, BOOK_DEPTH));
  }

  private clear(): void {
    this.bidLevels = [];
    this.askLevels = [];
    this.lastSequence = null;
    this.resyncing = false;
    this.bids.set([]);
    this.asks.set([]);
    this.phase.set(null);
    this.hasBook.set(false);
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
