import { findEngine } from '@exercise-engine/engine-registry';
import {
  DEFAULT_LIMITS,
  EXERCISE_BLOCKS,
  GRID_PATH_ENGINE_VERSION,
  GRID_PATH_EXERCISE_TYPE,
  GRID_PATH_SCHEMA_VERSION,
  buildDefinition,
  normalizeScene,
  parseDefinition,
  suggestedEditorModel,
  toEditorModel,
  type ExerciseEditorModel,
  type GridPathDefinition,
} from './exercise-definition';

/** Definizione valida, modellata sull'esempio di §13.2, con il traguardo in (3, 1). */
function sampleDefinition(): GridPathDefinition {
  return {
    schemaVersion: GRID_PATH_SCHEMA_VERSION,
    exerciseType: GRID_PATH_EXERCISE_TYPE,
    engineVersion: GRID_PATH_ENGINE_VERSION,
    title: 'Raggiungi la stella',
    instructions: 'Porta il personaggio sulla stella usando i blocchi.',
    scene: {
      width: 5,
      height: 4,
      start: { x: 0, y: 1, direction: 'East' },
      obstacles: [{ x: 2, y: 2 }],
      goal: { x: 3, y: 1 },
    },
    toolbox: { allowedBlocks: ['aula_start', 'aula_move', 'aula_turn', 'aula_repeat'] },
    limits: {
      maxBlocks: 30,
      maxNestingDepth: 8,
      maxExecutionSteps: 1000,
      maxActiveDurationMs: 120000,
    },
    completion: { type: 'ReachCell', x: 3, y: 1 },
    feedback: {
      success: 'Hai raggiunto la stella!',
      collision: "C'è un ostacolo: prova a cambiare percorso.",
    },
  };
}

function model(overrides: Partial<ExerciseEditorModel> = {}): ExerciseEditorModel {
  return { ...suggestedEditorModel(), ...overrides };
}

describe('buildDefinition', () => {
  it('compone la definizione dai campi con il titolo dell’esercizio', () => {
    const definition = buildDefinition(
      'Raggiungi la stella',
      model({
        instructions: 'Consegna nuova',
        width: 6,
        height: 7,
        startX: 1,
        startY: 2,
        direction: 'South',
        goalX: 4,
        goalY: 5,
        limits: {
          maxBlocks: 40,
          maxNestingDepth: 4,
          maxExecutionSteps: 200,
        },
        maxActiveDurationMinutes: 7,
        successMessage: 'Bravissimo!',
        collisionMessage: 'Riprova.',
      }),
    );

    expect(definition.title).toBe('Raggiungi la stella');
    expect(definition.instructions).toBe('Consegna nuova');
    expect(definition.scene).toEqual({
      width: 6,
      height: 7,
      start: { x: 1, y: 2, direction: 'South' },
      obstacles: [],
      goal: { x: 4, y: 5 },
    });
    expect(definition.limits).toEqual({
      maxBlocks: 40,
      maxNestingDepth: 4,
      maxExecutionSteps: 200,
      maxActiveDurationMs: 420_000,
    });
    expect(definition.feedback).toEqual({ success: 'Bravissimo!', collision: 'Riprova.' });
  });

  it('fa coincidere sempre il completamento con il traguardo', () => {
    const definition = buildDefinition('Esercizio', model({ goalX: 2, goalY: 3 }));

    expect(definition.completion).toEqual({ type: 'ReachCell', x: 2, y: 3 });
    expect(definition.completion.x).toBe(definition.scene.goal.x);
    expect(definition.completion.y).toBe(definition.scene.goal.y);
  });

  it('elenca i blocchi scelti nell’ordine del catalogo e tiene sempre `aula_start`', () => {
    const definition = buildDefinition(
      'Esercizio',
      model({
        blocks: { aula_start: false, aula_move: true, aula_turn: false, aula_repeat: true },
      }),
    );

    expect(definition.toolbox.allowedBlocks).toEqual(['aula_start', 'aula_move', 'aula_repeat']);
    expect(
      definition.toolbox.allowedBlocks.every((block) => EXERCISE_BLOCKS.includes(block as never)),
    ).toBe(true);
  });

  it('usa la durata predefinita al posto di un valore mancante', () => {
    const definition = buildDefinition('Esercizio', model({ maxActiveDurationMinutes: null }));

    expect(definition.limits.maxActiveDurationMs).toBe(DEFAULT_LIMITS.maxActiveDurationMs);
  });
});

describe('normalizeScene', () => {
  it('riporta le dimensioni nell’intervallo ammesso', () => {
    expect(normalizeScene(model({ width: 1, height: 99 }))).toMatchObject({ width: 2, height: 20 });
  });

  it('tiene partenza e traguardo dentro la griglia e scarta gli ostacoli fuori', () => {
    const scene = normalizeScene(
      model({
        width: 3,
        height: 3,
        startX: 9,
        startY: 9,
        direction: 'West',
        goalX: -4,
        goalY: -4,
        obstacles: [
          { x: 5, y: 5 },
          { x: 1, y: 0 },
          { x: 1, y: 0 },
        ],
      }),
    );

    expect(scene.start).toEqual({ x: 2, y: 2, direction: 'West' });
    expect(scene.goal).toEqual({ x: 0, y: 0 });
    expect(scene.obstacles).toEqual([{ x: 1, y: 0 }]);
  });

  it('tiene partenza e traguardo distinti spostando il traguardo sulla prima cella libera', () => {
    const scene = normalizeScene(
      model({ width: 2, height: 2, startX: 0, startY: 0, goalX: 0, goalY: 0 }),
    );

    expect(scene.goal).not.toEqual(scene.start);
    expect(scene.goal).toEqual({ x: 1, y: 0 });
  });

  it('non lascia ostacoli sopra partenza o traguardo', () => {
    const scene = normalizeScene(
      model({
        width: 3,
        height: 3,
        startX: 0,
        startY: 0,
        goalX: 2,
        goalY: 2,
        obstacles: [
          { x: 0, y: 0 },
          { x: 2, y: 2 },
          { x: 1, y: 1 },
        ],
      }),
    );

    expect(scene.obstacles).toEqual([{ x: 1, y: 1 }]);
  });
});

describe('toEditorModel', () => {
  it('porta la definizione nei campi e mantiene sempre `aula_start` selezionato', () => {
    const definition = sampleDefinition();

    const result = toEditorModel({ ...definition, toolbox: { allowedBlocks: ['aula_move'] } });

    expect(result.width).toBe(5);
    expect(result.startX).toBe(0);
    expect(result.direction).toBe('East');
    expect(result.obstacles).toEqual([{ x: 2, y: 2 }]);
    expect(result.blocks).toEqual({
      aula_start: true,
      aula_move: true,
      aula_turn: false,
      aula_repeat: false,
    });
    expect(result.limits).toEqual({
      maxBlocks: definition.limits.maxBlocks,
      maxNestingDepth: definition.limits.maxNestingDepth,
      maxExecutionSteps: definition.limits.maxExecutionSteps,
    });
    // La durata si porta nei minuti del campo: 120000 millisecondi sono 2 minuti.
    expect(result.maxActiveDurationMinutes).toBe(2);
  });
});

describe('parseDefinition', () => {
  it('legge una definizione valida e la restituisce invariata', () => {
    const definition = sampleDefinition();

    expect(parseDefinition(JSON.stringify(definition))).toEqual(definition);
  });

  it('rifiuta un testo che non è una definizione `GridPath`', () => {
    expect(parseDefinition('non è json')).toBeNull();
    expect(parseDefinition('42')).toBeNull();
    expect(parseDefinition('{}')).toBeNull();
    expect(
      parseDefinition(
        JSON.stringify({ schemaVersion: 1, scene: {}, toolbox: {}, limits: {}, feedback: {} }),
      ),
    ).toBeNull();
  });

  it('rifiuta una definizione di una versione del motore che questa build non conosce', () => {
    // La forma può sembrare quella giusta — scena, cassetto, limiti, messaggi — ma le regole sono
    // di un altro motore: leggerla come una griglia mostrerebbe campi che non le appartengono.
    const aliena = { ...sampleDefinition(), engineVersion: 'gridpath-9' };

    expect(parseDefinition(JSON.stringify(aliena))).toBeNull();
  });

  it('rifiuta una definizione il cui tipo non è quello della sua versione', () => {
    const incoerente = { ...sampleDefinition(), exerciseType: 'AltroTipo' };

    expect(parseDefinition(JSON.stringify(incoerente))).toBeNull();
  });

  it('conosce esattamente le versioni registrate anche lato server', () => {
    // Il registro del browser e quello del server devono elencare le stesse coppie tipo/versione:
    // il server distribuisce la versione fissata dall'assegnazione, e una versione che il client
    // non conosce diventa una pagina che spiega l'errore invece dell'esercizio.
    //
    // La prova non può leggere il registro .NET — è un'altra build, in un altro linguaggio — quindi
    // lo dichiara: aggiungere una versione qui **e** in
    // `src/Qwerty.Codi.Domain/Exercises/ExerciseEngineRegistry.cs` è l'unico modo per farla
    // passare.
    const registroServer = [{ exerciseType: 'GridPath', engineVersion: 'gridpath-1' }];

    expect(findEngine('gridpath-1')).toMatchObject(registroServer[0]);
    expect(findEngine('gridpath-2')).toBeNull();
    expect(registroServer).toHaveLength(1);
  });
});
