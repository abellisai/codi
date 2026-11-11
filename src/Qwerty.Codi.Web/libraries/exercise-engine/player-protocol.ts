import type {
  GridPathCheckpointState,
  GridPathOutcome,
} from '@exercise-engine/grid-path-checkpoint';
import type { GridCell, GridDirection } from '@exercise-engine/grid-path-scene';
import type { ValueProgramCheckpointState } from '@exercise-engine/value-program-runner';

/**
 * Protocollo fra la pagina dell'alunno e il worker che esegue il programma (§13.6). È il confine di
 * un processo: il worker non ha rete né credenziali e non conosce il trasporto, la pagina non esegue
 * il codice dei bambini. Ogni messaggio porta l'identità dell'esecuzione — tentativo, generazione,
 * corsa e istanza del worker — perché la pagina possa **ignorare** i messaggi di un worker
 * sostituito invece di applicarli alla corsa corrente.
 *
 * Le due parti condividono solo tipi e costanti: qui non c'è logica, così la stessa descrizione vale
 * per il worker, per il servizio che lo governa e per i test.
 */
export const PLAYER_PROTOCOL_VERSION = 1;

/**
 * Stato di esecuzione osservabile dal docente. I valori coincidono con quelli ammessi dalla
 * telemetria del server (`ParticipantObservationService`): un nome diverso farebbe rifiutare
 * l'osservazione. `Completed` indica una corsa conclusa, con esito positivo o negativo: l'esito fine
 * è nel campo `outcome` della scena. `Stopped` è una corsa fermata senza concludersi, `Error` un
 * player che non ha potuto eseguire.
 */
export type PlayerRuntimeStatus =
  'Ready' | 'Running' | 'Paused' | 'Completed' | 'Stopped' | 'Error';

/** Scena osservabile: posizione, orientamento, blocco attivo e contatori. */
export interface PlayerScene {
  readonly position: GridCell;
  readonly direction: GridDirection;
  readonly activeBlockId: string | null;
  readonly executions: number;
  readonly consumed: number;
  readonly outcome: GridPathOutcome;
}

/**
 * Scena osservabile di un esercizio che **conserva valori** (§13.9). Non ha posizione né direzione:
 * il suo stato è l'ambiente delle variabili con il loro valore, l'uscita prodotta e i valori ancora
 * da leggere. Sostituisce la scena del percorso nello stesso ruolo — è ciò che la pagina disegna e
 * che la telemetria osserva — senza condividerne la forma, perché i due motori non hanno lo stesso
 * stato da raccontare.
 */
export interface ValueProgramScene {
  readonly variables: Readonly<Record<string, number>>;
  readonly output: readonly string[];
  readonly pendingValues: readonly number[];
  readonly activeBlockId: string | null;
  readonly executions: number;
  readonly consumed: number;
  /**
   * Vero quando la corsa è ferma **in attesa di un valore** che l'alunno deve fornire. È lo stato
   * che il percorso non ha: una corsa sulla griglia non chiede nulla a chi la guarda.
   */
  readonly waitingForValue: boolean;
  /** Variabile che attende il valore, quando la corsa è in attesa. */
  readonly waitingVariable: string | null;
  /** Ultima diagnosi del motore: un valore mai impostato, una variabile non dichiarata, il budget. */
  readonly problem: string | null;
}

/**
 * Le due forme di scena del prodotto. Il discriminante è il campo `kind`, e sta **nella scena**
 * invece che nel messaggio perché è la scena a cambiare forma: chi legge un evento sa che cosa
 * disegnare senza chiedere al server quale motore stia eseguendo l'esercizio.
 */
export type PlayerSceneView =
  | { readonly kind: 'GridPath'; readonly grid: PlayerScene }
  | { readonly kind: 'ValueProgram'; readonly values: ValueProgramScene };

/** Campi comuni a ogni messaggio (§13.6). `runId` è assente finché non esiste una corsa. */
interface PlayerEnvelope {
  readonly protocolVersion: number;
  readonly workerInstanceId: string;
  readonly attemptId: string;
  readonly executionGeneration: string;
  readonly runId: string | null;
}

/** Carica progetto e definizione e fissa l'identità del tentativo. Non avvia nulla. */
export interface LoadProgramRequest extends PlayerEnvelope {
  readonly type: 'LoadProgram';
  readonly workspaceJson: string;
  readonly definitionJson: string;
  readonly projectRevision: string;
  readonly workspaceHash: string;
}

/** Avvia una corsa nuova dalla scena iniziale. */
export interface StartRunRequest extends PlayerEnvelope {
  readonly type: 'StartRun';
}

/** Sospende al prossimo confine di passo e restituisce un checkpoint. */
export interface PauseRunRequest extends PlayerEnvelope {
  readonly type: 'PauseRun';
  readonly reason: string;
}

/** Riprende un checkpoint valido senza ricostruire la scena da capo. */
export interface ResumeRunRequest extends PlayerEnvelope {
  readonly type: 'ResumeRun';
  readonly checkpoint: GridPathCheckpointState;
}

/** Conclude la corsa senza cancellare il progetto. */
export interface StopRunRequest extends PlayerEnvelope {
  readonly type: 'StopRun';
}

/**
 * Aggiorna il permesso operativo. `permitId` è monotono: un permesso più vecchio di uno già ricevuto
 * viene ignorato, così un messaggio in ritardo non prolunga la validità (§13.6). `validForMs` è la
 * durata residua **al momento dell'invio**: il worker la conta dal proprio istante di ricezione,
 * quindi un ritardo non allunga mai il permesso.
 */
export interface UpdatePermitRequest extends PlayerEnvelope {
  readonly type: 'UpdatePermit';
  readonly permitId: number;
  readonly permitted: boolean;
  readonly validForMs: number;
}

export type PlayerRequest =
  | LoadProgramRequest
  | StartRunRequest
  | PauseRunRequest
  | ResumeRunRequest
  | StopRunRequest
  | UpdatePermitRequest
  | ValueProgramResumeRequest
  | ProvideValueRequest;

/** Stato compatto per il rendering, la telemetria e il blocco attivo. */
export interface RuntimeStateEvent extends PlayerEnvelope {
  readonly type: 'RuntimeState';
  readonly runtimeStatus: PlayerRuntimeStatus;
  readonly scene: PlayerScene;
}

/**
 * Stato osservabile di un esercizio che conserva valori. È un evento distinto e non lo stesso con
 * un'altra scena: la forma del messaggio cambia con la forma dello stato, e un consumatore che
 * ricevesse l'evento sbagliato se ne accorgerebbe al tipo, non a schermo.
 */
export interface ValueProgramStateEvent extends PlayerEnvelope {
  readonly type: 'ValueProgramState';
  readonly runtimeStatus: PlayerRuntimeStatus;
  readonly scene: ValueProgramScene;
}

/**
 * La corsa si è fermata in attesa di un valore (§13.9). È un evento e non uno stato dentro l'altro
 * perché la pagina deve **agire**: mostrare il campo, dire al bambino che cosa aspettare e sapere
 * per quale variabile. `executionGeneration` e `runId` restano quelli dell'involucro, così un
 * messaggio di una corsa sostituita non fa comparire un campo che non serve più.
 */
export interface WaitingForValueEvent extends PlayerEnvelope {
  readonly type: 'WaitingForValue';
  readonly variable: string;
  /** Quanti valori il programma ha già letto: serve a numerare la richiesta mostrata. */
  readonly consumedValues: number;
}

/**
 * Il valore letto dall'alunno, diretto al worker. È l'unico modo in cui un valore entra nella corsa:
 * il worker non inventa valori e non li legge dal tempo, li riceve — ed è per questo che la ripresa è
 * riproducibile.
 */
export interface ProvideValueRequest extends PlayerEnvelope {
  readonly type: 'ProvideValue';
  readonly value: number;
  /** Variabile a cui il valore è destinato: un valore per la variabile sbagliata è un rifiuto. */
  readonly variable: string;
}

/**
 * Riprende una corsa da un checkpoint di un esercizio che conserva valori.
 *
 * Ha un'etichetta **propria** e non riusa `ResumeRun`: i due messaggi portano checkpoint di formati
 * diversi, e condividere l'etichetta renderebbe il campo `checkpoint` un'unione che il consumatore
 * deve distinguere a mano — cioè un errore di tipo che si scopre compilando, come è successo.
 */
export interface ValueProgramResumeRequest extends PlayerEnvelope {
  readonly type: 'ResumeValueProgram';
  readonly checkpoint: ValueProgramCheckpointState;
}

/** Stato serializzabile di un confine deterministico. */
export interface CheckpointCreatedEvent extends PlayerEnvelope {
  readonly type: 'CheckpointCreated';
  readonly checkpoint: GridPathCheckpointState;
  /**
   * Stato della corsa al momento del checkpoint. Il server accetta un checkpoint persistito solo se
   * dichiara `Running`, `Paused` o `Completed` (`AttemptCommandHandlers.ValidateCheckpoint`): il
   * valore va portato nell'involucro, non dedotto dal momento in cui il messaggio arriva.
   */
  readonly runtimeStatus: PlayerRuntimeStatus;
}

/** Esito e contatori finali della corsa. */
export interface RunFinishedEvent extends PlayerEnvelope {
  readonly type: 'RunFinished';
  readonly outcome: GridPathOutcome;
  readonly executions: number;
  readonly consumed: number;
}

/** Codice controllato, con l'eventuale blocco che ha causato l'errore. */
export interface RuntimeErrorEvent extends PlayerEnvelope {
  readonly type: 'RuntimeError';
  readonly code: string;
  readonly message: string;
  readonly sourceBlockId: string | null;
}

export type PlayerEvent =
  | RuntimeStateEvent
  | ValueProgramStateEvent
  | WaitingForValueEvent
  | CheckpointCreatedEvent
  | RunFinishedEvent
  | RuntimeErrorEvent;

/** Codici degli errori del player: un valore stabile, non il testo italiano mostrato. */
export const PlayerErrorCode = {
  /** Il programma non si compila: manca il punto di ingresso, un campo è invalido, ecc. */
  ProgramInvalid: 'ProgramInvalid',
  /** La definizione dell'esercizio non è nel formato atteso. */
  DefinitionInvalid: 'DefinitionInvalid',
  /** Un messaggio non appartiene a questa esecuzione. */
  StaleMessage: 'StaleMessage',
  /** Un comando è arrivato nell'ordine sbagliato (per esempio `ResumeRun` senza checkpoint). */
  UnexpectedCommand: 'UnexpectedCommand',
} as const;

export type PlayerErrorCodeValue = (typeof PlayerErrorCode)[keyof typeof PlayerErrorCode];
