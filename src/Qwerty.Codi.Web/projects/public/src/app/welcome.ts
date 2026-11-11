import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

/** Ingresso autonomo dell'app alunni: benvenuto e invito a entrare nella lezione (§9.4). */
@Component({
  selector: 'app-welcome',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    <main>
      <p>Codi</p>
      <h1>Benvenuto in aula!</h1>
      <p>Qui troverai le attività preparate dal tuo insegnante.</p>
      <p><a routerLink="/join">Entra nella lezione</a></p>
    </main>
  `,
})
export class Welcome {}
