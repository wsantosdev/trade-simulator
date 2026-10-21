import { Injectable, signal } from '@angular/core';

const storageKey = 'trade-simulator.account-id';
const uuidV7 = /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

/** Conta carregada na Conta corrente; vale para todas as telas e sobrevive a recarregamentos da aba. */
@Injectable({ providedIn: 'root' })
export class CurrentAccount {
  private readonly current = signal<string | null>(CurrentAccount.restore());
  readonly accountId = this.current.asReadonly();

  set(accountId: string | null): void {
    const value = accountId && uuidV7.test(accountId.trim()) ? accountId.trim().toLowerCase() : null;
    this.current.set(value);
    try {
      if (value) sessionStorage.setItem(storageKey, value);
      else sessionStorage.removeItem(storageKey);
    } catch {
      // storage indisponível: a conta vale apenas enquanto a página estiver aberta
    }
  }

  private static restore(): string | null {
    try {
      const stored = sessionStorage.getItem(storageKey);
      return stored && uuidV7.test(stored) ? stored : null;
    } catch {
      return null;
    }
  }
}
