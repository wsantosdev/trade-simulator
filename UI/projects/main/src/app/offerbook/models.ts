export type Phase = 'PreOpen' | 'Open' | 'Closed';
export type Side = 'Bid' | 'Ask';

export interface BookLevel {
  price: number;
  quantity: number;
  orders: number;
}

export interface BookChange extends BookLevel {
  side: Side;
}

export interface OfferBookSnapshot {
  ticker: string;
  sequence: number;
  phase: Phase;
  bids: BookLevel[];
  asks: BookLevel[];
  timestamp: string;
}

export interface OfferBookDelta {
  ticker: string;
  sequence: number;
  previousSequence: number;
  phase: Phase;
  changes: BookChange[];
  timestamp: string;
}

export const isTicker = (value: string): boolean => /^[A-Z][A-Z0-9]{3}[0-9]{1,2}$/.test(value);

const currency = new Intl.NumberFormat('pt-BR', {
  style: 'currency',
  currency: 'BRL',
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});
const integer = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 0 });
const percent = new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

export const formatPrice = (value: number): string => currency.format(value);
export const formatQuantity = (value: number): string => integer.format(value);
export const formatPercent = (value: number): string => `${percent.format(value)}%`;

export const formatTime = (timestamp: string): string => {
  const date = new Date(timestamp);
  return Number.isNaN(date.getTime())
    ? '-'
    : date.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
};

export const phaseLabel = (phase: Phase): string =>
  phase === 'Open' ? 'Aberto' : phase === 'PreOpen' ? 'Pré-abertura' : 'Fechado';
