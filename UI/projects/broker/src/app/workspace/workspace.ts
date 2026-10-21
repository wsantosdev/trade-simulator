import { ChangeDetectionStrategy, Component, ElementRef, OnDestroy, OnInit, computed, inject } from '@angular/core';
import { AccountWindow } from '../windows/account-window';
import { OfferBookWindow } from '../windows/offerbook-window';
import { OrdersWindow } from '../windows/orders-window';
import { PortfolioWindow } from '../windows/portfolio-window';
import { QuoteWindow } from '../windows/quote-window';
import { TicketWindow } from '../windows/ticket-window';
import { WindowFrame } from './window-frame';
import { WindowManager } from './window-manager';
import { DockZone, Rect } from './window-model';

interface Splitter {
  zone: DockZone;
  style: Rect;
  vertical: boolean;
}

@Component({
  selector: 'app-workspace',
  imports: [WindowFrame, AccountWindow, OfferBookWindow, OrdersWindow, PortfolioWindow, QuoteWindow, TicketWindow],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'relative block h-full w-full overflow-hidden bg-slate-200', role: 'region', 'aria-label': 'Área de trabalho' },
  template: `
    @if (manager.windows().length === 0) {
      <div class="absolute inset-0 grid place-items-center p-6 text-center text-slate-600">
        <div>
          <p class="text-lg font-semibold text-slate-800">Nenhuma janela aberta</p>
          <p class="mt-1 text-sm">Use o menu <strong>Janelas</strong> para abrir cotações, boletas e demais painéis.</p>
        </div>
      </div>
    }

    @for (item of manager.windows(); track item.id) {
      <app-window-frame [state]="item">
        @switch (item.kind) {
          @case ('account') { <app-account-window /> }
          @case ('quote') { <app-quote-window [window]="item" /> }
          @case ('ticket') { <app-ticket-window [window]="item" /> }
          @case ('orders') { <app-orders-window /> }
          @case ('portfolio') { <app-portfolio-window /> }
          @case ('offerbook') { <app-offerbook-window [window]="item" /> }
        }
      </app-window-frame>
    }

    @for (splitter of splitters(); track splitter.zone) {
      <div
        class="absolute z-[5] hover:bg-emerald-500/60"
        [class]="splitter.vertical ? 'cursor-col-resize' : 'cursor-row-resize'"
        [style.left.px]="splitter.style.x"
        [style.top.px]="splitter.style.y"
        [style.width.px]="splitter.style.width"
        [style.height.px]="splitter.style.height"
        style="touch-action: none"
        role="separator"
        [attr.aria-orientation]="splitter.vertical ? 'vertical' : 'horizontal'"
        [attr.aria-label]="'Redimensionar doca ' + label(splitter.zone)"
        (pointerdown)="startSplit($event)"
        (pointermove)="split($event, splitter.zone)"
        (pointerup)="endSplit($event)"
        (pointercancel)="endSplit($event)"
      ></div>
    }

    @if (manager.dragPreview(); as preview) {
      <div
        class="pointer-events-none absolute z-[500] rounded-md border-2 border-dashed border-emerald-600 bg-emerald-500/20"
        [style.left.px]="preview.rect.x"
        [style.top.px]="preview.rect.y"
        [style.width.px]="preview.rect.width"
        [style.height.px]="preview.rect.height"
        data-testid="dock-preview"
      ></div>
    }
  `,
})
export class Workspace implements OnInit, OnDestroy {
  protected readonly manager = inject(WindowManager);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private observer?: ResizeObserver;
  private splitting = false;

  protected readonly splitters = computed<Splitter[]>(() => {
    const area = this.manager.area();
    const sizes = this.manager.effectiveDockSizes();
    const occupied = this.manager.occupiedZones();
    const left = occupied.has('left') ? sizes.left : 0;
    const right = occupied.has('right') ? sizes.right : 0;
    const thickness = 6;
    const result: Splitter[] = [];
    if (left) result.push({ zone: 'left', vertical: true, style: { x: left - thickness / 2 + 1, y: 0, width: thickness, height: area.height } });
    if (right) result.push({ zone: 'right', vertical: true, style: { x: area.width - right - thickness / 2 - 1, y: 0, width: thickness, height: area.height } });
    if (occupied.has('bottom')) {
      result.push({
        zone: 'bottom',
        vertical: false,
        style: { x: left, y: area.height - sizes.bottom - thickness / 2 - 1, width: area.width - left - right, height: thickness },
      });
    }
    return result;
  });

  protected label(zone: DockZone): string {
    return zone === 'left' ? 'esquerda' : zone === 'right' ? 'direita' : 'inferior';
  }

  ngOnInit(): void {
    const measure = () => {
      const box = this.host.getBoundingClientRect();
      this.manager.setArea({ width: Math.round(box.width), height: Math.round(box.height) });
    };
    measure();
    if (this.manager.windows().length === 0) this.manager.resetLayout();
    this.observer = new ResizeObserver(measure);
    this.observer.observe(this.host);
  }

  ngOnDestroy(): void {
    this.observer?.disconnect();
  }

  protected startSplit(event: PointerEvent): void {
    (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
    this.splitting = true;
  }

  protected split(event: PointerEvent, zone: DockZone): void {
    if (!this.splitting) return;
    const box = this.host.getBoundingClientRect();
    if (zone === 'left') this.manager.resizeDock('left', event.clientX - box.left);
    else if (zone === 'right') this.manager.resizeDock('right', box.right - event.clientX);
    else this.manager.resizeDock('bottom', box.bottom - event.clientY);
  }

  protected endSplit(event: PointerEvent): void {
    this.splitting = false;
    (event.currentTarget as HTMLElement).releasePointerCapture?.(event.pointerId);
  }
}
