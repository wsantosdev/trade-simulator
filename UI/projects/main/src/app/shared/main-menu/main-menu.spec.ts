import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Component } from '@angular/core';
import { MainMenu } from './main-menu';
import { CurrentAccount } from '../current-account';

@Component({ imports: [MainMenu], template: '<app-main-menu subtitle="Teste" />' })
class Host {}

describe('MainMenu', () => {
  it('links to every screen except Exchange', async () => {
    await TestBed.configureTestingModule({
      imports: [Host],
      providers: [provideRouter([])],
    }).compileComponents();
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    el.querySelector<HTMLButtonElement>('button[aria-label="Menu"]')!.click();
    await fixture.whenStable();
    const hrefs = Array.from(el.querySelectorAll('a')).map((a) => a.getAttribute('href'));
    for (const path of ['/account', '/orders', '/portfolio', '/quotes', '/offerbook']) {
      expect(hrefs).toContain(path);
    }
    expect(hrefs.some((h) => h?.includes('exchange'))).toBe(false);
    expect(el.textContent).toContain('Teste');
  });
});

describe('MainMenu (hambúrguer e conta)', () => {
  beforeEach(() => sessionStorage.clear());

  it('opens the panel with links and shows the current account', async () => {
    await TestBed.configureTestingModule({ imports: [Host], providers: [provideRouter([])] }).compileComponents();
    const id = '01a13323-857f-754a-b009-6f8a317a186a';
    TestBed.inject(CurrentAccount).set(id);
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="current-account"]')!.textContent).toContain(id);
    expect(el.querySelectorAll("a").length).toBe(1);
    el.querySelector<HTMLButtonElement>('button[aria-label="Menu"]')!.click();
    await fixture.whenStable();
    expect(el.querySelectorAll('a').length).toBe(6);
  });
});
