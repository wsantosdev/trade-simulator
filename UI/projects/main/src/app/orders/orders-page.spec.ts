import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { OrdersPage } from './orders-page';
import { OrdersHub } from './orders-hub';
import { QuotesHub } from './quotes-hub';
import { OfferBookHub } from './offerbook-hub';

describe('OrdersPage', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [OrdersPage],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: OfferBookHub, useValue: { start: async () => {}, stop: async () => {}, isConnected: false } },
        { provide: QuotesHub, useValue: { start: async () => {}, stop: async () => {}, isConnected: false } },
        { provide: OrdersHub, useValue: { start: async () => {}, stop: async () => {}, subscribe: async () => {} } },
      ],
    }).compileComponents();
  });

  it('renders the ticket and the order list', async () => {
    const fixture = TestBed.createComponent(OrdersPage);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('h1')?.textContent).toContain('Suas ordens');
    expect(element.querySelector('app-order-ticket')).not.toBeNull();
    expect(element.querySelector('app-order-list')).not.toBeNull();
  });
});
