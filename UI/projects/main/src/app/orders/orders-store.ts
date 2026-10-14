import { Injectable, computed, inject, signal } from '@angular/core';
import { OrdersApi, describeError } from './orders-api';
import { OrdersHub, OrdersHubListener } from './orders-hub';
import {
  Order,
  OrderChanges,
  OrderFilters,
  SortField,
  isAccountNumber,
  pageSize,
} from './models';

const emptyFilters: OrderFilters = { ticker: '', state: '', side: '', type: '' };

@Injectable({ providedIn: 'root' })
export class OrdersStore implements OrdersHubListener {
  private readonly api = inject(OrdersApi);
  private readonly hub = inject(OrdersHub);
  private lastTimestamp: string | null = null;
  private loadVersion = 0;

  readonly accountId = signal<string | null>(null);
  readonly orders = signal<Order[]>([]);
  readonly totalCount = signal(0);
  readonly page = signal(1);
  readonly filters = signal<OrderFilters>({ ...emptyFilters });
  readonly sortBy = signal<SortField>('sentAt');
  readonly descending = signal(true);
  readonly loading = signal(false);
  readonly errorMessage = signal('');
  readonly connected = signal(false);
  readonly pageCount = computed(() => Math.max(1, Math.ceil(this.totalCount() / pageSize)));

  start(): void {
    void this.hub.start(this);
  }

  stop(): void {
    void this.hub.stop();
  }

  setAccount(accountId: string | null): void {
    const normalized = accountId && isAccountNumber(accountId) ? accountId.trim().toLowerCase() : null;
    if (normalized === this.accountId()) return;
    this.accountId.set(normalized);
    this.lastTimestamp = null;
    this.orders.set([]);
    this.totalCount.set(0);
    this.page.set(1);
    this.errorMessage.set('');
    if (normalized) {
      void this.hub.subscribe(normalized);
      this.reload();
    }
  }

  setFilters(filters: OrderFilters): void {
    this.filters.set(filters);
    this.page.set(1);
    this.reload();
  }

  sort(field: SortField): void {
    if (this.sortBy() === field) {
      this.descending.update((value) => !value);
    } else {
      this.sortBy.set(field);
      this.descending.set(false);
    }
    this.page.set(1);
    this.reload();
  }

  goToPage(page: number): void {
    this.page.set(Math.min(Math.max(1, page), this.pageCount()));
    this.reload();
  }

  reload(): void {
    const accountId = this.accountId();
    if (!accountId) return;
    const version = ++this.loadVersion;
    this.loading.set(true);
    this.errorMessage.set('');
    this.api
      .search({
        accountId,
        filters: this.filters(),
        sortBy: this.sortBy(),
        descending: this.descending(),
        page: this.page(),
      })
      .subscribe({
        next: (result) => {
          if (version !== this.loadVersion) return;
          this.orders.set(result.items);
          this.totalCount.set(result.totalCount);
          this.lastTimestamp = this.latest(result.items.map((order) => order.lastActionAt), this.lastTimestamp);
          this.loading.set(false);
        },
        error: (error: unknown) => {
          if (version !== this.loadVersion) return;
          this.loading.set(false);
          this.errorMessage.set(describeError(error));
        },
      });
  }

  created(order: Order): void {
    if (order.accountId.toLowerCase() !== this.accountId()) return;
    this.lastTimestamp = this.latest([order.lastActionAt], this.lastTimestamp);
    if (this.orders().some((existing) => existing.id === order.id)) return;
    this.totalCount.update((count) => count + 1);
    if (this.page() === 1 && this.matches(order)) {
      this.orders.update((orders) => this.sorted([order, ...orders]).slice(0, pageSize));
    }
  }

  updated(changes: OrderChanges): void {
    this.lastTimestamp = this.latest([changes.lastActionAt], this.lastTimestamp);
    const present = Object.fromEntries(
      Object.entries(changes).filter(([, value]) => value !== null && value !== undefined),
    );
    this.orders.update((orders) =>
      orders.map((order) => (order.id === changes.id ? { ...order, ...present } : order)),
    );
  }

  // After a reconnection only the orders changed since the last known timestamp are fetched.
  reconnected(): void {
    const accountId = this.accountId();
    if (!accountId) return;
    if (!this.lastTimestamp) {
      this.reload();
      return;
    }
    this.api
      .search({
        accountId,
        filters: this.filters(),
        sortBy: this.sortBy(),
        descending: this.descending(),
        page: 1,
        updatedSince: this.lastTimestamp,
      })
      .subscribe({
        next: (result) => {
          if (result.totalCount === 0) return;
          const changed = new Map(result.items.map((order) => [order.id, order]));
          this.lastTimestamp = this.latest(result.items.map((order) => order.lastActionAt), this.lastTimestamp);
          this.orders.update((orders) => {
            const merged = orders.map((order) => changed.get(order.id) ?? order);
            const known = new Set(orders.map((order) => order.id));
            const added = this.page() === 1 ? result.items.filter((order) => !known.has(order.id)) : [];
            return this.sorted([...added, ...merged]).slice(0, pageSize);
          });
          if (result.totalCount > pageSize) this.reload();
        },
        error: (error: unknown) => this.errorMessage.set(describeError(error)),
      });
  }

  connectionChanged(connected: boolean): void {
    this.connected.set(connected);
  }

  private matches(order: Order): boolean {
    const filters = this.filters();
    return (
      (!filters.ticker || order.ticker === filters.ticker.toUpperCase()) &&
      (!filters.state || order.state === filters.state) &&
      (!filters.side || order.side === filters.side) &&
      (!filters.type || order.type === filters.type)
    );
  }

  private sorted(orders: Order[]): Order[] {
    const field = this.sortBy();
    const direction = this.descending() ? -1 : 1;
    return [...orders].sort((left, right) => {
      const a = left[field];
      const b = right[field];
      if (a === b) return 0;
      if (a === null) return 1;
      if (b === null) return -1;
      return (a < b ? -1 : 1) * direction;
    });
  }

  private latest(values: string[], current: string | null): string | null {
    return values.reduce<string | null>(
      (max, value) => (max === null || Date.parse(value) > Date.parse(max) ? value : max),
      current,
    );
  }
}
