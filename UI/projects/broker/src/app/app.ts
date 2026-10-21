import { ChangeDetectionStrategy, Component, ElementRef, HostListener, computed, inject, signal } from '@angular/core';
import { ThemeToggle } from '../../../../shared/theme-toggle';
import { CurrentAccount } from '../../../main/src/app/shared/current-account';
import { Workspace } from './workspace/workspace';
import { WindowManager } from './workspace/window-manager';
import { WINDOW_KINDS, WINDOW_SPECS, WindowKind } from './workspace/window-model';

@Component({
  selector: 'app-root',
  imports: [Workspace, ThemeToggle],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex h-screen flex-col bg-[#f6f8f7] text-slate-900">
      <header class="flex h-14 shrink-0 items-center gap-3 border-b border-slate-200 bg-white px-3">
        <a class="flex items-center gap-2" href="/" aria-label="Trade Simulator início">
          <span class="grid size-9 shrink-0 place-items-center rounded-xl bg-emerald-700 text-white shadow-sm">
            <svg viewBox="0 0 24 24" fill="none" class="size-5" aria-hidden="true">
              <path d="M4 17.5 9 12l3.5 3 7.5-8" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" />
              <path d="M15 7h5v5" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" />
            </svg>
          </span>
          <span class="hidden sm:block">
            <span class="block text-sm font-bold leading-4 tracking-tight">Trade Simulator</span>
            <span class="block text-xs text-slate-500">Home broker</span>
          </span>
        </a>

        <nav class="ml-2 flex items-center gap-2" aria-label="Janelas">
          <div class="relative">
            <button
              type="button"
              id="windows-menu-button"
              class="inline-flex items-center gap-1.5 rounded-lg border border-slate-300 px-3 py-2 text-sm font-semibold text-slate-800 hover:bg-slate-100 focus:outline-none focus:ring-2 focus:ring-emerald-600"
              aria-haspopup="menu"
              [attr.aria-expanded]="menuOpen()"
              aria-controls="windows-menu"
              (click)="menuOpen.set(!menuOpen())"
            >
              Janelas
              <svg viewBox="0 0 16 16" class="size-3.5" fill="none" stroke="currentColor" stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m4 6 4 4 4-4" /></svg>
            </button>
            @if (menuOpen()) {
              <div id="windows-menu" role="menu" aria-labelledby="windows-menu-button" class="absolute left-0 top-full z-[1000] mt-1 w-80 rounded-lg border border-slate-200 bg-white p-1 shadow-xl">
                @for (item of items(); track item.kind) {
                  <button
                    type="button"
                    role="menuitem"
                    class="flex w-full items-center gap-3 rounded-md px-3 py-2 text-left hover:bg-slate-100 focus:bg-slate-100 focus:outline-none disabled:cursor-not-allowed disabled:opacity-50 disabled:hover:bg-transparent"
                    [disabled]="item.disabled"
                    [attr.data-testid]="'menu-' + item.kind"
                    (click)="choose(item.kind)"
                  >
                    <span class="min-w-0 flex-1">
                      <span class="block text-sm font-semibold text-slate-900">{{ item.label }}</span>
                      <span class="block truncate text-xs text-slate-500">{{ item.description }}</span>
                    </span>
                    <span
                      class="shrink-0 rounded-full px-2 py-0.5 text-xs font-semibold"
                      [class]="item.count ? 'bg-emerald-100 text-emerald-800' : 'bg-slate-100 text-slate-600'"
                    >
                      {{ item.status }}
                    </span>
                  </button>
                }
                <div class="my-1 border-t border-slate-200"></div>
                <button type="button" role="menuitem" class="w-full rounded-md px-3 py-2 text-left text-sm font-medium text-slate-700 hover:bg-slate-100 focus:bg-slate-100 focus:outline-none" (click)="reset()">
                  Restaurar layout padrão
                </button>
                <button type="button" role="menuitem" class="w-full rounded-md px-3 py-2 text-left text-sm font-medium text-slate-700 hover:bg-slate-100 focus:bg-slate-100 focus:outline-none" (click)="closeAll()">
                  Fechar todas as janelas
                </button>
              </div>
            }
          </div>

          <button
            type="button"
            class="rounded-lg bg-emerald-700 px-3 py-2 text-sm font-semibold text-white hover:bg-emerald-800 focus:outline-none focus:ring-2 focus:ring-emerald-600 focus:ring-offset-1 disabled:cursor-not-allowed disabled:opacity-50"
            [disabled]="!manager.canOpen('quote')"
            [title]="manager.canOpen('quote') ? 'Abrir nova cotação' : 'Limite de 10 cotações abertas'"
            data-testid="quick-quote"
            (click)="manager.open('quote')"
          >
            Cotação +
          </button>
          <button
            type="button"
            class="rounded-lg bg-emerald-700 px-3 py-2 text-sm font-semibold text-white hover:bg-emerald-800 focus:outline-none focus:ring-2 focus:ring-emerald-600 focus:ring-offset-1 disabled:cursor-not-allowed disabled:opacity-50"
            [disabled]="!manager.canOpen('ticket')"
            [title]="manager.canOpen('ticket') ? 'Abrir nova boleta' : 'Limite de 3 boletas abertas'"
            data-testid="quick-ticket"
            (click)="manager.open('ticket')"
          >
            Boleta +
          </button>
        </nav>

        <div class="ml-auto flex items-center gap-3">
          <span class="hidden rounded-full border border-slate-200 bg-slate-50 px-3 py-1.5 text-xs font-semibold text-slate-700 md:inline" role="status" aria-live="polite">
            @if (accountId(); as id) {
              Conta {{ id.slice(0, 8) }}…
            } @else {
              Nenhuma conta carregada
            }
          </span>
          <app-theme-toggle />
        </div>
      </header>

      <main class="min-h-0 flex-1"><app-workspace /></main>
    </div>
  `,
})
export class App {
  protected readonly manager = inject(WindowManager);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  protected readonly accountId = inject(CurrentAccount).accountId;
  protected readonly menuOpen = signal(false);

  protected readonly items = computed(() =>
    WINDOW_KINDS.map((kind) => {
      const spec = WINDOW_SPECS[kind];
      const count = this.manager.counts()[kind];
      const full = count >= spec.limit;
      return {
        kind,
        label: spec.label,
        description: full && spec.limit > 1 ? `Limite de ${spec.limit} janelas atingido` : spec.description,
        count,
        disabled: full && spec.limit > 1,
        status: spec.limit === 1 ? (count ? 'Aberta' : 'Fechada') : `${count}/${spec.limit}`,
      };
    }),
  );

  protected choose(kind: WindowKind): void {
    this.manager.open(kind);
    this.menuOpen.set(false);
  }

  protected reset(): void {
    this.manager.resetLayout();
    this.menuOpen.set(false);
  }

  protected closeAll(): void {
    this.manager.closeAll();
    this.menuOpen.set(false);
  }

  @HostListener('document:pointerdown', ['$event'])
  protected onPointerDown(event: Event): void {
    if (this.menuOpen() && !(event.target as Element).closest('#windows-menu, #windows-menu-button')) {
      this.menuOpen.set(false);
    }
  }

  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    if (!this.menuOpen()) return;
    this.menuOpen.set(false);
    this.host.querySelector<HTMLElement>('#windows-menu-button')?.focus();
  }
}
