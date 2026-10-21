import { ChangeDetectionStrategy, Component, signal } from '@angular/core';

const STORAGE_KEY = 'theme';

@Component({
  selector: 'app-theme-toggle',
  template: `
    <button
      type="button"
      class="grid size-10 shrink-0 place-items-center rounded-lg border border-slate-300 text-slate-700 hover:bg-slate-100 focus:outline-none focus:ring-2 focus:ring-emerald-600"
      [attr.aria-label]="dark() ? 'Mudar para o tema claro' : 'Mudar para o tema escuro'"
      [attr.aria-pressed]="dark()"
      [title]="dark() ? 'Tema claro' : 'Tema escuro'"
      (click)="toggle()"
    >
      @if (dark()) {
        <svg viewBox="0 0 24 24" fill="none" class="size-5" aria-hidden="true">
          <circle cx="12" cy="12" r="4" stroke="currentColor" stroke-width="2" />
          <path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" stroke="currentColor" stroke-width="2" stroke-linecap="round" />
        </svg>
      } @else {
        <svg viewBox="0 0 24 24" fill="none" class="size-5" aria-hidden="true">
          <path d="M20 14.5A8 8 0 0 1 9.5 4 8 8 0 1 0 20 14.5Z" stroke="currentColor" stroke-width="2" stroke-linejoin="round" />
        </svg>
      }
    </button>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ThemeToggle {
  protected readonly dark = signal(document.documentElement.classList.contains('dark'));

  protected toggle(): void {
    const next = !this.dark();
    this.dark.set(next);
    document.documentElement.classList.toggle('dark', next);
    localStorage.setItem(STORAGE_KEY, next ? 'dark' : 'light');
  }
}
