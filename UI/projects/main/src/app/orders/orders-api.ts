import { HttpClient, HttpErrorResponse, HttpHeaders, HttpParams, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, TimeoutError, timeout } from 'rxjs';
import {
  NewOrderRequest,
  Order,
  OrderFilters,
  OrderHistoryEntry,
  OrderPage,
  SortField,
} from './models';

export interface SearchOptions {
  accountId: string;
  filters: OrderFilters;
  sortBy: SortField;
  descending: boolean;
  page: number;
  updatedSince?: string;
}

@Injectable({ providedIn: 'root' })
export class OrdersApi {
  private readonly http = inject(HttpClient);
  private readonly requestTimeoutMs = 10_000;

  send(request: NewOrderRequest): Observable<HttpResponse<Order>> {
    return this.http
      .post<Order>('/api/orders', request, { headers: this.headers(), observe: 'response' })
      .pipe(timeout(this.requestTimeoutMs));
  }

  search(options: SearchOptions): Observable<OrderPage> {
    let params = new HttpParams()
      .set('accountId', options.accountId)
      .set('sortBy', options.sortBy)
      .set('sortDirection', options.descending ? 'desc' : 'asc')
      .set('page', options.page);
    if (options.filters.ticker) params = params.set('ticker', options.filters.ticker);
    if (options.filters.state) params = params.set('state', options.filters.state);
    if (options.filters.side) params = params.set('side', options.filters.side);
    if (options.filters.type) params = params.set('type', options.filters.type);
    if (options.updatedSince) params = params.set('updatedSince', options.updatedSince);
    return this.http
      .get<OrderPage>('/api/orders', { params, headers: this.headers() })
      .pipe(timeout(this.requestTimeoutMs));
  }

  history(accountId: string, orderId: string): Observable<OrderHistoryEntry[]> {
    return this.http
      .get<OrderHistoryEntry[]>(`/api/orders/${encodeURIComponent(orderId)}/history`, {
        params: new HttpParams().set('accountId', accountId),
        headers: this.headers(),
      })
      .pipe(timeout(this.requestTimeoutMs));
  }

  cancel(accountId: string, orderId: string): Observable<Order> {
    return this.http
      .post<Order>(`/api/orders/${encodeURIComponent(orderId)}/cancel`, { accountId }, { headers: this.headers() })
      .pipe(timeout(this.requestTimeoutMs));
  }

  replace(accountId: string, orderId: string, price: number): Observable<Order> {
    return this.http
      .post<Order>(
        `/api/orders/${encodeURIComponent(orderId)}/replace`,
        { accountId, price },
        { headers: this.headers() },
      )
      .pipe(timeout(this.requestTimeoutMs));
  }

  private headers(): HttpHeaders {
    return new HttpHeaders({ 'X-Correlation-Id': crypto.randomUUID() });
  }
}

export function describeError(error: unknown): string {
  if (error instanceof TimeoutError) return 'A API não respondeu a tempo. Tente novamente.';
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) return 'Não foi possível conectar à API de ordens.';
    return error.error?.detail ?? error.error?.title ?? 'A requisição não pôde ser processada.';
  }
  return 'Ocorreu um erro inesperado.';
}
