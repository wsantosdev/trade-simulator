import { Directive, ElementRef, forwardRef, inject } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

const MAX_DIGITS = 11;
const formatter = new Intl.NumberFormat('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

// Cents-style money mask: typed digits fill from the right (1 -> 0.01, 12 -> 0.12, 123 -> 1.23).
@Directive({
  selector: 'input[appMoneyMask]',
  host: {
    inputmode: 'numeric',
    placeholder: '0.00',
    '(input)': 'onInput()',
    '(blur)': 'onTouched()',
  },
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => MoneyMaskDirective), multi: true }],
})
export class MoneyMaskDirective implements ControlValueAccessor {
  private readonly element = inject<ElementRef<HTMLInputElement>>(ElementRef).nativeElement;
  private onChange: (value: number) => void = () => {};
  protected onTouched: () => void = () => {};

  writeValue(value: number | null): void {
    this.element.value = value && value > 0 ? formatter.format(value) : '';
  }

  registerOnChange(fn: (value: number) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(disabled: boolean): void {
    this.element.disabled = disabled;
  }

  protected onInput(): void {
    const digits = this.element.value.replace(/\D/g, '').replace(/^0+/, '').slice(0, MAX_DIGITS);
    const value = digits ? Number(digits) / 100 : 0;
    this.element.value = value > 0 ? formatter.format(value) : '';
    this.onChange(value);
  }
}
