import { TestBed } from '@angular/core/testing';
import { FakeQuotesHub, quote } from './fake-hub';
import { MAX_POINTS, QuotesStore, appendPoint, flashFor } from './quotes-store';
import { QuotesHub } from './quotes-hub';
import { dayChange, isTicker } from './models';

describe('QuotesStore', () => {
  let hub: FakeQuotesHub;
  let store: QuotesStore;

  beforeEach(() => {
    vi.useFakeTimers();
    hub = new FakeQuotesHub();
    TestBed.configureTestingModule({ providers: [{ provide: QuotesHub, useValue: hub }] });
    store = TestBed.inject(QuotesStore);
  });
  afterEach(() => vi.useRealTimers());

  it('validates tickers', () => {
    expect(isTicker('PETR4')).toBe(true);
    expect(isTicker('BOVA11')).toBe(true);
    expect(isTicker('petr4')).toBe(false);
    expect(isTicker('PETR')).toBe(false);
    expect(isTicker('PETR123')).toBe(false);
  });

  it('maps variation to flash direction', () => {
    expect(flashFor(quote(1, 'Up'))).toBe('up');
    expect(flashFor(quote(1, 'Down'))).toBe('down');
    expect(flashFor(quote(1, 'Unchanged'))).toBeNull();
  });

  it('computes the day change against the previous close', () => {
    const change = dayChange({ ...quote(10.5), close: 10 });
    expect(change.amount).toBe(0.5);
    expect(change.percent).toBeCloseTo(5);
  });

  it('caps the chart buffer', () => {
    let points: number[] = [];
    for (let i = 0; i < MAX_POINTS + 10; i++) points = appendPoint(points, i);
    expect(points.length).toBe(MAX_POINTS);
    expect(points[0]).toBe(10);
  });

  it('subscribes, flashes and alternates the animation on consecutive updates', async () => {
    await store.setTicker('petr4');
    expect(store.ticker()).toBe('PETR4');
    expect(hub.subscribed).toEqual(['PETR4']);

    store.quoteUpdated(quote(10.5, 'Up'));
    const first = store.flashClass();
    store.quoteUpdated(quote(10.6, 'Up'));
    expect(first).toContain('animate-flash-up');
    expect(store.flashClass()).not.toBe(first);
    expect(store.lastDelta()).toBe(0.1);

    store.quoteUpdated(quote(10.4, 'Down'));
    expect(store.flashClass()).toContain('animate-flash-down');
    store.quoteUpdated(quote(10.4, 'Unchanged'));
    expect(store.flashClass()).toBe('');
    expect(store.points()).toEqual([10.5, 10.6, 10.4, 10.4]);
  });

  it('ignores quotes of other tickers and does not flash the initial snapshot', async () => {
    hub.initial = quote(10.2, 'Up');
    await store.setTicker('PETR4');
    expect(store.quote()?.last).toBe(10.2);
    expect(store.flashClass()).toBe('');
    store.quoteUpdated(quote(99, 'Up', 'VALE3'));
    expect(store.quote()?.last).toBe(10.2);
  });

  it('switches ticker: unsubscribes previous and clears the chart', async () => {
    await store.setTicker('PETR4');
    store.quoteUpdated(quote(10, 'Up'));
    await store.setTicker('VALE3');
    expect(hub.unsubscribed).toEqual(['PETR4']);
    expect(hub.subscribed).toEqual(['PETR4', 'VALE3']);
    expect(store.points()).toEqual([]);
    expect(store.quote()).toBeNull();
  });

  it('does not subscribe invalid tickers', async () => {
    await store.setTicker('XX');
    expect(store.tickerInvalid()).toBe(true);
    expect(hub.subscribed).toEqual([]);
  });

  it('resubscribes after reconnect', async () => {
    await store.setTicker('PETR4');
    store.reconnected();
    await Promise.resolve();
    expect(hub.subscribed).toEqual(['PETR4', 'PETR4']);
  });

  it('shows waiting after 5 seconds without quotes', async () => {
    await store.setTicker('PETR4');
    expect(store.waiting()).toBe(false);
    vi.advanceTimersByTime(5000);
    expect(store.waiting()).toBe(true);
    store.quoteUpdated(quote(10));
    expect(store.waiting()).toBe(false);
  });

  it('reports invalid asset errors from the hub', async () => {
    hub.failWith = new Error("An unexpected error occurred invoking 'Subscribe'. HubException: Ativo inválido.");
    await store.setTicker('PETR4');
    expect(store.error()).toBe('Ativo inválido.');
  });
});
