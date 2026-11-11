import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { disabled, form, FormField, FormRoot, submit, validate } from '@angular/forms/signals';
import { NgTemplateOutlet } from '@angular/common';
import { formPublicationIssues, type PreparedExerciseDraft } from './publication-feedback';
import {
  ApiError,
  ApiService,
  ExerciseDraft,
  ExerciseReferenceSolution,
  toApiError,
} from '@core/api';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInputImports } from '@spartan-ng/helm/input';
import { HlmNativeSelectImports } from '@spartan-ng/helm/native-select';
import { HlmTextareaImports } from '@spartan-ng/helm/textarea';
import { textRules } from '@shared/forms';
import { ExercisePublication } from './exercise-publication/exercise-publication';
import {
  MAX_ANSWER_LENGTH,
  MAX_MESSAGE_LENGTH,
  MAX_PROMPT_LENGTH,
  MAX_STIMULUS_LENGTH,
  MATCH_LABELS,
  QUIZ_MATCHES,
  answerSolution,
  buildQuizDefinition,
  canonicalQuizDefinition,
  parseQuizDefinition,
  readAnswerSolution,
  suggestedQuizModel,
  toQuizModel,
  type QuizEditorModel,
} from './quiz-definition';

/**
 * Editor di un esercizio dichiarativo `Quiz` (§13.9.1). Compone la definizione — rappresentazione,
 * domanda, modo di confronto e i due messaggi — e la risposta attesa, che **non** entra nella
 * definizione: vive nella soluzione privata della versione, che il catalogo riserva al docente e che
 * non esce mai verso gli alunni.
 *
 * L'editor non prova l'esercizio: non c'è un programma da eseguire e non c'è una scena da guardare.
 * La verifica che conta è la prova generale della pubblicazione, che chiede al server di controllare
 * definizione e soluzione insieme.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    HlmButtonImports,
    HlmFieldImports,
    HlmInputImports,
    HlmNativeSelectImports,
    HlmTextareaImports,
    FormField,
    FormRoot,
    ExercisePublication,
    NgTemplateOutlet,
  ],
  selector: 'app-quiz-editor',
  styleUrl: './quiz-editor.css',
  templateUrl: './quiz-editor.html',
})
export class QuizEditor implements OnInit {
  protected readonly publication = viewChild(ExercisePublication);
  private readonly latestDraft = signal<ExerciseDraft | null>(null);
  private readonly publicationAttempted = signal(false);
  protected readonly publicationIssues = computed(() =>
    this.publicationAttempted() ? formPublicationIssues(this.editorForm) : [],
  );
  /**
   * Prepara la bozza per la verifica o la pubblicazione. Il salvataggio che serve alla pubblicazione
   * non annuncia un successo proprio: la conferma è una sola, quella della pubblicazione (§14.2).
   */
  private publishingDraft = false;
  protected readonly prepareForPublication = async (
    forPublication: boolean,
  ): Promise<PreparedExerciseDraft | null> => {
    this.publicationAttempted.set(forPublication);
    this.error.set(null);
    this.publishingDraft = forPublication;
    try {
      await submit(this.editorForm);
    } finally {
      this.publishingDraft = false;
    }
    const draft = this.latestDraft();
    return this.error() !== null || this.editorForm().invalid() || this.changed() || draft === null
      ? null
      : { ...draft, referenceSolutionJson: this.solutionJson() };
  };
  private readonly api = inject(ApiService);

  /** Esercizio in modifica: il contenitore lo legge dalla rotta e lo passa. */
  readonly exerciseId = input.required<string>();
  /** Titolo dell'esercizio: è il titolo della definizione e viene dall'anagrafica. */
  readonly title = input.required<string>();

  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<ApiError | null>(null);

  /** Vero quando il server non ha una bozza: il primo salvataggio la crea, senza versione base. */
  protected readonly isNewDraft = signal(false);

  protected readonly answer = computed(() => this.model().answer);
  private readonly savedAnswer = signal('');

  protected readonly savedRowVersion = signal<string | null>(null);

  protected readonly workspaceJson = '{"answer":""}';
  protected readonly solutionJson = computed(() => answerSolution(this.answer().trim()));

  /** Ultima definizione letta o salvata: termine di confronto di «modifiche non salvate». */
  private readonly savedDefinition = signal<string | null>(null);

  protected readonly model = signal<QuizEditorModel & { answer: string }>({
    ...suggestedQuizModel(),
    answer: '',
  });

  protected readonly matches = QUIZ_MATCHES;
  protected readonly matchLabels = MATCH_LABELS;

  protected readonly editorForm = form(
    this.model,
    (path) => {
      disabled(path, () => this.busy() || this.publication()?.locking() === true);
      textRules(path.stimulus, 'Rappresentazione', MAX_STIMULUS_LENGTH);
      textRules(path.prompt, 'Domanda', MAX_PROMPT_LENGTH);
      textRules(path.successMessage, 'Messaggio di risposta corretta', MAX_MESSAGE_LENGTH);
      textRules(path.wrongMessage, 'Messaggio di risposta sbagliata', MAX_MESSAGE_LENGTH);
      textRules(path.answer, 'Risposta corretta', MAX_ANSWER_LENGTH, false);
      validate(path.answer, ({ value }) =>
        this.publicationAttempted() && value().trim() === ''
          ? { kind: 'answerRequired', message: 'Inserisci la risposta corretta.' }
          : undefined,
      );
    },
    {
      submission: {
        action: async () => {
          await this.save();
        },
        ignoreValidators: 'none',
        onInvalid: (field) => field().errorSummary()[0]?.fieldTree().focusBoundControl(),
      },
    },
  );

  /** La definizione composta: è ciò che si confronta per sapere se ci sono modifiche da salvare. */
  protected readonly definitionJson = computed(() =>
    JSON.stringify(buildQuizDefinition(this.title(), this.model())),
  );

  /** Vero quando c'è qualcosa da salvare: definizione o risposta attesa diverse dall'ultima letta. */
  protected readonly changed = computed(
    () =>
      this.definitionJson() !== this.savedDefinition() ||
      this.answer().trim() !== this.savedAnswer(),
  );

  /**
   * Vero quando la risposta attesa è composta. Senza di essa il server non può correggere
   * l'esercizio: la bozza resta salvabile — il docente può metterla via incompleta — ma la pagina lo
   * dice, perché un esercizio pubblicato senza soluzione non dà un esito corretto.
   */
  protected readonly hasAnswer = computed(() => this.answer().trim() !== '');

  constructor() {
    // Il caricamento non parte qui: gli input non sono ancora legati quando il costruttore gira, e
    // `exerciseId` è obbligatorio. Parte da `ngOnInit`, dove la rotta ha già consegnato l'identità.
  }

  ngOnInit(): void {
    void this.load();
  }

  protected async save(): Promise<void> {
    if (!this.changed() && this.savedRowVersion() !== null) return;
    const version = this.savedRowVersion();
    const payload: Record<string, unknown> = {
      exerciseId: this.exerciseId(),
      definitionJson: this.definitionJson(),
      // Il lavoro dell'alunno su un esercizio dichiarativo è un verbale di risposta: la bozza non ha
      // un progetto, e il valore iniziale è il verbale vuoto.
      workspaceJson: this.workspaceJson,
      referenceSolutionJson: this.solutionJson(),
    };
    if (version !== null) {
      payload['baseRowVersion'] = version;
    }

    await this.run(async () => {
      const draft = await this.api.executeCommand<ExerciseDraft>('SaveExerciseDraft', payload, {
        // Dentro «Rendi disponibile» il salvataggio è un passo interno: la conferma è una sola.
        silentSuccess: this.publishingDraft,
      });
      this.latestDraft.set(draft);
      const parsed = parseQuizDefinition(draft.definitionJson);

      this.savedRowVersion.set(draft.rowVersion);
      this.savedDefinition.set(canonicalQuizDefinition(draft.definitionJson));
      this.savedAnswer.set(this.answer().trim());
      this.isNewDraft.set(false);
      if (parsed !== null) {
        this.model.set({ ...toQuizModel(parsed), answer: this.answer() });
      }
    });
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);

    try {
      await this.loadDraft();
      await this.loadAnswer();
    } catch (failure) {
      this.error.set(toApiError(failure));
    } finally {
      this.loading.set(false);
    }
  }

  protected async onDraftReplaced(): Promise<void> {
    await this.load();
  }

  private async loadDraft(): Promise<void> {
    try {
      const draft = await this.api.executeQuery<ExerciseDraft>(
        'GetExerciseDraft',
        { exerciseId: this.exerciseId() },
        // Una bozza che non esiste è la condizione di partenza dell'editor, non un guasto.
        { notFoundIsExpected: true },
      );
      this.latestDraft.set(draft);

      this.savedRowVersion.set(draft.rowVersion);
      this.savedDefinition.set(canonicalQuizDefinition(draft.definitionJson));
      this.isNewDraft.set(false);

      const parsed = parseQuizDefinition(draft.definitionJson);
      if (parsed !== null) {
        this.model.set({ ...toQuizModel(parsed), answer: this.answer() });
      } else {
        this.error.set(
          new ApiError(
            'DefinitionUnreadable',
            0,
            'La bozza salvata non è leggibile con questa versione dell’editor: i valori mostrati sono quelli iniziali.',
          ),
        );
        this.model.set({ ...suggestedQuizModel(), answer: '' });
      }
    } catch (failure) {
      if (toApiError(failure).status !== 404) {
        throw failure;
      }

      // 404: la bozza non esiste ancora. Si propone una definizione iniziale e il salvataggio sarà
      // una creazione, senza `baseRowVersion`.
      this.savedRowVersion.set(null);
      this.savedDefinition.set(null);
      this.isNewDraft.set(true);
      this.model.set({ ...suggestedQuizModel(), answer: '' });
    }

    this.editorForm().reset();
  }

  /** Legge la soluzione privata: è lì che vive la risposta attesa, non nella definizione. */
  private async loadAnswer(): Promise<void> {
    try {
      const solution = await this.api.executeQuery<ExerciseReferenceSolution>(
        'GetReferenceSolution',
        { exerciseId: this.exerciseId() },
        { notFoundIsExpected: true },
      );

      this.applyAnswer(readAnswerSolution(solution.workspaceJson));
    } catch (failure) {
      if (toApiError(failure).status !== 404) {
        throw failure;
      }

      this.applyAnswer('');
    }
  }

  private applyAnswer(answer: string): void {
    this.model.update((model) => ({ ...model, answer }));
    this.savedAnswer.set(answer);
  }

  private async run(action: () => Promise<void>): Promise<void> {
    if (this.busy()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    try {
      await action();
    } catch (failure) {
      this.error.set(toApiError(failure));
    } finally {
      this.busy.set(false);
    }
  }
}
