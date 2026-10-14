export interface Asset {
  ticker: string;
  available: number;
  blocked: number;
  total: number;
}

export interface AssetPage {
  items: Asset[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export type SortField = 'ticker' | 'total' | 'available' | 'blocked';

export const pageSize = 50;

const uuidV7 = /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
export const isAccountNumber = (value: string): boolean => uuidV7.test(value.trim());
