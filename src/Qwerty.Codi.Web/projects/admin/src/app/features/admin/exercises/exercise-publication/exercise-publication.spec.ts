import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ApiError, ApiService, ExerciseVersion } from '@core/api';
import { toast } from '@spartan-ng/brain/sonner';
import { buildDefinition, suggestedEditorModel } from '../exercise-definition';
import { ExercisePublication } from './exercise-publication';

/**
 * Prove della pubblicazione dell'esercizio (§13.1, §13.8): verifica, versione immutabile ed elenco
 * delle versioni.
 *
 * Il caso più importante è il confine fra «a schermo» e «salvato»: la verifica guarda ciò che il
 * docente ha composto, la pubblicazione fotografa la revisione che è sul server, e la copia di una
 * versione riporta la bozza indietro.
 */

const version: ExerciseVersion = {
  id: 'version-1',
  exerciseId: 'ex1',
  versionNumber: '1',
  definitionJson: '{"schemaVersion":1}',
  workspaceJson: '{}',
  exerciseType: 'GridPath',
  engineVersion: 'gridpath-1',
  schemaVersion: '1',
  contentHash: 'c'.repeat(64),
  createdAt: '2026-10-02T11:00:00Z',
  authorNotes: null,
};

/** Definizione reale, come quella che una pubblicazione lascia nella versione. */
const definitionJson = JSON.stringify(
  buildDefinition('Raggiungi la stella', suggestedEditorModel()),
);

/** Sostituto del dispatcher: registra le richieste e simula gli esiti. */
class ApiStub {
  readonly commands: { name: string; payload: Record<string, unknown> }[] = [];
  readonly queries: { name: string; payload: Record<string, unknown> }[] = [];

  versions: ExerciseVersion[] = [];
  validationProblems: string[] = [];

  /** Rifiuto simulato dei comandi: l'elenco resta leggibile, come nel caso reale. */
  failure: ApiError | null = null;

  async executeQuery<TResponse>(
    name: string,
    payload: Record<string, unknown> = {},
  ): Promise<TResponse> {
    this.queries.push({ name, payload });

    if (name === 'ListExerciseVersions') {
      return this.versions as unknown as TResponse;
    }
    if (name === 'GetExercise')
      return { publicationStatus: this.versions.length === 0 ? 'Draft' : 'Available' } as TResponse;

    throw new Error(`Query non prevista: ${name}`);
  }

  async executeCommand<TResponse>(
    name: string,
    payload: Record<string, unknown> = {},
  ): Promise<TResponse> {
    this.commands.push({ name, payload });

    if (this.failure !== null) {
      throw this.failure;
    }

    if (name === 'ValidateExerciseDraft') {
      return {
        isValid: this.validationProblems.length === 0,
        problems: this.validationProblems,
      } as TResponse;
    }
    if (name === 'PublishExerciseVersion') {
      const published: ExerciseVersion = {
        ...version,
        id: 'version-2',
        versionNumber: String(this.versions.length + 1),
      };
      this.versions = [published, ...this.versions];

      return published as TResponse;
    }
    if (name === 'DeleteExerciseVersion') {
      // Il server elimina la versione indicata: la rilettura dell'elenco non la ritrova.
      this.versions = this.versions.filter(
        (entry) => entry.versionNumber !== payload['versionNumber'],
      );

      return {
        exerciseId: payload['exerciseId'],
        versionNumber: payload['versionNumber'],
      } as TResponse;
    }

    return {} as TResponse;
  }
}

describe('ExercisePublication', () => {
  let api: ApiStub;

  beforeEach(async () => {
    api = new ApiStub();

    await TestBed.configureTestingModule({
      imports: [ExercisePublication],
      providers: [{ provide: ApiService, useValue: api }],
    }).compileComponents();
  });

  it('mostra le versioni pubblicate lette all’avvio, dalla più recente', async () => {
    api.versions = [version];

    const fixture = await render();

    expect(api.queries[0]).toEqual({
      name: 'ListExerciseVersions',
      payload: { exerciseId: 'ex1' },
    });
    expect(text(fixture)).toContain('gridpath-1');
    expect(text(fixture)).toContain('c'.repeat(8));
    // La data della pubblicazione si mostra: un nome di campo diverso da quello del contratto la
    // lascerebbe vuota, e la riga si romperebbe senza che nulla lo segnali.
    expect(text(fixture)).toMatch(/02\/10\/26/);
    expect(text(fixture)).not.toContain('Nessuna versione pubblicata');
  });

  it('verifica la bozza composta e mostra i problemi riportati', async () => {
    const rejected = vi.spyOn(toast, 'error').mockReturnValue('errore');
    api.validationProblems = [
      'Il progetto a blocchi usa il blocco «aula_jump», che questo esercizio non mette a disposizione.',
    ];
    const fixture = await render();

    click(fixture, 'Verifica la bozza');
    await settle(fixture);

    const command = api.commands.find((entry) => entry.name === 'ValidateExerciseDraft');
    expect(command?.payload['workspaceJson']).toBe('{"blocks":{}}');
    expect(command?.payload['referenceSolutionJson']).toBe('{"solution":true}');
    // L'esito è un avviso; i problemi restano in pagina perché sono cliccabili.
    expect(rejected).toHaveBeenCalledWith('La bozza non è ancora pubblicabile.', {
      description: 'I problemi sono elencati nella pagina.',
    });
    expect(problemText(fixture)).toContain('aula_jump');
  });

  it('dice che la bozza è pubblicabile con un avviso quando la verifica non trova problemi', async () => {
    const accepted = vi.spyOn(toast, 'success').mockReturnValue('successo');
    const fixture = await render();

    click(fixture, 'Verifica la bozza');
    await settle(fixture);

    expect(accepted).toHaveBeenCalledWith('La bozza è pubblicabile.');
    // In pagina non resta una riga di conferma: l'elenco dei problemi è assente.
    expect(problemText(fixture)).toBe('');
  });

  it('pubblica la revisione salvata e aggiorna l’elenco', async () => {
    const fixture = await render();

    click(fixture, 'Rendi disponibile');
    await settle(fixture);

    expect(api.commands.find((command) => command.name === 'PublishExerciseVersion')).toEqual({
      name: 'PublishExerciseVersion',
      payload: { exerciseId: 'ex1', baseRowVersion: '3', authorNotes: null },
    });
    // La conferma è lo stato della barra: il badge passa a «Disponibile».
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('.authoring-state')?.textContent?.trim(),
    ).toBe('Disponibile');
    // L'elenco si rilegge dopo la pubblicazione: la versione appena creata compare.
    expect(text(fixture)).toContain('c'.repeat(8));
  });

  it('pubblica le note dell’autore scritte nel pannello, ripulite dagli spazi', async () => {
    const fixture = await render();
    typeNotes(fixture, '  Alzato il limite dei passi.  ');

    click(fixture, 'Rendi disponibile');
    await settle(fixture);

    expect(
      api.commands.find((command) => command.name === 'PublishExerciseVersion')?.payload[
        'authorNotes'
      ],
    ).toBe('Alzato il limite dei passi.');
  });

  it('mostra le note di una versione pubblicata e un trattino quando non ne ha', async () => {
    api.versions = [
      { ...version, versionNumber: '2', authorNotes: 'Prima prova in classe.' },
      { ...version, versionNumber: '1', authorNotes: null },
    ];

    const fixture = await render();

    // Le note vivono nella riga compagna della versione: senza leggerle, la colonna resterebbe
    // vuota e il docente non saprebbe più perché la versione esiste.
    expect(notes(fixture)).toContain('Prima prova in classe.');
    expect(notes(fixture)).toContain('—');
  });

  it('non pubblica quando la bozza non è ancora salvata', async () => {
    const fixture = await render({ draftRowVersion: null });

    expect(button(fixture, 'Rendi disponibile').disabled).toBe(true);
    expect(text(fixture)).toContain('La bozza non è ancora salvata');
  });

  it('non pubblica né copia quando ci sono modifiche non salvate', async () => {
    api.versions = [version];
    const fixture = await render({ hasUnsavedChanges: true });

    expect(button(fixture, 'Rendi disponibile').disabled).toBe(true);
    expect(button(fixture, 'Copia nell’editor').disabled).toBe(true);
    expect(text(fixture)).toContain('modifiche non salvate');
  });

  it('copia una versione nella bozza dichiarando la revisione letta', async () => {
    api.versions = [version];
    const fixture = await render();
    const replaced: number[] = [];
    fixture.componentInstance.draftReplaced.subscribe(() => replaced.push(1));

    click(fixture, 'Copia nell’editor');
    await settle(fixture);

    expect(api.commands[0]).toEqual({
      name: 'CopyExerciseVersionToDraft',
      payload: { exerciseId: 'ex1', versionNumber: '1', baseRowVersion: '3' },
    });
    expect(replaced).toHaveLength(1);
    // La conferma è l'avviso standard del dispatcher: la pagina non aggiunge un messaggio suo.
    expect(text(fixture)).not.toContain('copiata nella bozza');
  });

  it('guarda una versione pubblicata senza copiarla nella bozza', async () => {
    // Il progetto iniziale di una versione si può eseguire in sola lettura: copiarlo nella bozza
    // sostituirebbe il lavoro in corso, e guardare non deve costare il lavoro di nessuno.
    api.versions = [{ ...version, definitionJson }];
    const fixture = await render();

    click(fixture, 'Guarda la versione');
    await settle(fixture);

    expect(preview(fixture)).not.toBeNull();
    expect(text(fixture)).toContain('Raggiungi la stella');
    expect(api.commands).toHaveLength(0);

    // Lo stesso comando richiude l'anteprima: la riga resta dove era, senza una seconda schermata.
    click(fixture, 'Guarda la versione');
    await settle(fixture);

    expect(preview(fixture)).toBeNull();
  });

  it('non offre la prova di una versione che parte da un progetto vuoto', async () => {
    // Il programma iniziale lo compone l'alunno: chiedere di eseguirlo mostrerebbe un errore
    // invece di una spiegazione.
    api.versions = [{ ...version, definitionJson }];
    const fixture = await render();

    click(fixture, 'Guarda la versione');
    await settle(fixture);

    expect(preview(fixture)).not.toBeNull();
    expect(text(fixture)).toContain('progetto vuoto');
    expect(buttonLabels(fixture)).not.toContain('Avvia la prova');
  });

  it('offre la prova quando la versione porta già un programma', async () => {
    api.versions = [
      {
        ...version,
        definitionJson,
        workspaceJson: JSON.stringify({
          blocks: { languageVersion: 0, blocks: [{ type: 'aula_start', id: 'start-1' }] },
        }),
      },
    ];
    const fixture = await render();

    click(fixture, 'Guarda la versione');
    await settle(fixture);

    expect(buttonLabels(fixture)).toContain('Avvia la prova');
  });

  it('dice quando la definizione di una versione non è leggibile', async () => {
    // `version` porta una definizione ridotta: la versione resta valida e copiabile, ma qui non
    // si può eseguire, e dirlo evita che il silenzio sembri una versione senza contenuto.
    api.versions = [version];
    const fixture = await render();

    click(fixture, 'Guarda la versione');
    await settle(fixture);

    expect(preview(fixture)).toBeNull();
    expect(errorText(fixture)).toContain('non è leggibile');
  });

  it('mostra il rifiuto del server senza perdere l’elenco', async () => {
    api.versions = [version];
    const fixture = await render();
    api.failure = new ApiError('RowVersionMismatch', 412, 'La versione dichiarata è superata.');

    click(fixture, 'Rendi disponibile');
    await settle(fixture);

    expect(errorText(fixture)).toContain('superata');
    expect(text(fixture)).toContain('gridpath-1');
  });

  it('offre l’eliminazione solo delle versioni che non sono più la più recente', async () => {
    api.versions = [
      { ...version, id: 'version-2', versionNumber: '2' },
      { ...version, id: 'version-1', versionNumber: '1' },
    ];
    const fixture = await render();

    // La cronologia si ripulisce, ma l'ultima versione resta: è quella che le prossime lezioni
    // userebbero, e il server la rifiuterebbe comunque.
    expect(actionLabels(fixture)).toContain('Elimina la versione 1 dell’esercizio');
    expect(actionLabels(fixture)).not.toContain('Elimina la versione 2 dell’esercizio');
  });

  it('elimina una versione solo dopo la conferma sulla riga', async () => {
    api.versions = [
      { ...version, id: 'version-2', versionNumber: '2' },
      { ...version, id: 'version-1', versionNumber: '1' },
    ];
    const fixture = await render();

    action(fixture, 'Elimina la versione 1 dell’esercizio').click();
    await settle(fixture);

    // Il primo gesto chiede conferma e non manda nulla: eliminare è definitivo.
    expect(text(fixture)).toContain('Eliminare la versione 1?');
    expect(
      api.commands.find((command) => command.name === 'DeleteExerciseVersion'),
    ).toBeUndefined();
    // Il pulsante premuto sparisce dalla riga: il focus passa alla conferma, così il cambio di
    // stato è annunciato anche senza vedere la schermata.
    expect(document.activeElement).toBe(action(fixture, 'Conferma eliminazione della versione 1'));

    action(fixture, 'Conferma eliminazione della versione 1').click();
    await settle(fixture);

    expect(api.commands.find((command) => command.name === 'DeleteExerciseVersion')).toEqual({
      name: 'DeleteExerciseVersion',
      payload: { exerciseId: 'ex1', versionNumber: '1' },
    });
    // L'elenco è riletto: la versione eliminata non compare più e la conferma si chiude.
    expect(actionLabels(fixture)).not.toContain('Elimina la versione 1 dell’esercizio');
    expect(text(fixture)).not.toContain('Eliminare la versione 1?');
  });

  it('chiude la conferma anche quando il server rifiuta l’eliminazione', async () => {
    api.versions = [
      { ...version, id: 'version-2', versionNumber: '2' },
      { ...version, id: 'version-1', versionNumber: '1' },
    ];
    const fixture = await render();
    action(fixture, 'Elimina la versione 1 dell’esercizio').click();
    await settle(fixture);
    api.failure = new ApiError(
      'ExerciseVersionInUse',
      409,
      'La versione è già stata assegnata a una lezione.',
    );

    action(fixture, 'Conferma eliminazione della versione 1').click();
    await settle(fixture);

    // Il rifiuto si legge in pagina e la riga non resta a metà: la domanda si chiude e l'azione
    // torna disponibile, invece di lasciare una conferma per una richiesta già rifiutata.
    expect(errorText(fixture)).toContain('già stata assegnata');
    expect(text(fixture)).not.toContain('Eliminare la versione 1?');
    expect(actionLabels(fixture)).toContain('Elimina la versione 1 dell’esercizio');
    // L'elenco non è cambiato: la versione rifiutata è ancora lì, con la più recente accanto.
    expect(notes(fixture)).toHaveLength(2);
  });
});

interface RenderOverrides {
  readonly draftRowVersion?: string | null;
  readonly hasUnsavedChanges?: boolean;
}

async function render(
  overrides: RenderOverrides = {},
): Promise<ComponentFixture<ExercisePublication>> {
  const fixture = TestBed.createComponent(ExercisePublication);
  fixture.componentRef.setInput('exerciseId', 'ex1');
  fixture.componentRef.setInput('definitionJson', '{"schemaVersion":1}');
  fixture.componentRef.setInput('workspaceJson', '{"blocks":{}}');
  fixture.componentRef.setInput('solutionJson', '{"solution":true}');
  fixture.componentRef.setInput(
    'draftRowVersion',
    overrides.draftRowVersion === undefined ? '3' : overrides.draftRowVersion,
  );
  fixture.componentRef.setInput('hasUnsavedChanges', overrides.hasUnsavedChanges ?? false);
  fixture.detectChanges();
  // La lettura delle versioni è asincrona: senza attenderla i comandi risulterebbero spenti.
  await settle(fixture);

  return fixture;
}

async function settle(fixture: ComponentFixture<ExercisePublication>): Promise<void> {
  // Più giri: le promesse del sostituto si risolvono fra un giro e l'altro, e la pagina deve
  // rendere lo stato che ne segue prima che il caso lo osservi.
  for (let round = 0; round < 3; round += 1) {
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
  }
}

function click(fixture: ComponentFixture<ExercisePublication>, label: string): void {
  button(fixture, label).click();
}

function typeNotes(fixture: ComponentFixture<ExercisePublication>, value: string): void {
  const field = fixture.nativeElement.querySelector(
    '#publication-notes',
  ) as HTMLTextAreaElement | null;
  if (field === null) {
    throw new Error('Campo note non trovato');
  }

  field.value = value;
  field.dispatchEvent(new Event('input'));
  fixture.detectChanges();
}

/** Celle della colonna delle note, nell'ordine delle righe mostrate. */
function notes(fixture: ComponentFixture<ExercisePublication>): string[] {
  return [...fixture.nativeElement.querySelectorAll('td.notes')].map(
    (cell) => cell.textContent?.trim() ?? '',
  );
}

/** Anteprima aperta, se ce n'è una. */
function preview(fixture: ComponentFixture<ExercisePublication>): Element | null {
  return fixture.nativeElement.querySelector('app-exercise-preview');
}

function button(fixture: ComponentFixture<ExercisePublication>, label: string): HTMLButtonElement {
  const found = [...fixture.nativeElement.querySelectorAll('button')].find(
    (candidate) => (candidate.textContent ?? '').trim() === label,
  );
  if (found === undefined) {
    throw new Error(`Pulsante non trovato: ${label}`);
  }

  return found as HTMLButtonElement;
}

function text(fixture: ComponentFixture<ExercisePublication>): string {
  return (fixture.nativeElement as HTMLElement).textContent ?? '';
}

/** Etichette dei pulsanti presenti, per dire che un'azione non è offerta. */
function buttonLabels(fixture: ComponentFixture<ExercisePublication>): string[] {
  return [...fixture.nativeElement.querySelectorAll('button')].map((candidate) =>
    (candidate.textContent ?? '').trim(),
  );
}

/** Etichette accessibili delle azioni icona: il testo del pulsante è solo il disegno. */
function actionLabels(fixture: ComponentFixture<ExercisePublication>): string[] {
  return [...fixture.nativeElement.querySelectorAll('button')]
    .map((candidate) => candidate.getAttribute('aria-label') ?? '')
    .filter((label) => label !== '');
}

/** Pulsante icona riconosciuto dalla sua etichetta accessibile. */
function action(fixture: ComponentFixture<ExercisePublication>, label: string): HTMLButtonElement {
  const found = [...fixture.nativeElement.querySelectorAll('button')].find(
    (candidate) => candidate.getAttribute('aria-label') === label,
  );
  if (found === undefined) {
    throw new Error(`Azione non trovata: ${label}`);
  }

  return found as HTMLButtonElement;
}

function problemText(fixture: ComponentFixture<ExercisePublication>): string {
  return (
    (fixture.nativeElement.querySelector('.problems') as HTMLElement | null)?.textContent ?? ''
  );
}

function errorText(fixture: ComponentFixture<ExercisePublication>): string {
  return (fixture.nativeElement.querySelector('.error') as HTMLElement | null)?.textContent ?? '';
}
