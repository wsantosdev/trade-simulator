import { CurrencyPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Observable, TimeoutError } from 'rxjs';
import { Account, AccountApi } from '../../../../main/src/app/account/account-api';
import { MoneyMaskDirective } from '../../../../main/src/app/account/money-mask.directive';
import { CurrentAccount } from '../../../../main/src/app/shared/current-account';

type Operation = 'deposits' | 'withdrawals';
const refreshIntervalMs = 2_000;

@Component({
  selector: 'app-account-window',
  imports: [CurrencyPipe, FormsModule, MoneyMaskDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex min-h-full flex-col gap-3 p-3">
      <form class="flex flex-col gap-1" (ngSubmit)="load()">
        <label for="acc-id" class="text-xs font-medium text-slate-600">Identificador da conta</label>
        <div class="flex gap-1">
          <input
            id="acc-id"
            name="accountId"
            autocomplete="off"
            placeholder="Cole o identificador"
            class="min-w-0 flex-1 rounded-md border border-slate-300 bg-white px-2 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-emerald-600"
            [ngModel]="accountIdInput()"
            (ngModelChange)="accountIdInput.set($event)"
          />
          <button type="submit" class="rounded-md bg-slate-900 px-2.5 text-sm font-semibold text-white hover:bg-slate-700 focus:outline-none focus:ring-2 focus:ring-emerald-600 disabled:opacity-40" [disabled]="busy() || !accountIdInput().trim()">Carregar</button>
        </div>
        <button type="button" class="self-start rounded-md border border-emerald-700 px-2.5 py-1 text-xs font-semibold text-emerald-800 hover:bg-emerald-50 focus:outline-none focus:ring-2 focus:ring-emerald-600 disabled:opacity-50" [disabled]="busy()" (click)="create()">
          Abrir nova conta
        </button>
      </form>

      <div role="status" aria-live="polite" class="text-xs">
        @if (errorMessage()) {
          <p class="rounded border border-red-200 bg-red-50 px-2 py-1 text-red-700" role="alert">{{ errorMessage() }}</p>
        }
        @if (successMessage()) {
          <p class="rounded border border-emerald-200 bg-emerald-50 px-2 py-1 text-emerald-800">{{ successMessage() }}</p>
        }
      </div>

      @if (account(); as current) {
        <div class="rounded-md bg-slate-950 p-3 text-white">
          <p class="text-xs text-slate-300">Saldo disponível</p>
          <p class="text-2xl font-bold tracking-tight tabular-nums" data-testid="available">{{ current.availableBalance | currency: 'USD' : 'symbol' : '1.2-2' }}</p>
          <div class="mt-2 flex h-1.5 overflow-hidden rounded-full bg-white/10" aria-hidden="true">
            <span class="h-full bg-emerald-400 transition-all" [style.width.%]="availableRatio()"></span>
            <span class="h-full bg-amber-300 transition-all" [style.width.%]="100 - availableRatio()"></span>
          </div>
          <p class="mt-2 flex justify-between text-xs text-slate-300">
            <span>Provisionado</span>
            <span class="font-semibold tabular-nums text-amber-200" data-testid="provisioned">{{ current.provisionedBalance | currency: 'USD' : 'symbol' : '1.2-2' }}</span>
          </p>
        </div>
        <button type="button" class="self-start truncate text-left text-[11px] text-slate-500 hover:text-slate-800" title="Copiar identificador" (click)="copy()">
          Conta {{ current.accountId }} (copiar)
        </button>

        <div class="grid gap-2">
          <form class="flex items-end gap-1" (ngSubmit)="submit('deposits')">
            <div class="flex min-w-0 flex-1 flex-col gap-0.5">
              <label for="acc-deposit" class="text-xs font-medium text-slate-600">Depósito (USD)</label>
              <input id="acc-deposit" name="deposit" appMoneyMask class="w-full rounded-md border border-slate-300 bg-white px-2 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-emerald-600" [ngModel]="deposit()" (ngModelChange)="deposit.set($event)" />
            </div>
            <button type="submit" class="rounded-md bg-emerald-700 px-2.5 py-1.5 text-sm font-semibold text-white hover:bg-emerald-800 focus:outline-none focus:ring-2 focus:ring-emerald-600 disabled:opacity-50" [disabled]="busy()">Depositar</button>
          </form>
          <form class="flex items-end gap-1" (ngSubmit)="submit('withdrawals')">
            <div class="flex min-w-0 flex-1 flex-col gap-0.5">
              <label for="acc-withdrawal" class="text-xs font-medium text-slate-600">Saque (USD)</label>
              <input id="acc-withdrawal" name="withdrawal" appMoneyMask class="w-full rounded-md border border-slate-300 bg-white px-2 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-emerald-600" [ngModel]="withdrawal()" (ngModelChange)="withdrawal.set($event)" />
            </div>
            <button type="submit" class="rounded-md bg-slate-900 px-2.5 py-1.5 text-sm font-semibold text-white hover:bg-slate-700 focus:outline-none focus:ring-2 focus:ring-emerald-600 disabled:opacity-50" [disabled]="busy()">Sacar</button>
          </form>
        </div>
      } @else {
        <p class="text-sm text-slate-600">Abra uma conta ou carregue uma existente para negociar.</p>
      }
    </div>
  `,
})
export class AccountWindow implements OnDestroy {
  private readonly api = inject(AccountApi);
  private readonly currentAccount = inject(CurrentAccount);
  private timer: ReturnType<typeof setInterval> | null = null;
  private refreshing = false;

  protected readonly account = signal<Account | null>(null);
  protected readonly accountIdInput = signal('');
  protected readonly busy = signal(false);
  protected readonly errorMessage = signal('');
  protected readonly successMessage = signal('');
  protected readonly deposit = signal(0);
  protected readonly withdrawal = signal(0);
  protected readonly availableRatio = computed(() => {
    const current = this.account();
    if (!current) return 0;
    const total = current.availableBalance + current.provisionedBalance;
    return total === 0 ? 0 : (current.availableBalance / total) * 100;
  });

  constructor() {
    const restored = this.currentAccount.accountId();
    if (restored) {
      this.accountIdInput.set(restored);
      this.run(this.api.get(restored), '');
    }
  }

  ngOnDestroy(): void {
    if (this.timer !== null) clearInterval(this.timer);
  }

  create(): void {
    this.run(this.api.open(), 'Conta corrente aberta com sucesso.');
  }

  load(): void {
    const id = this.accountIdInput().trim();
    if (id) this.run(this.api.get(id), 'Conta carregada com sucesso.');
  }

  submit(operation: Operation): void {
    const account = this.account();
    if (!account) return;
    const amount = operation === 'deposits' ? this.deposit() : this.withdrawal();
    if (!Number.isFinite(amount) || amount <= 0) return this.fail('Informe um valor maior que zero.');
    if (operation === 'deposits' && amount > 1_000_000) return this.fail('Cada depósito deve ser de no máximo $1.000.000.');
    if (operation === 'withdrawals' && amount > account.availableBalance) {
      return this.fail('O valor não pode exceder o saldo disponível.');
    }
    this.run(
      this.api.operate(account.accountId, operation, amount),
      operation === 'deposits' ? 'Depósito realizado com sucesso.' : 'Saque realizado com sucesso.',
      () => (operation === 'deposits' ? this.deposit.set(0) : this.withdrawal.set(0)),
    );
  }

  async copy(): Promise<void> {
    const account = this.account();
    if (!account) return;
    try {
      await navigator.clipboard.writeText(account.accountId);
      this.errorMessage.set('');
      this.successMessage.set('Identificador da conta copiado.');
    } catch {
      this.fail('Não foi possível copiar o identificador da conta.');
    }
  }

  private fail(message: string): void {
    this.errorMessage.set(message);
    this.successMessage.set('');
  }

  private startRefresh(): void {
    if (this.timer !== null) return;
    this.timer = setInterval(() => this.refresh(), refreshIntervalMs);
  }

  // Atualização silenciosa do saldo: mensagens e estado de ocupado não mudam.
  private refresh(): void {
    const current = this.account();
    if (!current || this.busy() || this.refreshing) return;
    this.refreshing = true;
    this.api.get(current.accountId).subscribe({
      next: (account) => {
        this.refreshing = false;
        if (this.account()?.accountId === account.accountId && !this.busy()) this.account.set(account);
      },
      error: () => (this.refreshing = false),
    });
  }

  private run(request: Observable<Account>, success: string, onSuccess?: () => void): void {
    this.busy.set(true);
    this.errorMessage.set('');
    this.successMessage.set('');
    request.subscribe({
      next: (account) => {
        this.account.set(account);
        this.currentAccount.set(account.accountId);
        this.accountIdInput.set(account.accountId);
        this.startRefresh();
        this.successMessage.set(success);
        onSuccess?.();
        this.busy.set(false);
      },
      error: (error: unknown) => {
        if (error instanceof TimeoutError) {
          this.errorMessage.set('A API não respondeu em até 10 segundos. Confira se o serviço está em execução e tente novamente.');
        } else if (error instanceof HttpErrorResponse) {
          this.errorMessage.set(error.error?.detail ?? error.error?.title ?? error.message ?? 'Não foi possível concluir a operação.');
        } else {
          this.errorMessage.set('Não foi possível concluir a operação.');
        }
        this.busy.set(false);
      },
    });
  }
}
