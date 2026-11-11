import { Component, computed, inject, input, output, signal } from '@angular/core';
import { ApiService, Attempt, toApiError, type ValueProgramOutcome } from '@codi/api';
import { AttemptTransport } from '../core/attempt-transport';
import { WorkspacePreviewPublisher } from '../core/workspace-preview';
import {
  ValueProgramRunner,
  type ValueInstruction,
  type ValueRunState,
} from '@exercise-engine/value-program-runner';
import { compileValueProgram, type ProgramProblem } from '@exercise-engine/value-program-compiler';

/**
 * Pannello di un esercizio che **conserva valori** (§13.9): mostra le variabili con il loro valore,
 * l'uscita prodotta e il campo con cui l'alunno fornisce un valore quando il programma lo chiede.
 *
 * Le tre cose che il pannello deve dire sono tre cose diverse, e mostrarle insieme è il punto:
 * *dove siamo* (le variabili), *che cosa ha prodotto* (l'uscita) e *che cosa aspetta* (il campo).
 * Un pannello che mostrasse solo l'uscita nasconderebbe il funzionamento, che è quello che l'esercizio
 * insegna.
 *
 * La prova è dell'alunno e si ferma al primo valore che manca: il pannello lo chiede, l'alunno lo
 * fornisce e la corsa riprende **dalla stessa istruzione**. È il comportamento del motore, e qui non
 * viene anticipato né corretto: il pannello chiede passi al runner e mostra ciò che riceve.
 */
@Component({
  selector: 'app-value-program-panel',
  template: `
    @if (variables().length > 0) {
      <table class="variables" aria-label="Le variabili del programma">
        <thead>
          <tr>
            <th scope="col">Variabile</th>
            <th scope="col">Valore</th>
          </tr>
        </thead>
        <tbody>
          @for (riga of variables(); track riga.name) {
            <tr>
              <th scope="row">{{ riga.name }}</th>
              <!-- Un valore mai impostato si legge «—» e non zero: zero sarebbe un valore, e non lo è. -->
              <td>{{ riga.value === null ? '—' : riga.value }}</td>
            </tr>
          }
        </tbody>
      </table>
    }

    <p role="status" class="uscita">
      @if (uscita().length === 0) {
        <span class="vuota">Il programma non ha ancora mostrato nulla.</span>
      } @else {
        Mostra: <strong>{{ uscita().join(' · ') }}</strong>
      }
    </p>

    @if (attesa(); as richiesta) {
      <p role="status" class="attesa">Il programma aspetta il valore per «{{ richiesta }}».</p>
      <p class="campo">
        <label [attr.for]="'valore-' + richiesta">Il valore per «{{ richiesta }}»</label>
        <input
          [id]="'valore-' + richiesta"
          type="number"
          [value]="valore()"
          [disabled]="!enabled()"
          (input)="onValore($event)"
        />
        <button type="button" [disabled]="!enabled()" (click)="fornisci()">Dai il valore</button>
      </p>
    }

    @if (messaggio(); as testo) {
      <p role="alert">{{ testo }}</p>
    }

    @if (esito(); as risultato) {
      <p role="status" class="esito">
        {{ risultato.passed ? risultato.successMessage : risultato.wrongMessage }}
      </p>
      @if (risultato.problem.length > 0) {
        <!-- La diagnosi del server: che cosa il programma ha mostrato, o che cosa non si esegue.
             Senza, «Riprova» lascerebbe il bambino a cercare l'errore a caso. -->
        <p class="diagnosi">{{ risultato.problem }}</p>
      }
    }

    <p class="actions">
      <button type="button" [disabled]="!enabled() || inCorso()" (click)="prova()">
        Prova il programma
      </button>
      <button type="button" [disabled]="!inCorso()" (click)="azzera()">Da capo</button>
      <!-- Chiedere l'esito è una cosa diversa dal provare: la prova è dell'alunno e non viene
           verificata, l'esito lo calcola il server eseguendo il programma sui casi di prova. -->
      <button type="button" [disabled]="!enabled() || busy()" (click)="chiediEsito()">
        Fai correggere
      </button>
    </p>
    <p class="note">
      La prova è solo per te: non cambia il lavoro che vede il maestro. «Fai correggere» chiede al
      server di eseguire il programma sui casi di prova dell'esercizio.
    </p>
  `,
  styles: `
    .variables {
      border-collapse: collapse;
      margin-block: 0.5rem;
    }
    .variables th,
    .variables td {
      border: 1px solid currentColor;
      padding: 0.25rem 0.75rem;
      text-align: left;
    }
    .uscita {
      font-size: 1.2rem;
    }
    .vuota {
      opacity: 0.7;
    }
    .attesa {
      font-weight: 700;
    }
    .campo input {
      font-size: 1.2rem;
      padding: 0.4rem 0.6rem;
      width: 8rem;
    }
    .actions button {
      font-size: 1.1rem;
      padding: 0.5rem 1rem;
    }
  `,
})
export class ValueProgramPanel {
  private readonly transport = inject(AttemptTransport);
  private readonly api = inject(ApiService);
  private readonly preview = inject(WorkspacePreviewPublisher);

  readonly definitionJson = input.required<string>();
  readonly attempt = input.required<Attempt>();
  readonly connectionEpoch = input.required<string>();
  /** Vero quando il maestro lascia lavorare: fuori da lì il pannello è in sola lettura. */
  readonly enabled = input(false);
  /** Il progetto a blocchi dell'alunno, così com'è nel workspace. */
  readonly workspaceJson = input.required<string>();

  /** Il lavoro è stato salvato: la pagina rilegge ciò che è stato scritto. */
  readonly confirmed = output<void>();

  /** Istanze delle variabili dichiarate dalla definizione, con il valore corrente. */
  protected readonly variables = computed(() => {
    const dichiarate = this.dichiarate();
    const stato = this.stato();

    return dichiarate.map((name) => ({
      name,
      value: stato?.variables[name] ?? null,
    }));
  });

  /**
   * Che cosa il programma ha mostrato finora: è l'uscita visibile dell'esercizio. Il nome non è
   * `output` perché in un componente Angular `output` è la funzione che dichiara un'emissione, e
   * ombreggiarla renderebbe illeggibile la dichiarazione qui sotto.
   */
  protected readonly uscita = computed(() => this.stato()?.output ?? []);
  protected readonly attesa = computed(() => {
    const stato = this.stato();

    return stato?.status === 'WaitingForValue' ? this.variabileAttesa() : null;
  });
  protected readonly inCorso = computed(() => this.corsa() !== null);
  protected readonly messaggio = signal('');
  protected readonly valore = signal('');
  protected readonly busy = signal(false);

  /**
   * Esito della correzione: la calcola il server eseguendo il programma sui casi di prova. Il
   * pannello non la anticipa — la prova locale serve a capire, questa serve a sapere.
   */
  protected readonly esito = signal<ValueProgramOutcome | null>(null);

  private readonly corsa = signal<ValueProgramRunner | null>(null);
  private readonly stato = signal<ValueRunState | null>(null);
  private readonly variabileAttesa = signal('');

  /** Nomi delle variabili, nell'ordine in cui la definizione le dichiara. */
  private readonly dichiarate = computed(() => {
    try {
      const definizione = JSON.parse(this.definitionJson()) as {
        variables?: { name?: string }[];
      };

      return (definizione.variables ?? [])
        .map((variable) => variable.name ?? '')
        .filter((name) => name.length > 0);
    } catch {
      return [];
    }
  });

  protected onValore(event: Event): void {
    this.valore.set((event.target as HTMLInputElement).value);
  }

  /**
   * Chiede al server di correggere il programma. Il salvataggio viene prima: il server corregge ciò
   * che ha in mano, e correggere un progetto che non è ancora arrivato darebbe l'esito di un lavoro
   * più vecchio di quello che l'alunno vede.
   */
  protected async chiediEsito(): Promise<void> {
    if (!this.enabled() || this.busy()) return;

    this.busy.set(true);
    this.esito.set(null);

    try {
      await this.pubblica();
      this.esito.set(
        await this.api.executeQuery<ValueProgramOutcome>('GetValueProgramOutcome', {}),
      );
    } catch (failure) {
      this.messaggio.set(toApiError(failure).message);
    } finally {
      this.busy.set(false);
    }
  }

  /** Prova il programma: compila, avvia e va avanti finché non serve un valore o non è finito. */
  protected async prova(): Promise<void> {
    if (!this.enabled()) return;

    this.messaggio.set('');
    const compilato = this.compila();
    if ('problems' in compilato) {
      this.messaggio.set(descrivi(compilato.problems));

      return;
    }

    const runner = ValueProgramRunner.start(
      compilato.instructions,
      this.dichiarate(),
      this.valoriIniziali(),
      [],
      this.limitePassi(),
    );
    this.corsa.set(runner);
    this.avanza(runner);
    await this.pubblica();
  }

  /**
   * Fornisce il valore che il programma attende. Il valore entra nella corsa **dal di fuori**: è
   * l'unico modo in cui un valore arriva al motore, ed è ciò che rende la ripresa riproducibile.
   */
  protected async fornisci(): Promise<void> {
    const runner = this.corsa();
    if (runner === null || !this.enabled()) return;

    const stato = runner.state;
    const valore = Number.parseInt(this.valore(), 10);
    if (Number.isNaN(valore)) {
      this.messaggio.set('Scrivi un numero intero, per favore.');

      return;
    }

    // La ripresa è la stessa del motore: si riparte dallo stato salvato con il valore disponibile.
    const ripreso = ValueProgramRunner.resume(
      this.istruzioni(),
      this.dichiarate(),
      this.valoriIniziali(),
      { ...stato, pendingValues: [...stato.pendingValues, valore], status: 'Ready' },
      this.limitePassi(),
    );

    this.corsa.set(ripreso);
    this.valore.set('');
    this.avanza(ripreso);
    await this.pubblica();
  }

  protected azzera(): void {
    this.corsa.set(null);
    this.stato.set(null);
    this.variabileAttesa.set('');
    this.messaggio.set('');
    this.valore.set('');
  }

  /** Esegue finché la corsa è in cammino, poi ferma il pannello sul primo valore che manca. */
  private avanza(runner: ValueProgramRunner): void {
    let stato = runner.state;
    let passi = 0;

    while (stato.status === 'Ready' && passi < this.limitePassi()) {
      stato = runner.step().state;
      passi += 1;
    }

    this.stato.set(stato);
    this.variabileAttesa.set(this.leggiVariabileAttesa(stato.pauseReason));

    if (stato.status === 'Refused' || stato.status === 'BudgetExhausted') {
      this.messaggio.set(stato.pauseReason);
    }
  }

  /**
   * Pubblica l'uscita come anteprima del lavoro. Per questo motore il lavoro che il docente guarda è
   * **che cosa il programma mostra**, non i blocchi: senza pubblicarla, la sua card resterebbe vuota.
   */
  private async pubblica(): Promise<void> {
    const stato = this.stato();
    if (stato === null) return;

    this.preview.publishNow(
      JSON.stringify({ output: stato.output, variables: stato.variables }),
      true,
    );

    try {
      await this.transport.save(this.attempt().attemptId, 'workspace', {
        saveId: crypto.randomUUID(),
        connectionEpoch: this.connectionEpoch(),
        baseRowVersion: this.attempt().rowVersion ?? '1',
        // Il campo si chiama come il contratto del server: `clientProjectRevision`. Un nome diverso è
        // un corpo che il server non sa leggere.
        clientProjectRevision: (BigInt(this.attempt().projectRevision) + 1n).toString(),
        workspaceJson: this.workspaceJson(),
      });
      this.confirmed.emit();
    } catch (failure) {
      this.messaggio.set(toApiError(failure).message);
    }
  }

  private compila(): { instructions: ValueInstruction[] } | { problems: ProgramProblem[] } {
    const compilato = compileValueProgram(this.workspaceJson(), {
      variables: this.dichiarate(),
      defaults: this.valoriIniziali(),
    });

    return 'problems' in compilato ? compilato : { instructions: compilato.instructions };
  }

  private istruzioni(): ValueInstruction[] {
    const compilato = this.compila();

    return 'instructions' in compilato ? compilato.instructions : [];
  }

  private valoriIniziali(): Record<string, number> {
    try {
      const definizione = JSON.parse(this.definitionJson()) as {
        variables?: { name?: string; initialValue?: number }[];
      };

      const valori: Record<string, number> = {};
      for (const variable of definizione.variables ?? []) {
        if (variable.name) valori[variable.name] = variable.initialValue ?? 0;
      }

      return valori;
    } catch {
      return {};
    }
  }

  private limitePassi(): number {
    try {
      const definizione = JSON.parse(this.definitionJson()) as {
        limits?: { maxExecutionSteps?: number };
      };

      return definizione.limits?.maxExecutionSteps ?? 1000;
    } catch {
      return 1000;
    }
  }

  /** Nome della variabile attesa, letto dal motivo scritto dal motore. */
  private leggiVariabileAttesa(motivo: string): string {
    const trovato = /«([^»]+)»/.exec(motivo);

    return trovato?.[1] ?? '';
  }
}

/** Riepilogo dei problemi di compilazione per il bambino: il primo, e quanti altri. */
function descrivi(problems: readonly ProgramProblem[]): string {
  const primo = problems[0]?.message ?? 'Il programma non si può eseguire.';

  return problems.length > 1 ? `${primo} (e ${String(problems.length - 1)} altri)` : primo;
}
