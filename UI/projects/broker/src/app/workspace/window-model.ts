export type WindowKind = 'account' | 'quote' | 'ticket' | 'orders' | 'portfolio' | 'offerbook';
export type DockZone = 'left' | 'right' | 'bottom';

export interface Rect {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface Point {
  x: number;
  y: number;
}

export interface Size {
  width: number;
  height: number;
}

export interface WindowSpec {
  label: string;
  description: string;
  /** Quantas janelas deste tipo podem ficar abertas ao mesmo tempo. */
  limit: number;
  size: Size;
  minSize: Size;
}

export const WINDOW_SPECS: Record<WindowKind, WindowSpec> = {
  account: {
    label: 'Conta corrente',
    description: 'Saldo, depósitos e saques',
    limit: 1,
    size: { width: 400, height: 460 },
    minSize: { width: 320, height: 260 },
  },
  quote: {
    label: 'Cotação',
    description: 'Preço em tempo real de um ativo',
    limit: 10,
    size: { width: 340, height: 330 },
    minSize: { width: 260, height: 220 },
  },
  ticket: {
    label: 'Boleta',
    description: 'Envio de ordens de compra e venda',
    limit: 3,
    size: { width: 400, height: 520 },
    minSize: { width: 340, height: 360 },
  },
  orders: {
    label: 'Ordens',
    description: 'Ordens enviadas, histórico, cancelamento e alteração',
    limit: 1,
    size: { width: 860, height: 380 },
    minSize: { width: 420, height: 240 },
  },
  portfolio: {
    label: 'Carteira',
    description: 'Posição disponível e bloqueada por ativo',
    limit: 1,
    size: { width: 460, height: 340 },
    minSize: { width: 320, height: 220 },
  },
  offerbook: {
    label: 'Livro de ofertas',
    description: 'Melhores ofertas de compra e venda',
    limit: 1,
    size: { width: 480, height: 460 },
    minSize: { width: 360, height: 300 },
  },
};

export const WINDOW_KINDS = Object.keys(WINDOW_SPECS) as WindowKind[];

export interface WindowState {
  id: string;
  kind: WindowKind;
  /** Numeração dentro do tipo ("Cotação 2"); o menor número livre é reaproveitado. */
  number: number;
  /** Posição e tamanho flutuantes; preservados enquanto a janela está acoplada. */
  rect: Rect;
  zone: DockZone | null;
  z: number;
  /** Ativo inicial sugerido para janelas de cotação, boleta e livro. */
  ticker?: string;
}

export interface DockSizes {
  left: number;
  right: number;
  bottom: number;
}

export const DEFAULT_DOCK_SIZES: DockSizes = { left: 360, right: 420, bottom: 320 };
export const MIN_DOCK_SIZE = 220;
export const DOCK_GAP = 4;
export const DOCK_EDGE_TRIGGER = 48;
