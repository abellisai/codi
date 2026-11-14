import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import type { Attempt, QuizOutcome } from '@codi/api';
import { ApiService } from '@codi/api';
import { vi } from 'vitest';
import { AttemptTransport } from '../core/attempt-transport';
import { WorkspacePreviewPublisher } from '../core/workspace-preview';
import { QuizPanel } from './quiz-panel';

/**
 * Pannello dell'esercizio dichiarativo (§13.9): legge, risponde, riceve un esito. Il caso che conta
 * è il confine: la correzione è del server, e il pannello non la anticipa.
 */
describe('Pannello della risposta', () => {
  const definition = JSON.stringify({
    schemaVersion: 1,
    exerciseType: 'Quiz',
    engineVersion: 'quiz-1',
    quiz: { stimulus: '0110', prompt: 'Quanto vale?', match: 'Integer' },
    feedback: { success: 'Giusto!', collision: 'Riprova.' },
  });

  const attempt: Attempt = {
    attemptId: 'a1',
    assignmentId: 'as1',
    exerciseVersionId: 'v1',
    attemptNumber: '1',
    status: 'Ready',
    workspaceJson: '{"answer":""}',
    workspaceHash: 'impronta',
    projectRevision: '1',
    checkpointJson: null,
    checkpointEngineVersion: null,
    lastSavedAtUtc: null,
    updatedAtUtc: '2026-10-09T10:00:00Z',
    rowVersion: '1',
  };

  let saved: { attemptId: string; kind: string; body: Record<string, unknown> }[] = [];
  let queries: string[] = [];
  let anteprime: string[] = [];
  let outcome: QuizOutcome = { correct: true, successMessage: 'Giusto!', wrongMessage: 'Riprova.' };
  let fixture: ComponentFixture<QuizPanel>;

  beforeEach(() => {
    saved = [];
    queries = [];
    anteprime = [];
    outcome = { correct: true, successMessage: 'Giusto!', wrongMessage: 'Riprova.' };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: AttemptTransport,
          useValue: {
            save: (attemptId: string, kind: string, body: Record<string, unknown>) => {
              saved.push({ attemptId, kind, body });
              return Promise.resolve({
                attemptId,
                saveId: 's',
                rowVersion: '2',
                projectRevision: '2',
              });
            },
          },
        },
        {
          // L'anteprima verso il docente ha i propri test: qui interessa che il pannello la
          // pubblichi, non come.
          provide: WorkspacePreviewPublisher,
          useValue: {
            publishNow: (workspace: string) => {
              anteprime.push(workspace);
            },
          },
        },
        {
          provide: ApiService,
          useValue: {
            executeQuery: (name: string) => {
              queries.push(name);
              return Promise.resolve(outcome);
            },
          },
        },
      ],
    });
  });

  it('mostra la rappresentazione e la domanda della definizione', async () => {
    const page = await mount(true);

    expect(page.textContent).toContain('0110');
    expect(page.textContent).toContain('Quanto vale?');
  });

  it("conferma la risposta: la salva nella forma del verbale e chiede l'esito al server", async () => {
    const page = await mount(true);
    const input = page.querySelector('input')!;
    input.value = '6';
    input.dispatchEvent(new Event('input'));
    confirmButton(page).click();
    await waitForOutcome(page);

    expect(saved).toHaveLength(1);
    expect(saved[0]!.kind).toBe('workspace');
    expect(saved[0]!.body['workspaceJson']).toBe('{"answer":"6"}');
    // L'impronta è quella calcolata sul verbale: una promessa non attesa partirebbe come oggetto
    // vuoto, e il server rifiuterebbe il corpo.
    expect(typeof saved[0]!.body['workspaceHash']).toBe('string');
    expect(String(saved[0]!.body['workspaceHash'])).toHaveLength(64);
    // Il nome del campo è quello del contratto del server: un invio con il nome del DTO viene
    // rifiutato come corpo non corrispondente al formato atteso.
    expect(saved[0]!.body['clientProjectRevision']).toBe('2');
    expect(saved[0]!.body['projectRevision']).toBeUndefined();
    // La correzione non è del browser: il pannello chiede l'esito al server.
    expect(queries).toEqual(['GetQuizOutcome']);
    // Il verbale è anche l'anteprima del docente: senza pubblicarla, la sua card resterebbe vuota.
    expect(anteprime).toEqual(['{"answer":"6"}']);
    expect(page.textContent).toContain('Giusto!');
  });

  it('con una risposta sbagliata mostra il messaggio del docente', async () => {
    outcome = { correct: false, successMessage: 'Giusto!', wrongMessage: 'Riprova.' };
    const page = await mount(true);
    confirmButton(page).click();
    await waitForOutcome(page);

    expect(page.textContent).toContain('Riprova.');
    expect(page.textContent).not.toContain('Giusto!');
  });

  it('non salva nulla quando l’insegnante non lascia lavorare', async () => {
    const page = await mount(false);
    confirmButton(page).click();
    await flush();

    expect(saved).toEqual([]);
    expect(queries).toEqual([]);
    expect(confirmButton(page).disabled).toBe(true);
  });

  async function mount(enabled: boolean): Promise<HTMLElement> {
    fixture = TestBed.createComponent(QuizPanel);
    fixture.componentRef.setInput('definitionJson', definition);
    fixture.componentRef.setInput('attempt', attempt);
    fixture.componentRef.setInput('connectionEpoch', 'epoch-1');
    fixture.componentRef.setInput('enabled', enabled);
    fixture.detectChanges();

    return fixture.nativeElement as HTMLElement;
  }

  function confirmButton(page: HTMLElement): HTMLButtonElement {
    return [...page.querySelectorAll('button')].find(
      (candidate) => candidate.textContent?.trim() === 'Conferma',
    )!;
  }

  /** Attende l'esito visibile, dopo il calcolo asincrono dell'impronta e il salvataggio. */
  async function waitForOutcome(page: HTMLElement): Promise<void> {
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(page.querySelector('[role="status"]')).not.toBeNull();
    });
  }

  /**
   * Attende le promesse del salvataggio e **ridisegna**: la pagina è `OnPush`, quindi lo stato
   * cambiato da un gestore asincrono non arriva al DOM finché non lo si chiede.
   */
  async function flush(): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
  }
});
