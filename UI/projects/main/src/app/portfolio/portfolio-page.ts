import { CurrentAccount } from '../shared/current-account';
import { MainMenu } from '../shared/main-menu/main-menu';
import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, effect, inject } from '@angular/core';
import { AssetList } from './asset-list/asset-list';
import { PortfolioStore } from './portfolio-store';

@Component({
  imports: [MainMenu, AssetList],
  selector: 'app-portfolio-page',
  templateUrl: './portfolio-page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PortfolioPage implements OnInit, OnDestroy {
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
