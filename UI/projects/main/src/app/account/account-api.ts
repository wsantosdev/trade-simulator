import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, timeout } from 'rxjs';

export interface Account {
  accountId: string;
  availableBalance: number;
  provisionedBalance: number;
}

export type AccountOperation = 'deposits' | 'withdrawals';

@Injectable({ providedIn: 'root' })
export class AccountApi {
  private readonly baseUrl = '/api/accounts';
  private readonly requestTimeoutMs = 10_000;

  constructor(private readonly http: HttpClient) {}

  open(): Observable<Account> {
    return this.http
      .post<Account>(this.baseUrl, {}, { headers: this.correlationHeaders() })
      .pipe(timeout(this.requestTimeoutMs));
  }

  get(id: string): Observable<Account> {
    return this.http
      .get<Account>(`${this.baseUrl}/${encodeURIComponent(id)}`, {
        headers: this.correlationHeaders(),
      })
      .pipe(timeout(this.requestTimeoutMs));
  }

  operate(id: string, operation: AccountOperation, amount: number): Observable<Account> {
    return this.http
      .post<Account>(
        `${this.baseUrl}/${encodeURIComponent(id)}/${operation}`,
        { amount },
        { headers: this.correlationHeaders() },
      )
      .pipe(timeout(this.requestTimeoutMs));
  }

  private correlationHeaders(): HttpHeaders {
    return new HttpHeaders({ 'X-Correlation-Id': crypto.randomUUID() });
  }
}
