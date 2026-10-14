import { Quote } from './models';
import { QuotesHubListener } from './quotes-hub';

export class FakeQuotesHub {
  listener?: QuotesHubListener;
  subscribed: string[] = [];
  unsubscribed: string[] = [];
  initial: Quote | null = null;
  failWith?: Error;

  async start(listener: QuotesHubListener): Promise<void> {
    this.listener = listener;
    listener.connectionChanged(true);
  }
  async subscribe(ticker: string): Promise<Quote | null> {
    this.subscribed.push(ticker);
    if (this.failWith) throw this.failWith;
    return this.initial;
  }
  async unsubscribe(ticker: string): Promise<void> {
    this.unsubscribed.push(ticker);
  }
  async stop(): Promise<void> {}
}

export const quote = (last: number, variation: Quote['variation'] = 'Up', ticker = 'PETR4'): Quote => ({
  ticker,
  open: 10,
  close: 10,
  high: 12,
  low: 9,
  last,
  variation,
  timestamp: '2026-01-05T10:00:00-03:00',
});
