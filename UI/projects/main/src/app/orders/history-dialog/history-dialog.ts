import { DatePipe } from '@angular/common';
import { AfterViewInit, Component, ElementRef, HostListener, OnInit, inject, input, output, signal } from '@angular/core';
import { Order, OrderHistoryEntry } from '../models';
import { OrdersApi, describeError } from '../orders-api';

const hiddenKeys = new Set(['orderId', 'accountId', 'occurredAt', 'requestHash']);
const labels: Record<string, string> = {
  ticker: 'Ativo',
  side: 'Lado',
  type: 'Tipo',
  quantity: 'Quantidade',
  price: 'Preço',
  newPrice: 'Novo preço',
  filledQuantity: 'Quantidade executada',
  remainingQuantity: 'Quantidade restante',
  executedQuantity: 'Quantidade executada',
  executionPrice: 'Preço de execução',
  reason: 'Motivo',
};

@Component({
  imports: [DatePipe],
  selector: 'app-history-dialog',
  templateUrl: './history-dialog.html',
})
export class HistoryDialog implements OnInit, AfterViewInit {
  private readonly api = inject(OrdersApi);

  readonly order = input.required<Order>();
  readonly closed = output<void>();

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    this.closed.emit();
  }

  ngAfterViewInit(): void {
    this.host.nativeElement.querySelector<HTMLElement>('[role=dialog],[role=alertdialog]')?.focus();
  }

  protected readonly entries = signal<OrderHistoryEntry[]>([]);
  protected readonly loading = signal(true);
  protected readonly errorMessage = signal('');

  ngOnInit(): void {
    const order = this.order();
    this.api.history(order.accountId, order.id).subscribe({
      next: (entries) => {
        this.entries.set(entries);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.errorMessage.set(describeError(error));
        this.loading.set(false);
      },
    });
  }

  protected details(entry: OrderHistoryEntry): { label: string; value: string }[] {
    return Object.entries(entry.data)
      .filter(([key, value]) => !hiddenKeys.has(key) && value != null)
      .map(([key, value]) => ({ label: labels[key] ?? key, value: String(value) }));
  }
}
