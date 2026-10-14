import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CancelDialog } from '../cancel-dialog/cancel-dialog';
import { HistoryDialog } from '../history-dialog/history-dialog';
import { Order, OrderFilters, SortField, canChange, canReplace, orderStates, stateBadgeClasses } from '../models';
import { OrdersStore } from '../orders-store';
import { ReplaceDialog } from '../replace-dialog/replace-dialog';

interface Column {
  field: SortField;
  label: string;
  align: 'left' | 'right';
}

@Component({
  imports: [FormsModule, DatePipe, DecimalPipe, HistoryDialog, CancelDialog, ReplaceDialog],
  selector: 'app-order-list',
  templateUrl: './order-list.html',
})
export class OrderList {
  protected readonly store = inject(OrdersStore);
  protected readonly states = orderStates;
  protected readonly stateBadges = stateBadgeClasses;
  protected readonly canChange = canChange;
  protected readonly canReplace = canReplace;
  protected readonly columns: Column[] = [
    { field: 'id', label: 'Id', align: 'left' },
    { field: 'ticker', label: 'Ativo', align: 'left' },
    { field: 'side', label: 'Lado', align: 'left' },
    { field: 'type', label: 'Tipo', align: 'left' },
    { field: 'quantity', label: 'Quantidade', align: 'right' },
    { field: 'price', label: 'Preço', align: 'right' },
    { field: 'averagePrice', label: 'Preço médio', align: 'right' },
    { field: 'state', label: 'Estado', align: 'left' },
    { field: 'remainingQuantity', label: 'Qtd. restante', align: 'right' },
    { field: 'lastTradeAt', label: 'Último negócio', align: 'left' },
  ];

  protected readonly historyOrder = signal<Order | null>(null);
  protected readonly cancelOrder = signal<Order | null>(null);
  protected readonly replaceOrder = signal<Order | null>(null);
  protected readonly draft = signal<OrderFilters>({ ticker: '', state: '', side: '', type: '' });
  protected readonly stateLabels = computed(
    () => new Map(orderStates.map((state) => [state.value, state.label])),
  );

  protected updateDraft<K extends keyof OrderFilters>(key: K, value: OrderFilters[K]): void {
    this.draft.update((current) => ({ ...current, [key]: value }));
  }

  protected applyFilters(): void {
    this.store.setFilters({ ...this.draft(), ticker: this.draft().ticker.trim().toUpperCase() });
  }

  protected clearFilters(): void {
    this.draft.set({ ticker: '', state: '', side: '', type: '' });
    this.applyFilters();
  }

  protected sortIndicator(field: SortField): string {
    if (this.store.sortBy() !== field) return '';
    return this.store.descending() ? '▼' : '▲';
  }

  protected ariaSort(field: SortField): 'ascending' | 'descending' | 'none' {
    if (this.store.sortBy() !== field) return 'none';
    return this.store.descending() ? 'descending' : 'ascending';
  }
}
