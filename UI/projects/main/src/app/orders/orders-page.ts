import { CurrentAccount } from '../shared/current-account';
import { MainMenu } from '../shared/main-menu/main-menu';
import { Component, OnDestroy, OnInit, effect, inject } from '@angular/core';
import { OrderList } from './order-list/order-list';
import { OrderTicket } from './order-ticket/order-ticket';
import { OrdersStore } from './orders-store';

@Component({
  imports: [MainMenu, OrderTicket, OrderList],
  selector: 'app-orders-page',
  templateUrl: './orders-page.html',
})
export class OrdersPage implements OnInit, OnDestroy {
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
