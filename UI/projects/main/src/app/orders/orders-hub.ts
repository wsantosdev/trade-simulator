import { Injectable } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import { Order, OrderChanges } from './models';

export interface OrdersHubListener {
  created(order: Order): void;
  updated(changes: OrderChanges): void;
  reconnected(): void;
  connectionChanged(connected: boolean): void;
}

@Injectable({ providedIn: 'root' })
export class OrdersHub {
  private connection?: HubConnection;
  private subscribedAccount?: string;

  async start(listener: OrdersHubListener): Promise<void> {
    if (this.connection) return;

    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/orders')
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10000])
      .build();
    this.connection = connection;

    connection.on('OrderCreated', (order: Order) => listener.created(order));
    connection.on('OrderUpdated', (changes: OrderChanges) => listener.updated(changes));
    connection.onreconnecting(() => listener.connectionChanged(false));
    connection.onreconnected(async () => {
      await this.subscribeCurrent();
      listener.connectionChanged(true);
      listener.reconnected();
    });
    connection.onclose(() => listener.connectionChanged(false));

    while (this.connection === connection) {
      try {
        await connection.start();
        listener.connectionChanged(true);
        await this.subscribeCurrent();
        listener.reconnected();
        return;
      } catch {
        listener.connectionChanged(false);
        await new Promise((resolve) => setTimeout(resolve, 5000));
      }
    }
  }

  async subscribe(accountId: string): Promise<void> {
    const previous = this.subscribedAccount;
    this.subscribedAccount = accountId;
    if (!this.isConnected) return;
    if (previous && previous !== accountId) {
      await this.connection!.invoke('UnsubscribeAccount', previous);
    }
    await this.connection!.invoke('SubscribeAccount', accountId);
  }

  stop(): Promise<void> {
    const connection = this.connection;
    this.connection = undefined;
    return connection?.stop() ?? Promise.resolve();
  }

  private get isConnected(): boolean {
    return this.connection?.state === HubConnectionState.Connected;
  }

  private async subscribeCurrent(): Promise<void> {
    if (this.subscribedAccount && this.isConnected) {
      await this.connection!.invoke('SubscribeAccount', this.subscribedAccount);
    }
  }
}
