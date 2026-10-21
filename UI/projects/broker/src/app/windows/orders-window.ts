import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, effect, inject } from '@angular/core';
import { CurrentAccount } from '../../../../main/src/app/shared/current-account';
import { OrderList } from '../../../../main/src/app/orders/order-list/order-list';
import { OrdersStore } from '../../../../main/src/app/orders/orders-store';
import { LiveBadge } from './live-badge';

@Component({
  selector: 'app-orders-window',
  imports: [OrderList, LiveBadge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex min-h-full flex-col">
      <div class="flex-1"><app-order-list /></div>
      <div class="px-3 pb-2"><app-live-badge [connected]="store.connected()" /></div>
    </div>
  `,
})
export class OrdersWindow implements OnInit, OnDestroy {
  protected readonly store = inject(OrdersStore);
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
