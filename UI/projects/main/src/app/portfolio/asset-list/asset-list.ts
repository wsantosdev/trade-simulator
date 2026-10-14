import { DecimalPipe } from '@angular/common';
import { Component, inject } from '@angular/core';
import { SortField } from '../models';
import { PortfolioStore } from '../portfolio-store';

interface Column {
  field: SortField;
  label: string;
  align: 'left' | 'right';
}

@Component({
  imports: [DecimalPipe],
  selector: 'app-asset-list',
  templateUrl: './asset-list.html',
})
export class AssetList {
  protected readonly store = inject(PortfolioStore);
  protected readonly columns: Column[] = [
    { field: 'ticker', label: 'Ativo', align: 'left' },
    { field: 'total', label: 'Quantidade total', align: 'right' },
    { field: 'available', label: 'Disponível', align: 'right' },
    { field: 'blocked', label: 'Bloqueada', align: 'right' },
  ];

  protected sortIndicator(field: SortField): string {
    if (this.store.sortBy() !== field) return '';
    return this.store.descending() ? '▼' : '▲';
  }

  protected ariaSort(field: SortField): 'ascending' | 'descending' | 'none' {
    if (this.store.sortBy() !== field) return 'none';
    return this.store.descending() ? 'descending' : 'ascending';
  }
}
