import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LearningScene } from './shared/learning-scene';

/** Ingresso autonomo dell'app alunni: benvenuto e invito a entrare nella lezione (§9.4). */
@Component({
  selector: 'app-welcome',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, LearningScene],
  template: `
    <main class="welcome">
      <section class="intro">
        <p class="eyebrow">Il tuo laboratorio di informatica</p>
        <h1>Piccole idee.<br /><span>Grandi scoperte!</span></h1>
        <p>
          Con Codì impari a usare il computer.<br />Un’attività alla volta, insieme alla tua classe.
        </p>
        <a class="button primary" routerLink="/join"
          >Entra nella lezione <span aria-hidden="true">➜</span></a
        >
        <p class="hint">Ti servono il tuo nome e il codice dell’insegnante.</p>
      </section>
      <section class="friend" aria-label="Un saluto da Codì">
        <span class="hello">Ciao! Impariamo insieme?</span>
        <app-learning-scene />
        <span class="friend-note"><span aria-hidden="true">✦</span> Prova. Scopri. Riprova!</span>
      </section>
      <ol class="welcome-steps" aria-label="Come funziona">
        <li>
          <span class="step-number">1</span>
          <div>
            <strong>Entra</strong>
            <p>Scrivi il nome e il codice.</p>
          </div>
        </li>
        <li>
          <span class="step-number">2</span>
          <div>
            <strong>Ascolta</strong>
            <p>L’insegnante ti spiega cosa fare.</p>
          </div>
        </li>
        <li>
          <span class="step-number">3</span>
          <div>
            <strong>Prova tu!</strong>
            <p>Ogni prova ti aiuta a imparare.</p>
          </div>
        </li>
      </ol>
    </main>
  `,
  styleUrl: './welcome.css',
})
export class Welcome {}
