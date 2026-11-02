import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CurrentAccount } from '../shared/current-account';
import { MainMenu } from '../shared/main-menu/main-menu';
import { AccountApi, AccountStream } from './account-api';

@Component({
  selector: 'app-account-stream-page',
  imports: [CommonModule, FormsModule, MainMenu],
  templateUrl: './account-stream-page.html',
})
export class AccountStreamPage {
  private readonly api = inject(AccountApi);
  protected readonly current = inject(CurrentAccount);

  protected readonly stream = signal<AccountStream | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly selected = signal<number | null>(null);

  protected position = 2;
  protected type: 'deposit' | 'withdrawal' = 'deposit';
  protected amount = 100;

  protected readonly selectedEntry = computed(() => {
    const version = this.selected();
    return this.stream()?.events.find((e) => e.version === version) ?? null;
  });

  constructor() {
    effect(() => {
      const id = this.current.accountId();
      if (id) this.load(id);
    });
  }

  protected select(version: number): void {
    this.selected.set(version);
    this.position = version + 1;
  }

  protected insert(): void {
    this.run((id) => this.api.insertEvent(id, this.position, this.type, this.amount), 'Evento inserido e stream reproduzido.');
  }

  protected replay(): void {
    this.run((id) => this.api.replay(id), 'Replay concluído: saldo reconstruído a partir do stream.');
  }

  protected amountOf(payload: { amount?: number }): number | null {
    return payload.amount ?? null;
  }

  private load(id: string): void {
    this.api.stream(id).subscribe({
      next: (s) => this.stream.set(s),
      error: (e) => this.fail(e),
    });
  }

  private run(call: (id: string) => ReturnType<AccountApi['replay']>, success: string): void {
    const id = this.current.accountId();
    if (!id) return;
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    call(id).subscribe({
      next: (s) => {
        this.stream.set(s);
        this.selected.set(null);
        this.message.set(success);
        this.busy.set(false);
      },
      error: (e) => {
        this.fail(e);
        this.busy.set(false);
      },
    });
  }

  private fail(e: unknown): void {
    const detail = e instanceof HttpErrorResponse ? (e.error?.detail ?? e.message) : 'Falha ao acessar o serviço.';
    this.error.set(detail);
  }
}
