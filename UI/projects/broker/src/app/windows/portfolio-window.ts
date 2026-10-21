import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, effect, inject } from '@angular/core';
import { CurrentAccount } from '../../../../main/src/app/shared/current-account';
import { AssetList } from '../../../../main/src/app/portfolio/asset-list/asset-list';
import { PortfolioStore } from '../../../../main/src/app/portfolio/portfolio-store';
import { LiveBadge } from './live-badge';

@Component({
  selector: 'app-portfolio-window',
  imports: [AssetList, LiveBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex min-h-full flex-col">
      <div class="flex-1"><app-asset-list /></div>
      <div class="px-3 pb-2"><app-live-badge [connected]="store.connected()" /></div>
    </div>
  `,
})
export class PortfolioWindow implements OnInit, OnDestroy {
  protected readonly store = inject(PortfolioStore);
  private readonly currentAccount = inject(CurrentAccount);

  constructor() {
    effect(() => this.store.setAccount(this.currentAccount.accountId()));
  }

  ngOnInit(): void {
    this.store.start();
  }

  ngOnDestroy(): void {
    this.store.stop();
  }
}
