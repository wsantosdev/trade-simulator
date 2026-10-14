import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { OfferBookPage } from './offerbook-page';
import { ask, bid, delta, FakeOfferBookHub, level, snapshot } from './fake-hub';
import { OfferBookHub } from './offerbook-hub';
import { OfferBookStore } from './offerbook-store';

describe('OfferBookPage', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [OfferBookPage],
      providers: [
        provideRouter([]),{ provide: OfferBookHub, useValue: new FakeOfferBookHub() }],
    }).compileComponents();
  });

  it('renders the header, status pill and ticker field', async () => {
    const fixture = TestBed.createComponent(OfferBookPage);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('h1')?.textContent).toContain('Livro de ofertas');
    expect(element.textContent).toContain('Tempo real conectado');
    expect(element.querySelector('#ticker')).not.toBeNull();
  });

  it('renders both sides, best prices, spread and flash classes', async () => {
    const fixture = TestBed.createComponent(OfferBookPage);
    await fixture.whenStable();
    const store = TestBed.inject(OfferBookStore);
    await store.setTicker('PETR4');
    store.snapshotReceived(snapshot(1, [level(10, 100, 2), level(9.99, 50)], [level(10.02, 80), level(10.03, 40)]));
    store.deltaReceived(delta(2, [bid(10, 300), ask(10.02, 10)]));
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    const bids = element.querySelectorAll('[data-testid="bid-row"]');
    const asks = element.querySelectorAll('[data-testid="ask-row"]');
    expect(bids.length).toBe(2);
    expect(asks.length).toBe(2);
    expect(bids[0].textContent).toContain('10,00');
    expect(bids[0].className).toContain('animate-book-flash-up');
    expect(asks[0].className).toContain('animate-book-flash-down');
    expect(bids[1].className).not.toContain('animate-flash');
    expect(element.querySelector('[data-testid="best-bid"]')?.textContent).toContain('10,00');
    expect(element.querySelector('[data-testid="best-ask"]')?.textContent).toContain('10,02');
    expect(element.querySelector('[data-testid="spread"]')?.textContent).toContain('0,02');
    expect(element.querySelector('[data-testid="phase"]')?.textContent).toContain('Aberto');
    expect(element.querySelector('[data-testid="frozen-notice"]')).toBeNull();
    expect((bids[0] as HTMLElement).style.backgroundImage).toContain('100%');
  });

  it('warns that the book is frozen outside the session', async () => {
    const fixture = TestBed.createComponent(OfferBookPage);
    await fixture.whenStable();
    const store = TestBed.inject(OfferBookStore);
    await store.setTicker('PETR4');
    store.snapshotReceived(snapshot(1, [level(10, 100)], [level(10.1, 100)], 'PreOpen'));
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('[data-testid="phase"]')?.textContent).toContain('Pré-abertura');
    expect(element.querySelector('[data-testid="frozen-notice"]')?.textContent).toContain('congelado');
  });
});
