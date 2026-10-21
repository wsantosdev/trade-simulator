import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, input } from '@angular/core';
import { insertionIndex, resizeRect, ResizeDirection, zoneAt } from './layout';
import { WindowManager } from './window-manager';
import { DockZone, Rect, WINDOW_SPECS, WindowState } from './window-model';

const DRAG_THRESHOLD = 4;

const handles: { direction: ResizeDirection; classes: string }[] = [
  { direction: 'n', classes: 'inset-x-2 top-0 h-1.5 cursor-ns-resize' },
  { direction: 's', classes: 'inset-x-2 bottom-0 h-1.5 cursor-ns-resize' },
  { direction: 'e', classes: 'inset-y-2 right-0 w-1.5 cursor-ew-resize' },
  { direction: 'w', classes: 'inset-y-2 left-0 w-1.5 cursor-ew-resize' },
  { direction: 'ne', classes: 'right-0 top-0 size-3 cursor-nesw-resize' },
  { direction: 'nw', classes: 'left-0 top-0 size-3 cursor-nwse-resize' },
  { direction: 'se', classes: 'bottom-0 right-0 size-3 cursor-nwse-resize' },
  { direction: 'sw', classes: 'bottom-0 left-0 size-3 cursor-nesw-resize' },
];

@Component({
  selector: 'app-window-frame',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'absolute block',
    '[style.left.px]': 'rect().x',
    '[style.top.px]': 'rect().y',
    '[style.width.px]': 'rect().width',
    '[style.height.px]': 'rect().height',
    '[style.z-index]': 'zIndex()',
    '(pointerdown)': 'manager.focus(state().id)',
  },
  template: `
    <section
      class="flex h-full flex-col overflow-hidden rounded-md border bg-white text-slate-900"
      [class.border-emerald-600]="active()"
      [class.shadow-xl]="!state().zone"
      [class.border-slate-300]="!active()"
      [attr.aria-label]="title()"
      role="group"
    >
      <header
        class="flex h-9 shrink-0 cursor-grab select-none items-center gap-1 border-b px-2 active:cursor-grabbing"
        [class.bg-emerald-700]="active()"
        [class.text-white]="active()"
        [class.border-emerald-800]="active()"
        [class.bg-slate-100]="!active()"
        [class.text-slate-700]="!active()"
        [class.border-slate-200]="!active()"
        style="touch-action: none"
        (pointerdown)="startDrag($event)"
        (pointermove)="drag($event)"
        (pointerup)="endDrag($event)"
        (pointercancel)="endDrag($event)"
        (dblclick)="toggleFloat()"
      >
        <span class="min-w-0 flex-1 truncate text-sm font-semibold">{{ title() }}</span>
        @for (zone of zones; track zone.id) {
          <button
            type="button"
            class="grid size-6 place-items-center rounded text-xs hover:bg-black/10 focus:outline-none focus:ring-2 focus:ring-white/70"
            [class.ring-2]="state().zone === zone.id"
            [class.ring-current]="state().zone === zone.id"
            [attr.aria-pressed]="state().zone === zone.id"
            [attr.aria-label]="zone.label + ' ' + title()"
            [title]="state().zone === zone.id ? 'Soltar (flutuar)' : zone.label"
            (pointerdown)="$event.stopPropagation()"
            (click)="dock(zone.id)"
          >
            <svg viewBox="0 0 16 16" class="size-4" fill="none" stroke="currentColor" stroke-width="1.5" aria-hidden="true">
              <rect x="1.75" y="2.75" width="12.5" height="10.5" rx="1" />
              <rect [attr.x]="zone.x" [attr.y]="zone.y" [attr.width]="zone.w" [attr.height]="zone.h" fill="currentColor" stroke="none" />
            </svg>
          </button>
        }
        <button
          type="button"
          class="grid size-6 place-items-center rounded hover:bg-red-600 hover:text-white focus:outline-none focus:ring-2 focus:ring-white/70"
          [attr.aria-label]="'Fechar ' + title()"
          title="Fechar"
          (pointerdown)="$event.stopPropagation()"
          (click)="manager.close(state().id)"
        >
          <svg viewBox="0 0 16 16" class="size-4" fill="none" stroke="currentColor" stroke-width="1.75" stroke-linecap="round" aria-hidden="true">
            <path d="m4 4 8 8M12 4l-8 8" />
          </svg>
        </button>
      </header>
      <div class="win-body min-h-0 flex-1 overflow-auto bg-[#f6f8f7]">
        <ng-content />
      </div>
    </section>
    @if (!state().zone) {
      @for (handle of handles; track handle.direction) {
        <div
          class="absolute z-10"
          [class]="handle.classes"
          style="touch-action: none"
          (pointerdown)="startResize($event, handle.direction)"
          (pointermove)="resize($event)"
          (pointerup)="endResize($event)"
          (pointercancel)="endResize($event)"
        ></div>
      }
    }
  `,
})
export class WindowFrame {
  readonly state = input.required<WindowState>();
  protected readonly manager = inject(WindowManager);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  protected readonly handles = handles;
  protected readonly zones: { id: DockZone; label: string; x: number; y: number; w: number; h: number }[] = [
    { id: 'left', label: 'Acoplar à esquerda:', x: 1.75, y: 2.75, w: 4.5, h: 10.5 },
    { id: 'bottom', label: 'Acoplar embaixo:', x: 1.75, y: 9, w: 12.5, h: 4.25 },
    { id: 'right', label: 'Acoplar à direita:', x: 9.75, y: 2.75, w: 4.5, h: 10.5 },
  ];

  protected readonly title = computed(() => this.manager.title(this.state()));
  protected readonly rect = computed(() => this.manager.rects().get(this.state().id) ?? this.state().rect);
  protected readonly active = computed(() => this.manager.activeId() === this.state().id);
  protected readonly zIndex = computed(() => (this.state().zone ? 1 : 10 + this.state().z));

  private gesture: {
    pointerId: number;
    startX: number;
    startY: number;
    moved: boolean;
    grabX: number;
    grabY: number;
  } | null = null;
  private resizing: { pointerId: number; direction: ResizeDirection; startX: number; startY: number; start: Rect } | null = null;

  private origin(): { left: number; top: number } {
    const box = this.host.parentElement!.getBoundingClientRect();
    return { left: box.left, top: box.top };
  }

  protected dock(zone: DockZone): void {
    const { id, zone: current } = this.state();
    if (current === zone) this.manager.float(id);
    else this.manager.dock(id, zone);
  }

  protected toggleFloat(): void {
    const { id, zone } = this.state();
    if (zone) this.manager.float(id);
  }

  protected startDrag(event: PointerEvent): void {
    if (event.button !== 0) return;
    (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
    const rect = this.rect();
    const { left, top } = this.origin();
    this.gesture = {
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      moved: false,
      grabX: event.clientX - left - rect.x,
      grabY: event.clientY - top - rect.y,
    };
  }

  protected drag(event: PointerEvent): void {
    const g = this.gesture;
    if (!g || g.pointerId !== event.pointerId) return;
    if (!g.moved && Math.hypot(event.clientX - g.startX, event.clientY - g.startY) < DRAG_THRESHOLD) return;

    const { left, top } = this.origin();
    const pointer = { x: event.clientX - left, y: event.clientY - top };
    const state = this.state();

    if (!g.moved && state.zone) {
      // Arrastar uma janela acoplada a solta da doca, mantendo o cursor no meio da barra de título.
      g.grabX = Math.min(g.grabX, state.rect.width / 2);
      g.grabY = 16;
      this.manager.float(state.id, { x: pointer.x - g.grabX, y: pointer.y - g.grabY });
    }
    g.moved = true;

    const current = this.manager.windows().find((w) => w.id === state.id) ?? state;
    this.manager.setRect(state.id, { ...current.rect, x: pointer.x - g.grabX, y: pointer.y - g.grabY });
    this.manager.setDragZone(zoneAt(pointer, this.manager.area()), state.id);
  }

  protected endDrag(event: PointerEvent): void {
    const g = this.gesture;
    if (!g || g.pointerId !== event.pointerId) return;
    this.gesture = null;
    const preview = this.manager.dragPreview();
    this.manager.setDragZone(null);
    if (!g.moved || !preview) return;

    const { left, top } = this.origin();
    const id = this.state().id;
    const others = this.manager
      .windows()
      .filter((w) => w.zone === preview.zone && w.id !== id)
      .map((w) => this.manager.rects().get(w.id)!);
    const index = insertionIndex(preview.zone, { x: event.clientX - left, y: event.clientY - top }, others);
    this.manager.dock(id, preview.zone, index);
  }

  protected startResize(event: PointerEvent, direction: ResizeDirection): void {
    if (event.button !== 0) return;
    (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
    this.resizing = {
      pointerId: event.pointerId,
      direction,
      startX: event.clientX,
      startY: event.clientY,
      start: this.rect(),
    };
  }

  protected resize(event: PointerEvent): void {
    const r = this.resizing;
    if (!r || r.pointerId !== event.pointerId) return;
    const state = this.state();
    const next = resizeRect(
      r.start,
      r.direction,
      event.clientX - r.startX,
      event.clientY - r.startY,
      WINDOW_SPECS[state.kind].minSize,
      this.manager.area(),
    );
    this.manager.setRect(state.id, next);
  }

  protected endResize(event: PointerEvent): void {
    if (this.resizing?.pointerId === event.pointerId) this.resizing = null;
  }
}
