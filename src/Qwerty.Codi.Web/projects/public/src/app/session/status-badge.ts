import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Tono di un riquadro di stato: dice a colpo d'occhio se va tutto bene, se si sta aspettando o se
 * qualcosa è fermo. Il colore **non è l'unica informazione**: ogni riquadro porta un'icona e un
 * testo, così lo stato si legge anche senza distinguere i colori (§14.4, §20.3).
 */
export type StatusTone = 'ok' | 'work' | 'wait' | 'stop' | 'idle';

/** Icona del riquadro: disegni semplici, riconoscibili da un bambino di otto anni. */
export type StatusIcon =
  | 'link'
  | 'unlink'
  | 'play'
  | 'pause'
  | 'hourglass'
  | 'lock'
  | 'star'
  | 'flag'
  | 'alert'
  | 'refresh';

/**
 * Riquadro di stato dell'app dell'alunno (§14.4): icona colorata, testo in parole, nessuna sigla.
 * È un componente di sola presentazione: decide il colore e l'icona chi lo usa, non lui.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-status-badge',
  styleUrl: './status-badge.css',
  template: `
    <li class="badge" [class]="'badge-' + tone()">
      <span class="badge-icon" aria-hidden="true">
        <svg
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
          focusable="false"
        >
          @switch (icon()) {
            @case ('link') {
              <path d="M5 12.55a11 11 0 0 1 14.08 0" />
              <path d="M8.53 16.11a6 6 0 0 1 6.95 0" />
              <path d="M12 20h.01" />
            }
            @case ('unlink') {
              <path d="M2 2l20 20" />
              <path d="M8.5 16.5a5 5 0 0 1 6.2-.4" />
              <path d="M5 12.55a11 11 0 0 1 4.3-2.6" />
              <path d="M15.6 5.3A11 11 0 0 1 19 7.4" />
              <path d="M12 20h.01" />
            }
            @case ('play') {
              <path d="M7 4l13 8-13 8z" />
            }
            @case ('pause') {
              <rect x="7" y="5" width="3.5" height="14" rx="1" />
              <rect x="13.5" y="5" width="3.5" height="14" rx="1" />
            }
            @case ('hourglass') {
              <path d="M7 3h10" />
              <path d="M7 21h10" />
              <path d="M8 3v4l4 5 4-5V3" />
              <path d="M8 21v-4l4-5 4 5v4" />
            }
            @case ('lock') {
              <rect x="5" y="11" width="14" height="9" rx="2" />
              <path d="M8 11V8a4 4 0 0 1 8 0v3" />
            }
            @case ('star') {
              <path d="M12 3l2.7 5.6 6.1.9-4.4 4.3 1 6.1L12 17l-5.4 2.9 1-6.1L3.2 9.5l6.1-.9z" />
            }
            @case ('flag') {
              <path d="M5 21V4" />
              <path d="M5 4h11l-1.5 4L16 12H5" />
            }
            @case ('alert') {
              <path d="M12 4l9 16H3z" />
              <path d="M12 10v4" />
              <path d="M12 17h.01" />
            }
            @case ('refresh') {
              <path d="M20 12a8 8 0 1 1-2.6-5.9" />
              <path d="M20 4v5h-5" />
            }
          }
        </svg>
      </span>
      <span class="badge-text">{{ label() }}</span>
    </li>
  `,
})
export class StatusBadge {
  /** Tono del riquadro: decide il colore. */
  readonly tone = input.required<StatusTone>();
  /** Icona del riquadro: dice la stessa cosa del colore, con un disegno. */
  readonly icon = input.required<StatusIcon>();
  /** Testo in parole: è l'informazione che resta quando i colori non aiutano. */
  readonly label = input.required<string>();
}
