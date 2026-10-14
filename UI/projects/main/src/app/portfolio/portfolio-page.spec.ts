import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { PortfolioPage } from './portfolio-page';
import { PortfolioHub } from './portfolio-hub';

describe('PortfolioPage', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PortfolioPage],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        { provide: PortfolioHub, useValue: { subscribe: async () => {}, start: async () => {}, stop: async () => {} } },
      ],
    }).compileComponents();
  });

  it('renders the page header and status pill', async () => {
    const fixture = TestBed.createComponent(PortfolioPage);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('h1')?.textContent).toContain('Suas posições');
    expect(element.querySelector('[role="status"]')?.textContent).toContain('Tempo real desconectado');
  });
});
