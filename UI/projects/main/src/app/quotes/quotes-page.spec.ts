import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { QuotesPage } from './quotes-page';
import { FakeQuotesHub, quote } from './fake-hub';
import { QuotesHub } from './quotes-hub';
import { QuotesStore } from './quotes-store';

describe('QuotesPage', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [QuotesPage],
      providers: [
        provideRouter([]),{ provide: QuotesHub, useValue: new FakeQuotesHub() }],
    }).compileComponents();
  });

  it('renders the header, status pill and ticker field', async () => {
    const fixture = TestBed.createComponent(QuotesPage);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('h1')?.textContent).toContain('Cotações');
    expect(element.textContent).toContain('Tempo real conectado');
    expect(element.querySelector('#ticker')).not.toBeNull();
  });

  it('shows the last price with flash class and day change', async () => {
    const fixture = TestBed.createComponent(QuotesPage);
    await fixture.whenStable();
    const store = TestBed.inject(QuotesStore);
    await store.setTicker('PETR4');
    store.quoteUpdated(quote(10.5, 'Up'));
    store.quoteUpdated(quote(10.6, 'Up'));
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    const last = element.querySelector('[data-testid="last"]')!;
    expect(last.textContent).toContain('10,60');
    expect(last.className).toContain('animate-flash-up');
    expect(element.querySelector('[data-testid="day-change"]')?.textContent).toContain('6,00%');
    expect(element.querySelector('svg')).not.toBeNull();
  });
});
