import { Injectable } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import { OfferBookDelta, OfferBookSnapshot } from './models';

export interface OfferBookHubListener {
  snapshotReceived(snapshot: OfferBookSnapshot): void;
  deltaReceived(delta: OfferBookDelta): void;
  reconnected(): void;
  connectionChanged(connected: boolean): void;
}

@Injectable({ providedIn: 'root' })
export class OfferBookHub {
  private connection?: HubConnection;

  async start(listener: OfferBookHubListener): Promise<void> {
    if (this.connection) return;

    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/offerbook')
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10000])
      .build();
    this.connection = connection;

    connection.on('OfferBookSnapshot', (snapshot: OfferBookSnapshot) => listener.snapshotReceived(snapshot));
    connection.on('OfferBookDelta', (delta: OfferBookDelta) => listener.deltaReceived(delta));
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

  subscribe(ticker: string): Promise<OfferBookSnapshot | null> {
    return this.connection!.invoke<OfferBookSnapshot | null>('Subscribe', ticker);
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
