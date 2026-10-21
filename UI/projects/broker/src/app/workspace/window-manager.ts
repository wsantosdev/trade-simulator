import { Injectable, computed, signal } from '@angular/core';
import {
  cascadeOrigin,
  clampDockSizes,
  clampFloating,
  dockPreview,
  dockedRects,
} from './layout';
import {
  DEFAULT_DOCK_SIZES,
  DockSizes,
  DockZone,
  MIN_DOCK_SIZE,
  Rect,
  Size,
  WINDOW_KINDS,
  WINDOW_SPECS,
  WindowKind,
  WindowState,
} from './window-model';

export interface OpenOptions {
  zone?: DockZone | null;
  ticker?: string;
}

export interface DragPreview {
  zone: DockZone;
  rect: Rect;
}

/** Estado do espaço de trabalho: quais janelas estão abertas, onde estão e como as docas se dividem. */
@Injectable({ providedIn: 'root' })
export class WindowManager {
  private nextZ = 1;

  readonly windows = signal<WindowState[]>([]);
  readonly area = signal<Size>({ width: 1280, height: 720 });
  readonly dockSizes = signal<DockSizes>({ ...DEFAULT_DOCK_SIZES });
  readonly dragPreview = signal<DragPreview | null>(null);

  readonly counts = computed(() => {
    const counts = Object.fromEntries(WINDOW_KINDS.map((kind) => [kind, 0])) as Record<WindowKind, number>;
    for (const window of this.windows()) counts[window.kind]++;
    return counts;
  });

  readonly activeId = computed(() => {
    const windows = this.windows();
    return windows.length ? windows.reduce((top, w) => (w.z > top.z ? w : top)).id : null;
  });

  readonly effectiveDockSizes = computed(() => clampDockSizes(this.dockSizes(), this.area()));

  /** Retângulo final de cada janela, já considerando a doca em que ela está. */
  readonly rects = computed(() => {
    const area = this.area();
    const windows = this.windows();
    const docked = dockedRects(windows, this.dockSizes(), area);
    return new Map(windows.map((w) => [w.id, docked.get(w.id) ?? clampFloating(w.rect, area)]));
  });

  readonly occupiedZones = computed(() => new Set(this.windows().flatMap((w) => (w.zone ? [w.zone] : []))));

  title(window: WindowState): string {
    const spec = WINDOW_SPECS[window.kind];
    return spec.limit > 1 ? `${spec.label} ${window.number}` : spec.label;
  }

  titleOf(id: string): string {
    const window = this.windows().find((w) => w.id === id);
    return window ? this.title(window) : '';
  }

  canOpen(kind: WindowKind): boolean {
    return this.counts()[kind] < WINDOW_SPECS[kind].limit;
  }

  /** Abre uma janela do tipo, ou apenas traz à frente a existente quando o tipo é único. Devolve null no limite. */
  open(kind: WindowKind, options: OpenOptions = {}): string | null {
    const spec = WINDOW_SPECS[kind];
    const existing = this.windows().filter((w) => w.kind === kind);

    if (existing.length >= spec.limit) {
      if (spec.limit === 1) {
        this.focus(existing[0].id);
        return existing[0].id;
      }
      return null;
    }

    const taken = new Set(existing.map((w) => w.number));
    let number = 1;
    while (taken.has(number)) number++;

    const area = this.area();
    const size = {
      width: Math.min(spec.size.width, Math.max(spec.minSize.width, area.width - 16)),
      height: Math.min(spec.size.height, Math.max(spec.minSize.height, area.height - 16)),
    };
    const origin = cascadeOrigin(this.windows().filter((w) => !w.zone).length, size, area);
    const window: WindowState = {
      id: `${kind}-${number}`,
      kind,
      number,
      rect: { ...origin, ...size },
      zone: null,
      z: this.nextZ++,
      ticker: options.ticker,
    };
    this.windows.update((windows) => [...windows, window]);
    if (options.zone) this.dock(window.id, options.zone);
    return window.id;
  }

  close(id: string): void {
    this.windows.update((windows) => windows.filter((w) => w.id !== id));
  }

  closeAll(): void {
    this.windows.set([]);
  }

  focus(id: string): void {
    const current = this.windows().find((w) => w.id === id);
    if (!current || current.z === this.nextZ - 1) return;
    const z = this.nextZ++;
    this.windows.update((windows) => windows.map((w) => (w.id === id ? { ...w, z } : w)));
  }

  setRect(id: string, rect: Rect): void {
    this.windows.update((windows) => windows.map((w) => (w.id === id ? { ...w, rect } : w)));
  }

  /** Acopla a janela; `index` define a ordem dentro da doca (padrão: no fim). */
  dock(id: string, zone: DockZone, index?: number): void {
    this.windows.update((windows) => {
      const target = windows.find((w) => w.id === id);
      if (!target) return windows;
      const rest = windows.filter((w) => w.id !== id);
      const inZone = rest.filter((w) => w.zone === zone);
      const at = index === undefined ? inZone.length : Math.min(Math.max(index, 0), inZone.length);
      const docked = { ...target, zone };
      if (at >= inZone.length) {
        const last = inZone.length ? rest.indexOf(inZone[inZone.length - 1]) + 1 : rest.length;
        return [...rest.slice(0, last), docked, ...rest.slice(last)];
      }
      const before = rest.indexOf(inZone[at]);
      return [...rest.slice(0, before), docked, ...rest.slice(before)];
    });
    this.focus(id);
  }

  /** Solta a janela da doca, devolvendo-a ao último tamanho flutuante (opcionalmente numa nova posição). */
  float(id: string, at?: { x: number; y: number }): void {
    const area = this.area();
    this.windows.update((windows) =>
      windows.map((w) =>
        w.id === id
          ? { ...w, zone: null, rect: clampFloating({ ...w.rect, ...(at ?? {}) }, area) }
          : w,
      ),
    );
    this.focus(id);
  }

  resizeDock(zone: DockZone, size: number): void {
    const area = this.area();
    const max = zone === 'bottom' ? area.height * 0.6 : area.width * 0.45;
    const min = zone === 'bottom' ? MIN_DOCK_SIZE * 0.7 : MIN_DOCK_SIZE;
    this.dockSizes.update((sizes) => ({ ...sizes, [zone]: Math.min(Math.max(size, min), Math.max(min, max)) }));
  }

  setDragZone(zone: DockZone | null, ignoring?: string): void {
    if (!zone) {
      if (this.dragPreview()) this.dragPreview.set(null);
      return;
    }
    const occupied = new Set(
      this.windows().flatMap((w) => (w.zone && w.id !== ignoring ? [w.zone] : [])),
    );
    this.dragPreview.set({ zone, rect: dockPreview(zone, occupied, this.dockSizes(), this.area()) });
  }

  setArea(area: Size): void {
    const current = this.area();
    if (current.width === area.width && current.height === area.height) return;
    this.area.set(area);
  }

  /** Arranjo inicial: cotações à esquerda, conta e boleta à direita, ordens e carteira embaixo e o livro flutuando. */
  resetLayout(): void {
    this.nextZ = 1;
    this.dockSizes.set({ ...DEFAULT_DOCK_SIZES });
    this.windows.set([]);
    this.open('quote', { zone: 'left', ticker: 'PETR4' });
    this.open('quote', { zone: 'left', ticker: 'VALE3' });
    this.open('account', { zone: 'right' });
    this.open('ticket', { zone: 'right' });
    this.open('orders', { zone: 'bottom' });
    this.open('portfolio', { zone: 'bottom' });
    const book = this.open('offerbook', { ticker: 'PETR4' });
    if (book) {
      const area = this.area();
      const left = this.dockSizes().left;
      const right = this.dockSizes().right;
      const spec = WINDOW_SPECS.offerbook;
      const width = Math.min(spec.size.width, Math.max(spec.minSize.width, area.width - left - right - 32));
      this.setRect(book, { x: left + 16, y: 16, width, height: Math.min(spec.size.height, Math.max(spec.minSize.height, area.height - DEFAULT_DOCK_SIZES.bottom - 32)) });
    }
  }
}
