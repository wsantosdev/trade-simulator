import { TestBed } from '@angular/core/testing';
import { ask, bid, delta, FakeOfferBookHub, level, snapshot } from './fake-hub';
import { isTicker, phaseLabel } from './models';
import { OfferBookHub } from './offerbook-hub';
import { MAX_LEVELS, OfferBookStore, WAIT_MS } from './offerbook-store';

describe('OfferBookStore', () => {
  let hub: FakeOfferBookHub;
  let store: OfferBookStore;

  beforeEach(async () => {
    vi.useFakeTimers();
    hub = new FakeOfferBookHub();
    TestBed.configureTestingModule({ providers: [{ provide: OfferBookHub, useValue: hub }] });
    store = TestBed.inject(OfferBookStore);
    await store.setTicker('PETR4');
    store.snapshotReceived(
      snapshot(1, [level(10, 100), level(9.99, 200)], [level(10.01, 300), level(10.02, 150)]),
    );
  });
  afterEach(() => vi.useRealTimers());

  it('validates tickers and labels phases', () => {
    expect(isTicker('PETR4')).toBe(true);
    expect(isTicker('petr4')).toBe(false);
    expect(phaseLabel('PreOpen')).toBe('Pré-abertura');
    expect(phaseLabel('Open')).toBe('Aberto');
    expect(phaseLabel('Closed')).toBe('Fechado');
  });

  it('subscribes and applies the snapshot without flashing', () => {
    expect(hub.subscribed).toEqual(['PETR4']);
    expect(store.bids().map((l) => l.price)).toEqual([10, 9.99]);
    expect(store.asks().map((l) => l.price)).toEqual([10.01, 10.02]);
    expect(store.flashClass('Bid', 10)).toBe('');
    expect(store.bestBid()?.price).toBe(10);
    expect(store.bestAsk()?.price).toBe(10.01);
    expect(store.spread()?.amount).toBe(0.01);
    expect(store.phase()).toBe('Open');
    expect(store.frozen()).toBe(false);
  });

  it('applies the initial snapshot returned by Subscribe', async () => {
    hub.initial = snapshot(7, [level(20, 10)], [level(21, 10)], 'Closed', 'VALE3');
    await store.setTicker('VALE3');
    expect(store.bids()[0].price).toBe(20);
    expect(store.frozen()).toBe(true);
    expect(store.flashClass('Bid', 20)).toBe('');
  });

  it('flashes green when quantity grows, red when it shrinks and nothing when equal', () => {
    store.deltaReceived(delta(2, [bid(10, 150), ask(10.01, 100), ask(10.02, 150)]));
    expect(store.flashClass('Bid', 10)).toContain('animate-book-flash-up');
    expect(store.flashClass('Ask', 10.01)).toContain('animate-book-flash-down');
    expect(store.flashClass('Ask', 10.02)).toBe('');
    expect(store.bids()[0].quantity).toBe(150);
  });

  it('flashes green for a new level and alternates the animation on repeated changes', () => {
    store.deltaReceived(delta(2, [bid(9.98, 50)]));
    const first = store.flashClass('Bid', 9.98);
    expect(first).toContain('animate-book-flash-up');
    store.deltaReceived(delta(3, [bid(9.98, 60)]));
    expect(store.flashClass('Bid', 9.98)).toContain('animate-book-flash-up');
    expect(store.flashClass('Bid', 9.98)).not.toBe(first);
  });

  it('removes levels with quantity 0 and keeps ordering', () => {
    store.deltaReceived(delta(2, [bid(9.99, 0), bid(10.005, 40), ask(10.015, 20)]));
    expect(store.bids().map((l) => l.price)).toEqual([10.005, 10]);
    expect(store.asks().map((l) => l.price)).toEqual([10.01, 10.015, 10.02]);
  });

  it('keeps at most 10 levels per side', () => {
    const bids = Array.from({ length: 10 }, (_, i) => bid(9 - i / 100, 10));
    store.deltaReceived(delta(2, bids));
    expect(store.bids().length).toBe(MAX_LEVELS);
    expect(store.bids()[0].price).toBe(10);
    store.deltaReceived(delta(3, [bid(10.5, 5)]));
    expect(store.bids().length).toBe(MAX_LEVELS);
    expect(store.bids()[0].price).toBe(10.5);
  });

  it('heartbeat delta only advances sequence and phase', () => {
    store.deltaReceived(delta(2, [], 'Closed'));
    expect(store.frozen()).toBe(true);
    expect(store.bids().length).toBe(2);
    store.deltaReceived(delta(3, []));
    expect(hub.subscribed).toEqual(['PETR4']);
  });

  it('resubscribes on a sequence gap and replaces the book with the new snapshot', async () => {
    hub.initial = snapshot(10, [level(11, 1)], [level(12, 1)]);
    store.deltaReceived({ ...delta(9, [bid(10, 5)]), previousSequence: 5 });
    await vi.advanceTimersByTimeAsync(0);
    expect(hub.subscribed).toEqual(['PETR4', 'PETR4']);
    expect(store.bids().map((l) => l.price)).toEqual([11]);
    store.deltaReceived(delta(11, [bid(11, 2)]));
    expect(store.bids()[0].quantity).toBe(2);
  });

  it('ignores deltas of other tickers', () => {
    store.deltaReceived(delta(2, [bid(10, 999)], 'Open', 'VALE3'));
    expect(store.bids()[0].quantity).toBe(100);
    expect(hub.subscribed).toEqual(['PETR4']);
  });

  it('switches ticker: unsubscribes previous and clears the book', async () => {
    await store.setTicker('VALE3');
    expect(hub.unsubscribed).toEqual(['PETR4']);
    expect(hub.subscribed).toEqual(['PETR4', 'VALE3']);
    expect(store.hasBook()).toBe(false);
    expect(store.bids()).toEqual([]);
  });

  it('does not subscribe invalid tickers', async () => {
    await store.setTicker('XX');
    expect(store.tickerInvalid()).toBe(true);
    expect(hub.subscribed).toEqual(['PETR4']);
  });

  it('resubscribes after reconnect', async () => {
    store.reconnected();
    await vi.advanceTimersByTimeAsync(0);
    expect(hub.subscribed).toEqual(['PETR4', 'PETR4']);
  });

  it('computes proportional volume bars', () => {
    expect(store.maxQuantity()).toBe(300);
    expect(store.barPercent(level(10, 150))).toBe(50);
  });

  it('shows waiting when no snapshot arrives and reports hub errors', async () => {
    await store.setTicker('BBAS3');
    vi.advanceTimersByTime(WAIT_MS);
    expect(store.waiting()).toBe(true);
    hub.failWith = new Error("HubException: Invalid ticker.");
    await store.setTicker('ITUB4');
    expect(store.error()).toBe('Ativo inválido.');
  });
});
