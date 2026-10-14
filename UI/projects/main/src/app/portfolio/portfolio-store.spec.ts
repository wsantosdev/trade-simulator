import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { Asset } from './models';
import { PortfolioApi } from './portfolio-api';
import { PortfolioHub } from './portfolio-hub';
import { PortfolioStore } from './portfolio-store';

const account = '01a13323-857f-754a-b009-6f8a317a186a';
const asset = (ticker: string, available: number, blocked: number): Asset => ({
  ticker,
  available,
  blocked,
  total: available + blocked,
});

describe('PortfolioStore', () => {
  let store: PortfolioStore;
  const search = vi.fn();

  beforeEach(() => {
    search.mockReset();
    search.mockReturnValue(of({ items: [asset('PETR4', 70, 30)], page: 1, pageSize: 50, totalCount: 1 }));
    TestBed.configureTestingModule({
      providers: [
        { provide: PortfolioApi, useValue: { search } },
        { provide: PortfolioHub, useValue: { subscribe: async () => {}, start: async () => {}, stop: async () => {} } },
      ],
    });
    store = TestBed.inject(PortfolioStore);
    store.setAccount(account);
  });

  it('loads the informed account and clears the list when the number becomes invalid', () => {
    expect(search).toHaveBeenCalledTimes(1);
    expect(store.assets().length).toBe(1);
    store.setAccount('abc');
    expect(store.accountId()).toBeNull();
    expect(store.assets()).toEqual([]);
  });

  it('updates an existing asset in place', () => {
    store.assetChanged(asset('PETR4', 50, 50));
    expect(store.assets()[0].blocked).toBe(50);
    expect(store.totalCount()).toBe(1);
  });

  it('adds a new asset keeping the current sort', () => {
    store.assetChanged(asset('ABEV3', 10, 0));
    expect(store.assets().map((a) => a.ticker)).toEqual(['ABEV3', 'PETR4']);
    expect(store.totalCount()).toBe(2);
  });

  it('removes the asset when the total reaches zero', () => {
    store.assetChanged(asset('PETR4', 0, 0));
    expect(store.assets()).toEqual([]);
    expect(store.totalCount()).toBe(0);
  });

  it('toggles the sort direction on the same field and resets to ascending on a new one', () => {
    store.sort('ticker');
    expect(store.descending()).toBe(true);
    store.sort('total');
    expect(store.sortBy()).toBe('total');
    expect(store.descending()).toBe(false);
  });
});
