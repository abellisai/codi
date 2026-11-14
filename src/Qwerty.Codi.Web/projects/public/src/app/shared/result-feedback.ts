import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { LearningScene } from './learning-scene';

@Component({
  selector: 'app-result-feedback',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LearningScene],
  template: `
    <section class="feedback" [class.success]="success()" role="status">
      <span class="feedback-symbol" aria-hidden="true">{{ success() ? '★' : '↻' }}</span>
      <div>
        <strong>{{ success() ? 'Ce l’hai fatta!' : 'Prova ancora, puoi farcela!' }}</strong>
        <p>{{ message() }}</p>
      </div>
      @if (success()) {
        <app-learning-scene [celebrate]="true" />
      }
    </section>
  `,
  styles: `
    .feedback {
      position: relative;
      flex-wrap: wrap;
    }
    strong {
      font-size: 1.4rem;
    }
    p {
      margin: 0.4rem 0 0;
    }
    app-learning-scene {
      flex-basis: 100%;
    }
  `,
})
export class ResultFeedback {
  readonly success = input.required<boolean>();
  readonly message = input.required<string>();
}
