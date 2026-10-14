import { HttpClient, HttpErrorResponse, HttpHeaders, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, TimeoutError, timeout } from 'rxjs';
import { AssetPage, SortField } from './models';

export interface SearchOptions {
  accountId: string;
  sortBy: SortField;
  descending: boolean;
  page: number;
}

@Injectable({ providedIn: 'root' })
export class PortfolioApi {
  private readonly http = inject(HttpClient);
  private readonly requestTimeoutMs = 10_000;

  search(options: SearchOptions): Observable<AssetPage> {
    const params = new HttpParams()
      .set('accountId', options.accountId)
      .set('sortBy', options.sortBy)
      .set('sortDirection', options.descending ? 'desc' : 'asc')
      .set('page', options.page);
    return this.http
      .get<AssetPage>('/api/assets', { params, headers: this.headers() })
      .pipe(timeout(this.requestTimeoutMs));
  }

  private headers(): HttpHeaders {
    return new HttpHeaders({ 'X-Correlation-Id': crypto.randomUUID() });
  }
}

export function describeError(error: unknown): string {
  if (error instanceof TimeoutError) return 'A API não respondeu a tempo. Tente novamente.';
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) return 'Não foi possível conectar à API de portfólio.';
    return error.error?.detail ?? error.error?.title ?? 'A requisição não pôde ser processada.';
  }
  return 'Ocorreu um erro inesperado.';
}
