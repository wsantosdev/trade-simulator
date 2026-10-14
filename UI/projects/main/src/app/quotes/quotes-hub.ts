import { Injectable } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import { Quote } from './models';

export interface QuotesHubListener {
  quoteUpdated(quote: Quote): void;
  reconnected(): void;
  connectionChanged(connected: boolean): void;
}

@Injectable({ providedIn: 'root' })
export class QuotesHub {
  private connection?: HubConnection;

  async start(listener: QuotesHubListener): Promise<void> {
    if (this.connection) return;

    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/quotes')
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10000])
      .build();
    this.connection = connection;

    connection.on('QuoteUpdated', (quote: Quote) => listener.quoteUpdated(quote));
    connection.onreconnecting(() => listener.connectionChanged(false));
    connection.onreconnected(() => {
      listener.connectionChanged(true);
      listener.reconnected();
    });
    connection.onclose(() => listener.connectionChanged(false));

    while (this.connection === connection) {
      try {
        await connection.start();
        listener.connectionChanged(true);
        listener.reconnected();
        return;
      } catch {
        listener.connectionChanged(false);
        await new Promise((resolve) => setTimeout(resolve, 5000));
      }
    }
  }

  get isConnected(): boolean {
    return this.connection?.state === HubConnectionState.Connected;
  }

  subscribe(ticker: string): Promise<Quote | null> {
    return this.connection!.invoke<Quote | null>('Subscribe', ticker);
  }

  async unsubscribe(ticker: string): Promise<void> {
    if (this.isConnected) await this.connection!.invoke('Unsubscribe', ticker);
  }

  stop(): Promise<void> {
    const connection = this.connection;
    this.connection = undefined;
    return connection?.stop() ?? Promise.resolve();
  }
}
