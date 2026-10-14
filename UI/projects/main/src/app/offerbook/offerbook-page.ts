import { MainMenu } from '../shared/main-menu/main-menu';
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { BookLevel, Side, formatPercent, formatPrice, formatQuantity, formatTime, phaseLabel } from './models';
import { OfferBookStore } from './offerbook-store';

@Component({
  imports: [MainMenu, FormsModule],
  selector: 'app-offerbook-page',
  templateUrl: './offerbook-page.html',
})
export class OfferBookPage implements OnInit, OnDestroy {
  protected readonly store = inject(OfferBookStore);
  protected readonly formatPrice = formatPrice;
  protected readonly formatQuantity = formatQuantity;
  protected readonly formatPercent = formatPercent;
  protected readonly formatTime = formatTime;
  protected readonly phaseLabel = phaseLabel;

  protected barStyle(side: Side, level: BookLevel): string {
    const percent = this.store.barPercent(level);
    const color = side === 'Bid' ? '#d1fae5' : '#fee2e2';
    const direction = side === 'Bid' ? 'to left' : 'to right';
    return `linear-gradient(${direction}, ${color} ${percent}%, transparent ${percent}%)`;
  }

  ngOnInit(): void {
    this.store.start();
  }

  ngOnDestroy(): void {
    this.store.stop();
  }
}
