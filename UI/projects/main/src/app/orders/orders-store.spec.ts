import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { Order } from './models';
import { OrdersApi } from './orders-api';
import { OrdersHub } from './orders-hub';
import { OrdersStore } from './orders-store';

const account = '01a13323-857f-754a-b009-6f8a317a186a';
const order = (id: string, overrides: Partial<Order> = {}): Order => ({
  id,
  accountId: account,
  ticker: 'PETR4',
  side: 'Buy',
  type: 'Limit',
  quantity: 100,
  price: 50,
  state: 'New',
  executedQuantity: 0,
  remainingQuantity: 100,
  averagePrice: null,
  lastPrice: null,
  lastTradeAt: null,
  sentAt: '2026-01-01T00:00:00Z',
  lastActionAt: '2026-01-01T00:00:00Z',
  ...overrides,
});

describe('OrdersStore', () => {
  let store: OrdersStore;
  const search = vi.fn();

  beforeEach(() => {
    search.mockReset();
    search.mockReturnValue(of({ items: [order('a')], page: 1, pageSize: 10, totalCount: 1 }));
    TestBed.configureTestingModule({
      providers: [
        { provide: OrdersApi, useValue: { search } },
        { provide: OrdersHub, useValue: { subscribe: async () => {}, start: async () => {}, stop: async () => {} } },
      ],
    });
    store = TestBed.inject(OrdersStore);
    store.setAccount(account);
  });

  it('loads the informed account and clears the list when the number becomes invalid', () => {
    expect(search).toHaveBeenCalledTimes(1);
    expect(store.accountId()).toBe(account);
    store.setAccount('abc');
    expect(store.accountId()).toBeNull();
    expect(store.orders()).toEqual([]);
  });

  it('applies updates changing only the informed fields', () => {
    store.updated({
      id: 'a',
      lastActionAt: '2026-01-01T00:00:05Z',
      state: 'PartiallyFilled',
      executedQuantity: 30,
      remainingQuantity: null as unknown as undefined,
    });
    const updated = store.orders()[0];
    expect(updated.state).toBe('PartiallyFilled');
    expect(updated.executedQuantity).toBe(30);
    expect(updated.remainingQuantity).toBe(100);
    expect(updated.lastActionAt).toBe('2026-01-01T00:00:05Z');
  });

  it('adds a created order and resumes by the last received timestamp on reconnection', () => {
    store.created(order('b', { lastActionAt: '2026-01-01T00:00:09Z', sentAt: '2026-01-01T00:00:09Z' }));
    expect(store.orders().map((o) => o.id)).toEqual(['b', 'a']);

    search.mockClear();
    search.mockReturnValue(of({ items: [], page: 1, pageSize: 10, totalCount: 0 }));
    store.reconnected();
    expect(search.mock.calls[0][0].updatedSince).toBe('2026-01-01T00:00:09Z');
  });
});
