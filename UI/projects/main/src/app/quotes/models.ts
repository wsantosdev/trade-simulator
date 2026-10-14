export type Variation = 'Up' | 'Down' | 'Unchanged';

export interface Quote {
  ticker: string;
  open: number;
  close: number;
  high: number;
  low: number;
  last: number;
  variation: Variation;
  timestamp: string;
}

export const isTicker = (value: string): boolean => /^[A-Z][A-Z0-9]{3}[0-9]{1,2}$/.test(value);

const currency = new Intl.NumberFormat('pt-BR', {
  style: 'currency',
  currency: 'BRL',
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});
const percent = new Intl.NumberFormat('pt-BR', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
  signDisplay: 'exceptZero',
});
const signedCurrency = new Intl.NumberFormat('pt-BR', {
  style: 'currency',
  currency: 'BRL',
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
  signDisplay: 'exceptZero',
});

export const formatPrice = (value: number): string => currency.format(value);
export const formatSignedPrice = (value: number): string => signedCurrency.format(value);
export const formatPercent = (value: number): string => `${percent.format(value)}%`;

export const formatTime = (timestamp: string): string => {
  const date = new Date(timestamp);
  return Number.isNaN(date.getTime())
    ? '-'
    : date.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
};

export const dayChange = (quote: Quote): { amount: number; percent: number } => {
  const amount = Math.round((quote.last - quote.close) * 100) / 100;
  return { amount, percent: quote.close > 0 ? (amount / quote.close) * 100 : 0 };
};
