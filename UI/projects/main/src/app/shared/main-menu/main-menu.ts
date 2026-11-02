import { ChangeDetectionStrategy, Component, HostListener, inject, input, signal } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { ThemeToggle } from '../../../../../../shared/theme-toggle';
import { CurrentAccount } from '../current-account';

interface MenuItem {
  readonly path: string;
  readonly label: string;
}

@Component({
  selector: 'app-main-menu',
  imports: [RouterLink, RouterLinkActive, ThemeToggle],
  templateUrl: './main-menu.html',
  host: { class: 'relative block' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MainMenu {
  protected readonly account = inject(CurrentAccount);
  readonly subtitle = input.required<string>();
  protected readonly open = signal(false);

  protected readonly items: readonly MenuItem[] = [
    { path: '/account', label: 'Conta corrente' },
    { path: '/orders', label: 'Ordens' },
    { path: '/portfolio', label: 'Portfólio' },
    { path: '/quotes', label: 'Cotações' },
    { path: '/offerbook', label: 'Livro de ofertas' },
  ];

  @HostListener('document:keydown.escape')
  protected close(): void {
    this.open.set(false);
  }
}
