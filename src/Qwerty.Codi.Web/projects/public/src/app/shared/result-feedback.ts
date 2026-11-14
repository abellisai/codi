import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { LearningScene } from './learning-scene';

@Component({
  selector: 'app-result-feedback',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LearningScene],
  template: `
    <section
      class="feedback"
      [class.success]="success()"
      [class.retry]="!success()"
      role="status"
      aria-atomic="true"
    >
      @if (success()) {
        <div class="star-burst" aria-hidden="true">
          @for (spark of sparks; track spark) {
            <span [style.--i]="spark">{{ spark % 2 === 0 ? '★' : '✦' }}</span>
          }
        </div>
      }
      <span class="feedback-symbol" aria-hidden="true">{{ success() ? '★' : '!' }}</span>
      <div class="feedback-copy">
        <strong>{{ success() ? 'Ce l’hai fatta!' : 'Prova ancora, puoi farcela!' }}</strong>
        <p>{{ message() }}</p>
        @if (!success()) {
          <p class="retry-hint">Puoi cambiare il tuo lavoro e riprovare.</p>
        }
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
      isolation: isolate;
      overflow: hidden;
      border-width: 3px;
      padding: 1.5rem;
    }
    .feedback.success {
      background: linear-gradient(120deg, #e6f8ed, #fff8d9);
      animation: success-arrival 650ms ease-out;
    }
    .feedback.retry {
      background: #fff0f1;
      border-color: #d54a60;
      color: #922239;
      animation: retry-arrival 450ms ease-out;
    }
    .feedback-symbol {
      position: relative;
      display: grid;
      place-items: center;
      flex: 0 0 4rem;
      height: 4rem;
      border-radius: 50%;
      background: #ffe49b;
      color: #795300;
      font-size: 2.8rem;
    }
    .retry .feedback-symbol {
      background: #b92e49;
      color: white;
    }
    .retry .feedback-symbol::after {
      content: '';
      position: absolute;
      inset: -3px;
      border: 3px solid #d54a60;
      border-radius: inherit;
      opacity: 0;
      animation: alert-ring 1.1s ease-out 3;
    }
    .feedback-copy {
      flex: 1 1 12rem;
      min-width: 0;
      overflow-wrap: anywhere;
    }
    .retry-hint {
      font-weight: 500;
    }
    .star-burst {
      position: absolute;
      inset: 0;
      pointer-events: none;
      z-index: -1;
    }
    .star-burst span {
      position: absolute;
      left: calc(4% + var(--i) * 8%);
      bottom: 0;
      color: #ae7600;
      font-size: 1.8rem;
      opacity: 0;
      animation: star-flight 1.8s ease-out 2;
      animation-delay: calc(var(--i) * 45ms);
    }
    .star-burst span:nth-child(3n) {
      color: #7252c4;
      font-size: 1.3rem;
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
    @keyframes success-arrival {
      from {
        transform: scale(0.96);
      }
      to {
        transform: scale(1);
      }
    }
    @keyframes retry-arrival {
      from {
        transform: translateY(6px);
      }
      to {
        transform: translateY(0);
      }
    }
    @keyframes star-flight {
      0% {
        opacity: 0;
        transform: translateY(0) rotate(-20deg) scale(0.4);
      }
      20% {
        opacity: 0.9;
      }
      100% {
        opacity: 0;
        transform: translateY(-180px) rotate(100deg) scale(1.2);
      }
    }
    @keyframes alert-ring {
      0% {
        opacity: 0.75;
        transform: scale(1);
      }
      100% {
        opacity: 0;
        transform: scale(1.5);
      }
    }
    @media (prefers-reduced-motion: reduce) {
      *,
      *::after {
        animation: none !important;
      }
      .star-burst span {
        opacity: 0.3;
        transform: none;
      }
    }
  `,
})
export class ResultFeedback {
  readonly success = input.required<boolean>();
  readonly message = input.required<string>();
  protected readonly sparks = Array.from({ length: 12 }, (_, index) => index);
}
