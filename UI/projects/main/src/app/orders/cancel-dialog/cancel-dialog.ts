import { AfterViewInit, Component, ElementRef, HostListener, inject, input, output, signal } from '@angular/core';
import { Order } from '../models';
import { OrdersApi, describeError } from '../orders-api';

@Component({
  selector: 'app-cancel-dialog',
  templateUrl: './cancel-dialog.html',
})
export class CancelDialog implements AfterViewInit {
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

  protected readonly busy = signal(false);
  protected readonly errorMessage = signal('');

  protected confirm(): void {
    if (this.busy()) return;
    const order = this.order();
    this.busy.set(true);
    this.errorMessage.set('');
    this.api.cancel(order.accountId, order.id).subscribe({
      next: () => this.closed.emit(),
      error: (error: unknown) => {
        this.busy.set(false);
        this.errorMessage.set(describeError(error));
      },
    });
  }
}
