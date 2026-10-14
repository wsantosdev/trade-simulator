import { Injectable, computed, inject, signal } from '@angular/core';
import { Asset, SortField, isAccountNumber, pageSize } from './models';
import { PortfolioApi, describeError } from './portfolio-api';
import { PortfolioHub, PortfolioHubListener } from './portfolio-hub';

@Injectable({ providedIn: 'root' })
export class PortfolioStore implements PortfolioHubListener {
  private readonly api = inject(PortfolioApi);
  private readonly hub = inject(PortfolioHub);
  private loadVersion = 0;

  readonly accountId = signal<string | null>(null);
  readonly assets = signal<Asset[]>([]);
  readonly totalCount = signal(0);
  readonly page = signal(1);
  readonly sortBy = signal<SortField>('ticker');
  readonly descending = signal(false);
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
    this.assets.set([]);
    this.totalCount.set(0);
    this.page.set(1);
    this.errorMessage.set('');
    if (normalized) {
      void this.hub.subscribe(normalized);
      this.reload();
    }
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
      .search({ accountId, sortBy: this.sortBy(), descending: this.descending(), page: this.page() })
      .subscribe({
        next: (result) => {
          if (version !== this.loadVersion) return;
          this.assets.set(result.items);
          this.totalCount.set(result.totalCount);
          this.loading.set(false);
        },
        error: (error: unknown) => {
          if (version !== this.loadVersion) return;
          this.loading.set(false);
          this.errorMessage.set(describeError(error));
        },
      });
  }

  // Total = 0 means the asset left the portfolio; any other change updates or adds the position.
  assetChanged(asset: Asset): void {
    const exists = this.assets().some((existing) => existing.ticker === asset.ticker);

    if (asset.total === 0) {
      if (!exists) return;
      this.assets.update((assets) => assets.filter((existing) => existing.ticker !== asset.ticker));
      this.totalCount.update((count) => Math.max(0, count - 1));
      return;
    }

    if (exists) {
      this.assets.update((assets) => assets.map((existing) => (existing.ticker === asset.ticker ? asset : existing)));
      return;
    }

    this.totalCount.update((count) => count + 1);
    if (this.page() === 1) {
      this.assets.update((assets) => this.sorted([...assets, asset]).slice(0, pageSize));
    }
  }

  reconnected(): void {
    this.reload();
  }

  connectionChanged(connected: boolean): void {
    this.connected.set(connected);
  }

  private sorted(assets: Asset[]): Asset[] {
    const field = this.sortBy();
    const direction = this.descending() ? -1 : 1;
    return [...assets].sort((left, right) => {
      const a = left[field];
      const b = right[field];
      if (a === b) return 0;
      return (a < b ? -1 : 1) * direction;
    });
  }
}
