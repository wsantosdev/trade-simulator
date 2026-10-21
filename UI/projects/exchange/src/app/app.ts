import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ThemeToggle } from '../../../../shared/theme-toggle';

@Component({
  selector: 'app-root',
  imports: [ThemeToggle],
  templateUrl: './app.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {}
