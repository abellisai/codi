import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { ApiService, Attempt, QuizOutcome, toApiError } from '@codi/api';
import { AttemptTransport } from '../core/attempt-transport';
import { workspaceDigest } from '../core/attempt-autosave';
import { WorkspacePreviewPublisher } from '../core/workspace-preview';

/** Il verbale di risposta dell'alunno, nella forma che il server si aspetta (§13.9). */
function answerRecord(answer: string): string {
  return JSON.stringify({ answer });
}

/**
 * Pannello di un esercizio dichiarativo (§13.9): l'alunno legge la rappresentazione, scrive la
 * risposta e riceve un esito.
 *
 * Non monta Blockly e non parla con il worker del player: non c'è un progetto da comporre né
 * un'esecuzione da governare. Il lavoro del tentativo è il verbale di risposta, e la correzione la
 * fa il server — l'unico che conosce la risposta attesa, perché vive nella soluzione privata della
 * versione pubblicata e non esce mai verso gli alunni.
 *
 * Il pannello si salva da sé invece di passare dal salvataggio automatico: la risposta si salva
 * quando il bambino la conferma, non mentre la digita, e non ha senso salvare ogni lettera di una
 * risposta che verrà comunque corretta alla conferma.
 */
@Component({
  selector: 'app-quiz-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (stimulus(); as text) {
      <p class="stimulus" aria-label="Che cosa devi leggere">{{ text }}</p>
    }
    @if (prompt(); as question) {
      <p class="prompt">{{ question }}</p>
    }

    <label for="quiz-answer">La tua risposta</label>
    <input
      id="quiz-answer"
      type="text"
      inputmode="numeric"
      autocomplete="off"
      [value]="answer()"
      [disabled]="!enabled() || busy()"
      (input)="onInput($event)"
      (keydown.enter)="confirm()"
    />

    <p class="actions">
      <button type="button" [disabled]="!enabled() || busy()" (click)="confirm()">Conferma</button>
    </p>

    @if (problem(); as message) {
      <p role="alert">{{ message }}</p>
    }
    @if (outcome(); as result) {
      <p role="status" class="outcome">
        {{ result.correct ? result.successMessage : result.wrongMessage }}
      </p>
    }
  `,
  styles: `
    .stimulus {
      font-size: 2rem;
      letter-spacing: 0.3rem;
      font-weight: 700;
    }
    .prompt {
      font-size: 1.2rem;
    }
    input {
      font-size: 1.2rem;
      padding: 0.4rem 0.6rem;
    }
    .actions button {
      font-size: 1.1rem;
      padding: 0.5rem 1rem;
    }
    .outcome {
      font-size: 1.2rem;
      font-weight: 700;
    }
  `,
})
export class QuizPanel {
  private readonly transport = inject(AttemptTransport);
  private readonly api = inject(ApiService);
  private readonly preview = inject(WorkspacePreviewPublisher);

  readonly definitionJson = input.required<string>();
  readonly attempt = input.required<Attempt>();
  readonly connectionEpoch = input.required<string>();
  /** Vero quando il maestro lascia lavorare: fuori da lì il pannello è in sola lettura. */
  readonly enabled = input(false);

  /** Risposta salvata e confermata: la pagina rilegge il lavoro appena scritto. */
  readonly confirmed = output<void>();

  protected readonly answer = signal('');
  protected readonly busy = signal(false);
  protected readonly problem = signal('');
  protected readonly outcome = signal<QuizOutcome | null>(null);

  /** Rappresentazione da leggere, dalla definizione pubblicata. */
  protected readonly stimulus = computed(() => this.read('stimulus'));
  /** Che cosa deve produrre l'alunno. */
  protected readonly prompt = computed(() => this.read('prompt'));

  /** Conferma la risposta: la salva e chiede l'esito al server. */
  protected async confirm(): Promise<void> {
    if (!this.enabled() || this.busy()) {
      return;
    }

    const attempt = this.attempt();
    const workspace = answerRecord(this.answer());
    // L'impronta è asincrona: senza attenderla partirebbe una promessa, che il server leggerebbe
    // come un oggetto vuoto e rifiuterebbe come corpo non corrispondente.
    const hash = await workspaceDigest(workspace);
    this.busy.set(true);
    this.problem.set('');

    try {
      await this.transport.save(attempt.attemptId, 'workspace', {
        saveId: crypto.randomUUID(),
        connectionEpoch: this.connectionEpoch(),
        baseRowVersion: attempt.rowVersion ?? '1',
        // La revisione avanza di uno: una risposta confermata è un lavoro nuovo, anche se il testo
        // è lo stesso di prima. Il nome del campo è quello del contratto
        // (`clientProjectRevision`), non quello del DTO: un nome diverso è un corpo che il server
        // non sa leggere, e il salvataggio viene rifiutato con «formato non corrispondente».
        clientProjectRevision: (BigInt(attempt.projectRevision) + 1n).toString(),
        workspaceJson: workspace,
        workspaceHash: hash,
      });
      this.confirmed.emit();
      // Anche una risposta è lavoro dell'alunno, e il docente la vede dall'anteprima: per un
      // esercizio dichiarativo il «progetto» è il verbale, e senza pubblicarlo la card del docente
      // resterebbe vuota. L'anteprima è immediata e non è un salvataggio (§15.5).
      this.preview.publishNow(workspace, true);
      this.outcome.set(await this.api.executeQuery<QuizOutcome>('GetQuizOutcome', {}));
    } catch (failure) {
      this.problem.set(toApiError(failure).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected onInput(event: Event): void {
    this.answer.set((event.target as HTMLInputElement).value);
  }

  /** Legge una proprietà di testo dalla definizione; una definizione illeggibile non la inventa. */
  private read(property: string): string {
    try {
      const value = JSON.parse(this.definitionJson())?.quiz?.[property];

      return typeof value === 'string' ? value : '';
    } catch {
      return '';
    }
  }
}
