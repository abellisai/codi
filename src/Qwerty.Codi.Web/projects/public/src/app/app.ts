import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="brand-header">
      <img class="brand-logo" src="brand/codi-logo.svg" alt="Codì" width="728" height="232" />
    </header>
    <router-outlet />
  `,
  styles: `
    .brand-header {
      display: flex;
      align-items: center;
      padding: 1rem 1.5rem 0;
    }

    .brand-logo {
      display: block;
      width: clamp(8rem, 18vw, 11rem);
      max-width: 100%;
      height: auto;
    }
  `,
})
export class App {}
