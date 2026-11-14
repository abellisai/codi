import * as Blockly from 'blockly';

/**
 * Stato serializzato di un workspace Blockly, nella forma restituita da
 * `Blockly.serialization.workspaces.save`.
 */
export type WorkspaceState = Record<string, unknown>;

/** Messaggio per un testo che non è uno stato di workspace. */
export const INVALID_STATE_MESSAGE = 'Il testo non contiene uno stato di workspace Blockly valido.';

/** Messaggio per uno stato con blocchi che la piattaforma non sa ricostruire. */
export const UNKNOWN_BLOCKS_MESSAGE =
  'Il workspace contiene blocchi che questa versione non conosce: il contenuto non è stato modificato.';

/**
 * Errore sollevato quando uno stato non è utilizzabile: non è un workspace oppure contiene
 * blocchi che la piattaforma non sa ricostruire. Il chiamante lo distingue dagli errori
 * inattesi del motore e mostra il messaggio all'utente.
 */
export class InvalidWorkspaceStateError extends Error {
  constructor(message: string = INVALID_STATE_MESSAGE, cause?: unknown) {
    super(message, { cause });
    this.name = 'InvalidWorkspaceStateError';
  }
}

/**
 * Serializza il workspace con la serializzazione ufficiale di Blockly.
 * Definizione didattica, workspace e checkpoint sono formati diversi e non vanno
 * scambiati (§13): qui si gestisce solo il workspace.
 */
export function serializeWorkspace(workspace: Blockly.Workspace): string {
  return JSON.stringify(Blockly.serialization.workspaces.save(workspace));
}

/**
 * Opzioni di ricarica per {@link deserializeWorkspaceJson}.
 */
export interface DeserializeOptions {
  /**
   * Vero quando gli eventi Blockly prodotti durante il caricamento devono arrivare ai
   * listener: la ricarica è tracciata come una normale modifica del workspace. Usato dalla
   * via assistita (strumenti di collaudo e docente). Falso, comportamento di default,
   * sopprime gli eventi come la ricarica ordinaria.
   */
  readonly emitEvents?: boolean;
}

/**
 * Ricarica nel workspace lo stato prodotto da {@link serializeWorkspace}.
 * La ricarica sostituisce il contenuto, così ripeterla non duplica i blocchi.
 * La validazione (JSON, blocchi conosciuti, conteggio) avviene prima di toccare il
 * workspace: uno stato rifiutato non modifica nulla.
 * Senza `emitEvents` gli eventi sono soppressi durante il caricamento e ripristinati anche
 * in errore: `Events.enable` riporta lo stato precedente alla corrispondente `disable`,
 * quindi una soppressione già attiva nel chiamante resta attiva.
 * Con `emitEvents: true` gli eventi di creazione restano attivi: i listener vedono la
 * modifica come una normale modifica dell'utente. Il ripristino in caso di errore resta
 * comunque silenzioso, perché una modifica rifiutata non deve arrivare al salvataggio.
 * Uno stato con blocchi che Blockly non sa ricostruire viene rifiutato: il confronto fra i
 * blocchi attesi e quelli caricati lo rileva anche se Blockly si limitasse a saltarli, e in
 * entrambi i casi il contenuto precedente viene ripristinato (§13.3).
 */
export function deserializeWorkspaceJson(
  json: string,
  workspace: Blockly.Workspace,
  options: DeserializeOptions = {},
): void {
  const state = parseWorkspaceState(json);
  // Anche uno shadow di riserva deve essere conosciuto: altrimenti il problema emergerebbe
  // soltanto quando il blocco che lo sostituisce viene scollegato.
  visitBlocks(
    state,
    (type) => {
      if (!Object.hasOwn(Blockly.Blocks, type)) {
        throw new InvalidWorkspaceStateError(UNKNOWN_BLOCKS_MESSAGE);
      }
    },
    true,
  );
  const expectedBlocks = countBlocks(state);
  const previousState = Blockly.serialization.workspaces.save(workspace);

  const suppressEvents = !options.emitEvents;
  if (suppressEvents) {
    Blockly.Events.disable();
  }
  try {
    workspace.clear();
    Blockly.serialization.workspaces.load(state, workspace);

    if (workspace.getAllBlocks(false).length !== expectedBlocks) {
      throw new InvalidWorkspaceStateError(UNKNOWN_BLOCKS_MESSAGE);
    }
  } catch (error) {
    // Il ripristino è sempre soppresso: anche nella via con eventi attivi, una modifica
    // rifiutata non deve produrre eventi di annullamento partiali nel salvataggio.
    Blockly.Events.disable();
    try {
      restore(workspace, previousState);
    } finally {
      Blockly.Events.enable();
    }
    throw error instanceof InvalidWorkspaceStateError
      ? error
      : new InvalidWorkspaceStateError(INVALID_STATE_MESSAGE, error);
  } finally {
    if (suppressEvents) {
      Blockly.Events.enable();
    }
  }
}

/** Riporta il workspace allo stato precedente. Usato solo quando una ricarica viene rifiutata. */
function restore(workspace: Blockly.Workspace, previousState: WorkspaceState): void {
  workspace.clear();
  Blockly.serialization.workspaces.load(previousState, workspace);
}

/** Conta i blocchi dello stato, compresi quelli annidati in sequenze e ingressi. */
function countBlocks(state: WorkspaceState): number {
  let count = 0;
  visitBlocks(state, () => {
    count += 1;
  });

  return count;
}

/** Percorre i blocchi dello stato, compresi quelli annidati, notificandoli al chiamante. */
function visitBlocks(
  state: WorkspaceState,
  onBlock: (type: string) => void,
  includeInactiveShadows = false,
): void {
  const blocks = asRecord(state['blocks']);
  const list = blocks === null ? undefined : blocks['blocks'];
  if (!Array.isArray(list)) {
    return;
  }

  for (const block of list) {
    visitBlock(block, onBlock, includeInactiveShadows);
  }
}

function visitBlock(
  block: unknown,
  onBlock: (type: string) => void,
  includeInactiveShadows: boolean,
): void {
  const record = asRecord(block);
  if (record === null) {
    return;
  }

  const type = record['type'];
  if (typeof type === 'string') {
    onBlock(type);
  }

  const next = asRecord(record['next']);
  if (next !== null) {
    visitBlock(next['block'], onBlock, includeInactiveShadows);
  }

  const inputs = asRecord(record['inputs']);
  if (inputs === null) {
    return;
  }

  for (const value of Object.values(inputs)) {
    const input = asRecord(value);
    if (input === null) {
      continue;
    }

    // Blockly conserva lo shadow di riserva nel JSON, ma materializza solo il blocco attivo.
    if (includeInactiveShadows) {
      visitBlock(input['block'], onBlock, true);
      visitBlock(input['shadow'], onBlock, true);
    } else {
      visitBlock(input['block'] ?? input['shadow'], onBlock, false);
    }
  }
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return typeof value === 'object' && value !== null ? (value as Record<string, unknown>) : null;
}

/**
 * Converte il testo in uno stato di workspace, rifiutando ciò che non è un oggetto JSON.
 * La validazione di blocchi, campi e versioni appartiene al server (§13); qui si
 * impedisce soltanto di passare a Blockly un valore che non è uno stato.
 */
export function parseWorkspaceState(json: string): WorkspaceState {
  let parsed: unknown;
  try {
    parsed = JSON.parse(json);
  } catch (error) {
    throw new InvalidWorkspaceStateError(INVALID_STATE_MESSAGE, error);
  }

  if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
    throw new InvalidWorkspaceStateError();
  }

  return parsed as WorkspaceState;
}
