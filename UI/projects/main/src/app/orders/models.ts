export type OrderSide = 'Buy' | 'Sell';
export type OrderType = 'Market' | 'Limit';
export type OrderState =
  | 'PendingNew'
  | 'New'
  | 'PartiallyFilled'
  | 'Filled'
  | 'Cancelled'
  | 'Rejected'
  | 'PendingCancel'
  | 'PendingReplace'
  | 'PendingDispatching'
  | 'ScheduledDispatching';

export const orderStates: { value: OrderState; label: string }[] = [
  { value: 'PendingNew', label: 'Pendente' },
  { value: 'New', label: 'Nova' },
  { value: 'PartiallyFilled', label: 'Parcialmente executada' },
  { value: 'Filled', label: 'Executada' },
  { value: 'Cancelled', label: 'Cancelada' },
  { value: 'Rejected', label: 'Rejeitada' },
  { value: 'PendingCancel', label: 'Cancelamento pendente' },
  { value: 'PendingReplace', label: 'Alteração pendente' },
  { value: 'PendingDispatching', label: 'Despacho pendente' },
  { value: 'ScheduledDispatching', label: 'Agendada' },
];

export const stateBadgeClasses: Record<OrderState, string> = {
  PendingDispatching: 'bg-slate-100 text-slate-700',
  PendingNew: 'bg-amber-100 text-amber-800',
  ScheduledDispatching: 'bg-violet-800 text-violet-100',
  New: 'bg-sky-100 text-sky-800',
  PartiallyFilled: 'bg-indigo-100 text-indigo-800',
  Filled: 'bg-emerald-100 text-emerald-800',
  Cancelled: 'bg-slate-200 text-slate-700',
  Rejected: 'bg-rose-100 text-rose-800',
  PendingCancel: 'bg-amber-100 text-amber-800',
  PendingReplace: 'bg-amber-100 text-amber-800',
};

export interface Order {
  id: string;
  accountId: string;
  ticker: string;
  side: OrderSide;
  type: OrderType;
  quantity: number;
  price: number | null;
  state: OrderState;
  executedQuantity: number;
  remainingQuantity: number;
  averagePrice: number | null;
  lastPrice: number | null;
  lastTradeAt: string | null;
  sentAt: string;
  lastActionAt: string;
}

export interface OrderChanges {
  id: string;
  lastActionAt: string;
  state?: OrderState;
  price?: number;
  executedQuantity?: number;
  remainingQuantity?: number;
  averagePrice?: number;
  lastPrice?: number;
  lastTradeAt?: string;
}

export interface OrderPage {
  items: Order[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface OrderHistoryEntry {
  sequence: number;
  eventType: string;
  occurredAt: string;
  data: Record<string, unknown>;
}

export type SortField =
  | 'id'
  | 'ticker'
  | 'side'
  | 'type'
  | 'quantity'
  | 'price'
  | 'averagePrice'
  | 'state'
  | 'executedQuantity'
  | 'remainingQuantity'
  | 'lastPrice'
  | 'lastTradeAt'
  | 'sentAt'
  | 'lastActionAt';

export interface OrderFilters {
  ticker: string;
  state: OrderState | '';
  side: OrderSide | '';
  type: OrderType | '';
}

export interface NewOrderRequest {
  accountId: string;
  ticker: string;
  side: OrderSide;
  type: OrderType;
  quantity: number;
  price: number | null;
  requestHash: string;
}

export const pageSize = 10;

const uuidV7 = /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
export const isAccountNumber = (value: string): boolean => uuidV7.test(value.trim());
export const isTicker = (value: string): boolean => /^[A-Z]{4}[345]$/.test(value);

export const canChange = (order: Order): boolean =>
  order.state === 'New' || order.state === 'PartiallyFilled' || order.state === 'ScheduledDispatching';
export const canReplace = (order: Order): boolean => canChange(order) && order.state !== 'ScheduledDispatching' && order.type === 'Limit';

export type QuoteVariation = 'Up' | 'Down' | 'Unchanged';

export interface Quote {
  ticker: string;
  open: number;
  close: number;
  high: number;
  low: number;
  last: number;
  variation: QuoteVariation;
  timestamp: string;
}

export type BookPhase = 'PreOpen' | 'Open' | 'Closed';
export type BookSide = 'Bid' | 'Ask';

export interface BookLevel {
  price: number;
  quantity: number;
  orders: number;
}

export interface BookChange extends BookLevel {
  side: BookSide;
}

export interface OfferBookSnapshot {
  ticker: string;
  sequence: number;
  phase: BookPhase;
  bids: BookLevel[];
  asks: BookLevel[];
  timestamp: string;
}

export interface OfferBookDelta {
  ticker: string;
  sequence: number;
  previousSequence: number;
  phase: BookPhase;
  changes: BookChange[];
  timestamp: string;
}
