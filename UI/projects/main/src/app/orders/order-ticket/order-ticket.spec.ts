import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { OrderTicket } from './order-ticket';
import { OrdersHub } from '../orders-hub';
import { Quote } from '../models';
import { QuotesHub, QuotesHubListener } from '../quotes-hub';
import { OfferBookHub, OfferBookHubListener } from '../offerbook-hub';
import { OfferBookSnapshot } from '../models';

const lv = (price: number, quantity: number) => ({ price, quantity, orders: 1 });
const book = (sequence: number, phase: OfferBookSnapshot['phase'] = 'Open'): OfferBookSnapshot => ({
  ticker: 'PETR4',
  sequence,
  phase,
  bids: Array.from({ length: 7 }, (_, i) => lv(10 - i / 100, 100 + i)),
  asks: Array.from({ length: 7 }, (_, i) => lv(10.01 + i / 100, 200 + i)),
  timestamp: '2026-01-05T10:00:00-03:00',
});

class FakeOfferBookHub {
  listener?: OfferBookHubListener;
  isConnected = true;
  subscribed: string[] = [];
  unsubscribed: string[] = [];
  stopped = false;
  initial: OfferBookSnapshot | null = null;
  async start(listener: OfferBookHubListener): Promise<void> {
    this.listener = listener;
    listener.connectionChanged(this.isConnected);
  }
  async subscribe(ticker: string): Promise<OfferBookSnapshot | null> {
    this.subscribed.push(ticker);
    return this.initial;
  }
  async unsubscribe(ticker: string): Promise<void> {
    this.unsubscribed.push(ticker);
  }
  async stop(): Promise<void> {
    this.stopped = true;
  }
}

const quote = (last: number, variation: Quote['variation']): Quote => ({
  ticker: 'PETR4', open: 10, close: 10, high: 12, low: 9, last, variation, timestamp: '2026-01-05T10:00:00-03:00',
});

class FakeQuotesHub {
  listener?: QuotesHubListener;
  isConnected = true;
  subscribed: string[] = [];
  unsubscribed: string[] = [];
  stopped = false;
  failSubscribe = false;
  async start(listener: QuotesHubListener): Promise<void> {
    this.listener = listener;
    listener.connectionChanged(this.isConnected);
  }
  async subscribe(ticker: string): Promise<Quote | null> {
    this.subscribed.push(ticker);
    if (this.failSubscribe) throw new Error('down');
    return null;
  }
  async unsubscribe(ticker: string): Promise<void> {
    this.unsubscribed.push(ticker);
  }
  async stop(): Promise<void> {
    this.stopped = true;
  }
}

describe('OrderTicket quotes', () => {
  let hub: FakeQuotesHub;
  let bookHub: FakeOfferBookHub;

  const create = async () => {
    const fixture = TestBed.createComponent(OrderTicket);
    await fixture.whenStable();
    return fixture;
  };
  const type = async (fixture: Awaited<ReturnType<typeof create>>, value: string) => {
    fixture.componentInstance.onTickerChanged(value);
    await fixture.whenStable();
    await Promise.resolve();
    fixture.detectChanges();
  };

  beforeEach(() => {
    hub = new FakeQuotesHub();
    bookHub = new FakeOfferBookHub();
    TestBed.configureTestingModule({
      imports: [OrderTicket],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: QuotesHub, useValue: hub },
        { provide: OfferBookHub, useValue: bookHub },
        { provide: OrdersHub, useValue: { start: async () => {}, stop: async () => {}, subscribe: async () => {} } },
      ],
    });
  });

  it('subscribes a valid ticker and shows price, flash and day change', async () => {
    const fixture = await create();
    await type(fixture, 'petr4');
    expect(hub.subscribed).toEqual(['PETR4']);

    hub.listener!.quoteUpdated(quote(10.5, 'Up'));
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const last = el.querySelector('[data-testid="quote-last"]')!;
    expect(last.textContent).toContain('10,50');
    expect(last.className).toContain('animate-flash-up');
    expect(el.querySelector('[data-testid="quote-change"]')!.className).toContain('text-emerald-700');
    expect(el.querySelector('[data-testid="quote-change"]')!.textContent).toContain('5,00%');

    hub.listener!.quoteUpdated(quote(10.5, 'Unchanged'));
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="quote-last"]')!.className).not.toContain('animate-flash');
  });

  it('unsubscribes when the ticker changes, becomes invalid, and on destroy', async () => {
    const fixture = await create();
    await type(fixture, 'PETR4');
    await type(fixture, 'VALE3');
    expect(hub.unsubscribed).toEqual(['PETR4']);
    await type(fixture, 'VAL');
    expect(hub.unsubscribed).toEqual(['PETR4', 'VALE3']);
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="quote-last"]')?.textContent).toContain('0,00');
    await type(fixture, 'PETR4');
    fixture.destroy();
    expect(hub.unsubscribed).toEqual(['PETR4', 'VALE3', 'PETR4']);
  });

  it('shows "Cotação indisponível" quietly when the service is down', async () => {
    hub.isConnected = false;
    const fixture = await create();
    await type(fixture, 'PETR4');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Cotação indisponível');
    expect(fixture.componentInstance.errorMessage()).toBe('');
  });

  it('shows the 5 best offers per side and applies deltas', async () => {
    bookHub.initial = book(1);
    const fixture = await create();
    await type(fixture, 'PETR4');
    expect(bookHub.subscribed).toEqual(['PETR4']);
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelectorAll('[data-testid="book-bid-row"]').length).toBe(5);
    expect(el.querySelectorAll('[data-testid="book-ask-row"]').length).toBe(5);
    expect(el.querySelector('[data-testid="book-bid-row"]')!.textContent).toContain('10,00');
    expect(el.querySelector('[data-testid="book-ask-row"]')!.textContent).toContain('10,01');

    bookHub.listener!.deltaReceived({
      ticker: 'PETR4', sequence: 2, previousSequence: 1, phase: 'Closed', timestamp: '',
      changes: [{ side: 'Bid', price: 10, quantity: 0, orders: 0 }, { side: 'Ask', price: 10.01, quantity: 999, orders: 1 }],
    });
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="book-bid-row"]')!.textContent).toContain('9,99');
    expect(el.querySelector('[data-testid="book-ask-row"]')!.textContent).toContain('999');
    expect(el.querySelector('[data-testid="book-frozen"]')).not.toBeNull();
  });

  it('resubscribes the book on a sequence gap', async () => {
    bookHub.initial = book(1);
    const fixture = await create();
    await type(fixture, 'PETR4');
    bookHub.initial = book(10);
    bookHub.listener!.deltaReceived({
      ticker: 'PETR4', sequence: 5, previousSequence: 4, phase: 'Open', timestamp: '', changes: [],
    });
    await Promise.resolve();
    await Promise.resolve();
    expect(bookHub.subscribed).toEqual(['PETR4', 'PETR4']);
  });

  it('unsubscribes the book on ticker change and on destroy', async () => {
    const fixture = await create();
    await type(fixture, 'PETR4');
    await type(fixture, 'VALE3');
    expect(bookHub.unsubscribed).toEqual(['PETR4']);
    await type(fixture, 'VAL');
    expect(bookHub.unsubscribed).toEqual(['PETR4', 'VALE3']);
    await type(fixture, 'PETR4');
    fixture.destroy();
    expect(bookHub.unsubscribed).toEqual(['PETR4', 'VALE3', 'PETR4']);
    expect(bookHub.stopped).toBe(true);
  });
});
