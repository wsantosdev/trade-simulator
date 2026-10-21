import { DOCK_EDGE_TRIGGER, DOCK_GAP, DockSizes, DockZone, MIN_DOCK_SIZE, Point, Rect, Size, WindowState } from './window-model';

export const TITLE_BAR_HEIGHT = 36;
const MIN_VISIBLE = 96;

const clamp = (value: number, min: number, max: number): number => Math.min(Math.max(value, min), Math.max(min, max));

/** Limita cada doca a uma fração do espaço de trabalho, deixando sempre uma área central livre. */
export function clampDockSizes(sizes: DockSizes, area: Size): DockSizes {
  const maxSide = Math.max(MIN_DOCK_SIZE, area.width * 0.45);
  const left = clamp(sizes.left, MIN_DOCK_SIZE, maxSide);
  const right = clamp(sizes.right, MIN_DOCK_SIZE, maxSide);
  const bottom = clamp(sizes.bottom, MIN_DOCK_SIZE * 0.7, Math.max(MIN_DOCK_SIZE, area.height * 0.6));
  return { left, right, bottom };
}

/** Retângulo de cada doca ocupada: laterais em altura total, inferior entre as laterais. */
export function zoneRects(occupied: ReadonlySet<DockZone>, sizes: DockSizes, area: Size): Partial<Record<DockZone, Rect>> {
  const s = clampDockSizes(sizes, area);
  const left = occupied.has('left') ? s.left : 0;
  const right = occupied.has('right') ? s.right : 0;
  const bottom = occupied.has('bottom') ? s.bottom : 0;
  const rects: Partial<Record<DockZone, Rect>> = {};
  if (left) rects.left = { x: 0, y: 0, width: left, height: area.height };
  if (right) rects.right = { x: area.width - right, y: 0, width: right, height: area.height };
  if (bottom) rects.bottom = { x: left, y: area.height - bottom, width: Math.max(0, area.width - left - right), height: bottom };
  return rects;
}

/** Divide a doca igualmente entre as janelas acopladas nela, na ordem recebida. */
export function dockedRects(windows: readonly WindowState[], sizes: DockSizes, area: Size): Map<string, Rect> {
  const result = new Map<string, Rect>();
  const occupied = new Set(windows.flatMap((w) => (w.zone ? [w.zone] : [])));
  const zones = zoneRects(occupied, sizes, area);

  for (const zone of ['left', 'right', 'bottom'] as const) {
    const bounds = zones[zone];
    if (!bounds) continue;
    const members = windows.filter((w) => w.zone === zone);
    const vertical = zone !== 'bottom';
    const total = vertical ? bounds.height : bounds.width;
    const each = (total - DOCK_GAP * (members.length - 1)) / members.length;
    members.forEach((member, index) => {
      const offset = index * (each + DOCK_GAP);
      result.set(
        member.id,
        vertical
          ? { x: bounds.x, y: bounds.y + offset, width: bounds.width, height: each }
          : { x: bounds.x + offset, y: bounds.y, width: each, height: bounds.height },
      );
    });
  }
  return result;
}

/** Doca sob o ponteiro (coordenadas do espaço de trabalho) quando ele chega perto de uma borda. */
export function zoneAt(point: Point, area: Size): DockZone | null {
  if (point.x < 0 || point.y < 0 || point.x > area.width || point.y > area.height) return null;
  const distances: [DockZone, number][] = [
    ['left', point.x],
    ['right', area.width - point.x],
    ['bottom', area.height - point.y],
  ];
  const [zone, distance] = distances.reduce((best, current) => (current[1] < best[1] ? current : best));
  return distance <= DOCK_EDGE_TRIGGER ? zone : null;
}

/** Posição de inserção ao soltar uma janela numa doca: conta quantas já acopladas ficam antes do ponteiro. */
export function insertionIndex(zone: DockZone, point: Point, others: readonly Rect[]): number {
  return others.filter((rect) =>
    zone === 'bottom' ? rect.x + rect.width / 2 < point.x : rect.y + rect.height / 2 < point.y,
  ).length;
}

/** Área que a doca ocuparia se a janela fosse solta agora, para a pré-visualização do acoplamento. */
export function dockPreview(zone: DockZone, occupied: ReadonlySet<DockZone>, sizes: DockSizes, area: Size): Rect {
  return zoneRects(new Set([...occupied, zone]), sizes, area)[zone]!;
}

/** Mantém a janela flutuante alcançável: a barra de título nunca sai do espaço de trabalho. */
export function clampFloating(rect: Rect, area: Size): Rect {
  return {
    ...rect,
    x: clamp(rect.x, MIN_VISIBLE - rect.width, area.width - MIN_VISIBLE),
    y: clamp(rect.y, 0, area.height - TITLE_BAR_HEIGHT),
  };
}

export type ResizeDirection = 'n' | 's' | 'e' | 'w' | 'ne' | 'nw' | 'se' | 'sw';

export function resizeRect(start: Rect, direction: ResizeDirection, dx: number, dy: number, min: Size, area: Size): Rect {
  let { x, y, width, height } = start;
  if (direction.includes('e')) width = Math.max(min.width, start.width + dx);
  if (direction.includes('s')) height = Math.max(min.height, start.height + dy);
  if (direction.includes('w')) {
    width = Math.max(min.width, start.width - dx);
    x = start.x + start.width - width;
  }
  if (direction.includes('n')) {
    height = Math.max(min.height, start.height - dy);
    y = start.y + start.height - height;
  }
  return clampFloating({ x, y: Math.max(0, y), width, height }, area);
}

/** Posição inicial em cascata para uma nova janela flutuante. */
export function cascadeOrigin(index: number, size: Size, area: Size): Point {
  const step = 32;
  const slots = Math.max(1, Math.floor(Math.min(area.width - size.width, area.height - size.height) / step));
  const slot = index % slots;
  return {
    x: clamp(48 + slot * step, 0, area.width - size.width),
    y: clamp(24 + slot * step, 0, area.height - size.height),
  };
}
