import { BookChange, BookLevel, OfferBookDelta, OfferBookSnapshot, Phase } from './models';
import { OfferBookHubListener } from './offerbook-hub';

export class FakeOfferBookHub {
  listener?: OfferBookHubListener;
  subscribed: string[] = [];
  unsubscribed: string[] = [];
  initial: OfferBookSnapshot | null = null;
  failWith?: Error;

  async start(listener: OfferBookHubListener): Promise<void> {
    this.listener = listener;
    listener.connectionChanged(true);
  }
  async subscribe(ticker: string): Promise<OfferBookSnapshot | null> {
    this.subscribed.push(ticker);
    if (this.failWith) throw this.failWith;
    return this.initial;
  }
  async unsubscribe(ticker: string): Promise<void> {
    this.unsubscribed.push(ticker);
  }
  async stop(): Promise<void> {}
}

export const level = (price: number, quantity: number, orders = 1): BookLevel => ({ price, quantity, orders });

export const snapshot = (
  sequence: number,
  bids: BookLevel[],
  asks: BookLevel[],
  phase: Phase = 'Open',
  ticker = 'PETR4',
): OfferBookSnapshot => ({ ticker, sequence, phase, bids, asks, timestamp: '2026-01-05T10:00:00-03:00' });

export const delta = (
  sequence: number,
  changes: BookChange[],
  phase: Phase = 'Open',
  ticker = 'PETR4',
): OfferBookDelta => ({
  ticker,
  sequence,
  previousSequence: sequence - 1,
  phase,
  changes,
  timestamp: '2026-01-05T10:00:01-03:00',
});

export const bid = (price: number, quantity: number, orders = 1): BookChange => ({
  side: 'Bid',
  ...level(price, quantity, orders),
});
export const ask = (price: number, quantity: number, orders = 1): BookChange => ({
  side: 'Ask',
  ...level(price, quantity, orders),
});
