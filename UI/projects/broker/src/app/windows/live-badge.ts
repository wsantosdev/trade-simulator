import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'app-live-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span
      class="inline-flex items-center gap-1.5 rounded-full border px-2 py-0.5 text-[11px] font-semibold"
      [class]="connected() ? 'border-emerald-200 bg-emerald-50 text-emerald-800' : 'border-amber-200 bg-amber-50 text-amber-800'"
      role="status"
      aria-live="polite"
    >
      <span class="size-1.5 rounded-full" [class]="connected() ? 'bg-emerald-500' : 'bg-amber-500'"></span>
      {{ connected() ? 'Tempo real' : 'Reconectando…' }}
    </span>
  `,
})
export class LiveBadge {
  readonly connected = input.required<boolean>();
}
