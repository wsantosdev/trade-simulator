import { CurrentAccount } from '../shared/current-account';
import { MainMenu } from '../shared/main-menu/main-menu';
import { CurrencyPipe } from '@angular/common';
import { MoneyMaskDirective } from './money-mask.directive';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectorRef, Component, OnDestroy, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Observable, TimeoutError } from 'rxjs';
import { Account, AccountApi } from './account-api';

type Operation = 'deposits' | 'withdrawals';

@Component({
  imports: [MainMenu, CurrencyPipe, FormsModule, MoneyMaskDirective],
  selector: 'app-account-page',
  styleUrl: './account-page.css',
  templateUrl: './account-page.html',
})
export class AccountPage implements OnDestroy {
  static readonly refreshIntervalMs = 2_000;
  private refreshTimer: ReturnType<typeof setInterval> | null = null;
  private refreshing = false;
  account: Account | null = null;
  accountIdInput = '';
  busy = false;
  errorMessage = '';
  successMessage = '';
  amounts = { deposit: 0, withdrawal: 0 };

  private readonly currentAccount = inject(CurrentAccount);

  constructor(
    private readonly api: AccountApi,
    private readonly changeDetector: ChangeDetectorRef,
  ) {
    const restored = this.currentAccount.accountId();
    if (restored) {
      this.accountIdInput = restored;
      this.run(this.api.get(restored), '');
    }
  }

  ngOnDestroy(): void {
    this.stopRefresh();
  }

  get availableBalanceRatio(): number {
    if (!this.account) return 0;
    const total = this.account.availableBalance + this.account.provisionedBalance;
    return total === 0 ? 0 : (this.account.availableBalance / total) * 100;
  }

  get provisionedBalanceRatio(): number {
    if (!this.account) return 0;
    const total = this.account.availableBalance + this.account.provisionedBalance;
    return total === 0 ? 0 : (this.account.provisionedBalance / total) * 100;
  }

  createAccount(): void {
    this.run(this.api.open(), 'Conta corrente aberta com sucesso.');
  }

  loadAccount(): void {
    const id = this.accountIdInput.trim();
    if (!id) return;
    this.run(this.api.get(id), 'Conta carregada com sucesso.');
  }

  submitOperation(operation: Operation): void {
    if (!this.account) return;
    const amount = operation === 'deposits' ? this.amounts.deposit : this.amounts.withdrawal;
    if (!Number.isFinite(amount) || amount <= 0) {
      this.errorMessage = 'Informe um valor maior que zero.';
      this.successMessage = '';
      return;
    }
    if (operation === 'deposits' && amount > 1_000_000) {
      this.errorMessage = 'Cada depósito deve ser de no máximo $1.000.000.';
      this.successMessage = '';
      return;
    }
    if (operation !== 'deposits' && amount > this.account.availableBalance) {
      this.errorMessage = 'O valor não pode exceder o saldo disponível.';
      this.successMessage = '';
      return;
    }

    this.run(
      this.api.operate(this.account.accountId, operation, amount),
      operation === 'deposits'
        ? 'Depósito realizado com sucesso.'
        : 'Saque realizado com sucesso.',
      () => {
        if (operation === 'deposits') this.amounts.deposit = 0;
        if (operation === 'withdrawals') this.amounts.withdrawal = 0;
      },
    );
  }

  async copyAccountId(): Promise<void> {
    if (!this.account) return;
    try {
      await navigator.clipboard.writeText(this.account.accountId);
      this.errorMessage = '';
      this.successMessage = 'Identificador da conta copiado.';
    } catch {
      this.errorMessage = 'Não foi possível copiar o identificador da conta.';
      this.successMessage = '';
    } finally {
      this.changeDetector.detectChanges();
    }
  }

  private startRefresh(): void {
    if (this.refreshTimer !== null) return;
    this.refreshTimer = setInterval(() => this.refreshBalance(), AccountPage.refreshIntervalMs);
  }

  dismissSuccess(): void {
    this.successMessage = '';
  }

  private stopRefresh(): void {
    if (this.refreshTimer === null) return;
    clearInterval(this.refreshTimer);
    this.refreshTimer = null;
  }

  // Silent refresh: only balances change; messages and busy state are left alone.
  private refreshBalance(): void {
    const current = this.account;
    if (!current || this.busy || this.refreshing) return;
    this.refreshing = true;
    this.api.get(current.accountId).subscribe({
      next: (account) => {
        this.refreshing = false;
        if (this.account?.accountId !== account.accountId || this.busy) return;
        this.account = account;
        this.changeDetector.detectChanges();
      },
      error: () => {
        this.refreshing = false;
      },
    });
  }

  private run(request: Observable<Account>, success: string, onSuccess?: () => void): void {
    this.busy = true;
    this.errorMessage = '';
    this.successMessage = '';
    request.subscribe({
      next: (account) => {
        this.account = account;
        this.currentAccount.set(account.accountId);
        this.accountIdInput = account.accountId;
        this.startRefresh();
        this.successMessage = success;
        onSuccess?.();
        this.busy = false;
        this.changeDetector.detectChanges();
      },
      error: (error: unknown) => {
        if (error instanceof TimeoutError) {
          this.errorMessage = 'A API não respondeu em até 10 segundos. Confira se o serviço está em execução e tente novamente.';
        } else if (error instanceof HttpErrorResponse) {
          this.errorMessage =
            error.error?.detail ??
            error.error?.title ??
            error.message ??
            'Não foi possível concluir a operação.';
        } else {
          this.errorMessage = 'Não foi possível concluir a operação.';
        }
        this.busy = false;
        this.changeDetector.detectChanges();
      },
    });
  }
}
