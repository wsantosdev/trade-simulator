import { AfterViewInit, Component, ElementRef, HostListener, OnInit, inject, input, output, signal } from '@angular/core';
import { MoneyMaskDirective } from '../money-mask.directive';
import { FormsModule } from '@angular/forms';
import { Order } from '../models';
import { OrdersApi, describeError } from '../orders-api';

@Component({
  imports: [FormsModule, MoneyMaskDirective],
  selector: 'app-replace-dialog',
  templateUrl: './replace-dialog.html',
})
export class ReplaceDialog implements OnInit, AfterViewInit {
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

  protected readonly price = signal<number | null>(null);
  protected readonly busy = signal(false);
  protected readonly errorMessage = signal('');

  ngOnInit(): void {
    const order = this.order();
    this.price.set(order.lastPrice ?? order.price);
  }

  protected confirm(): void {
    if (this.busy()) return;
    const price = this.price();
    if (price === null || !(price > 0)) {
      this.errorMessage.set('Informe um preço maior que zero.');
      return;
    }

    const order = this.order();
    this.busy.set(true);
    this.errorMessage.set('');
    this.api.replace(order.accountId, order.id, price).subscribe({
      next: () => this.closed.emit(),
      error: (error: unknown) => {
        this.busy.set(false);
        this.errorMessage.set(describeError(error));
      },
    });
  }
}
