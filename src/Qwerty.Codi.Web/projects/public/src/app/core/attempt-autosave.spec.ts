import { TestBed } from '@angular/core/testing';
import {
  ApiError,
  Attempt,
  AttemptCheckpoint,
  AttemptFinalizationGrant,
  AttemptSave,
  ParticipantSnapshot,
} from '@codi/api';
import { vi } from 'vitest';
import { AttemptAutosave, workspaceDigest } from './attempt-autosave';
import { AttemptTransport } from './attempt-transport';
import { AttemptRecoveryStore, RecoveryCopy } from './attempt-recovery-store';
const attempt: Attempt = {
  attemptId: 't',
  assignmentId: 'a',
  exerciseVersionId: 'v',
  attemptNumber: '1',
  status: 'Ready',
  workspaceJson: '{}',
  workspaceHash: '',
  projectRevision: '0',
  rowVersion: '1',
  checkpointJson: null,
  checkpointEngineVersion: null,
  lastSavedAtUtc: null,
  updatedAtUtc: '',
};
const snapshot = {
  sessionId: 's',
  participantId: 'p',
  connectionEpoch: '1',
} as ParticipantSnapshot;
const grant: AttemptFinalizationGrant = {
  sessionId: 's',
  participantId: 'p',
  grantId: 'g',
  attemptId: 't',
  connectionEpoch: '1',
  executionGeneration: '0',
  expiresAtUtc: '',
};
class Store {
  rows = new Map<string, RecoveryCopy>();
  async prune(): Promise<void> {
    /* Archivio in memoria senza scadenze per questi casi. */
  }
  async get(key: string): Promise<RecoveryCopy | null> {
    return structuredClone(this.rows.get(key) ?? null);
  }
  async put(copy: RecoveryCopy): Promise<void> {
    this.rows.set(copy.key, structuredClone(copy));
  }
  async remove(key: string): Promise<void> {
    this.rows.delete(key);
  }
}
class Transport {
  calls: { id: string; kind: string; body: Record<string, unknown> }[] = [];
  hold: ((receipt: AttemptSave) => void) | null = null;
  blocked = false;
  failure = false;
  rowVersion = 1n;
  async grants(): Promise<readonly AttemptFinalizationGrant[]> {
    return [grant];
  }
  async save(id: string, kind: string, body: Record<string, unknown>): Promise<AttemptSave> {
    this.calls.push({ id, kind, body: structuredClone(body) });
    if (this.failure) throw new ApiError('RowVersionMismatch', 412, 'conflitto');
    const checkpoint = body['checkpoint'] as AttemptCheckpoint | undefined;
    const receipt: AttemptSave = {
      attemptId: id,
      saveId: body['saveId'] as string,
      rowVersion: String(++this.rowVersion),
      projectRevision:
        (body['clientProjectRevision'] as string) ?? checkpoint?.projectRevision ?? '0',
      workspaceHash: (body['workspaceHash'] as string) ?? checkpoint?.workspaceHash ?? '',
      lastSavedAtUtc: 'now',
      checkpointSaved: kind === 'checkpoint',
    };
    if (this.blocked)
      return new Promise((resolve) => {
        this.hold = resolve;
      });
    return receipt;
  }
}
async function settle(): Promise<void> {
  for (let i = 0; i < 30; i++) await Promise.resolve();
}
describe('AttemptAutosave', () => {
  let save: AttemptAutosave;
  let store: Store;
  let transport: Transport;
  beforeEach(async () => {
    store = new Store();
    transport = new Transport();
    TestBed.configureTestingModule({
      providers: [
        { provide: AttemptRecoveryStore, useValue: store },
        { provide: AttemptTransport, useValue: transport },
      ],
    });
    save = TestBed.inject(AttemptAutosave);
    await save.attach(snapshot, { ...attempt, workspaceHash: await workspaceDigest('{}') });
    save.setOperative(true);
  });
  afterEach(() => {
    save.dispose();
    vi.useRealTimers();
  });
  it('raggruppa le modifiche e cancella la copia solo dopo la ricevuta', async () => {
    save.change('{"a":1}');
    save.change('{"a":2}');
    await save.flush();
    expect(transport.calls).toHaveLength(1);
    expect(transport.calls[0].body['workspaceJson']).toBe('{"a":2}');
    expect(save.saved()?.projectRevision).toBe('2');
    expect(store.rows.size).toBe(0);
  });
  it('non salva o modifica senza operatività', async () => {
    save.setOperative(false);
    save.change('{"a":1}');
    await save.flush();
    expect(transport.calls).toHaveLength(0);
    expect(save.workspace()).toBe('{}');
  });
  it('conserva un conflitto senza sovrascrivere il server', async () => {
    transport.failure = true;
    save.change('{"a":1}');
    await save.flush();
    expect(save.conflict()).toBe(true);
    expect(store.rows.size).toBe(1);
    save.setOperative(true);
    await save.flush();
    expect(transport.calls).toHaveLength(1);
  });
  it('recupera una copia soltanto per lo stesso contesto verificato', async () => {
    save.change('{"a":1}');
    await settle();
    await save.flush();
    await store.put({
      key: 'other:p:t',
      sessionId: 'other',
      participantId: 'p',
      attemptId: 't',
      workspaceJson: 'private',
      workspaceHash: 'private',
      projectRevision: '3',
      baseRowVersion: '1',
      checkpoint: null,
      pending: null,
      expiresAt: Date.now() + 1000,
    });
    await save.attach({ ...snapshot, participantId: 'other', connectionEpoch: '2' }, attempt);
    expect(save.workspace()).toBe('{}');
  });
  it('ripristina la copia non confermata e sospende un conflitto di versione', async () => {
    transport.failure = true;
    save.change('{"a":1}');
    await save.flush();
    await save.attach({ ...snapshot, connectionEpoch: '2' }, { ...attempt, rowVersion: '9' });
    expect(save.workspace()).toBe('{}');
    expect(save.conflict()).toBe(true);
    expect(store.rows.size).toBe(1);
  });
  it('continua il salvataggio server anche se IndexedDB non è disponibile', async () => {
    vi.spyOn(store, 'put').mockRejectedValue(new Error('storage'));
    save.change('{"a":1}');
    await save.flush();
    expect(save.saved()?.projectRevision).toBe('1');
  });
  it('una risposta del tentativo precedente non aggiorna quello nuovo', async () => {
    save.change('{"a":1}');
    await save.flush();
    await save.attach(snapshot, { ...attempt, attemptId: 'new' });
    await save.finalize('t');
    expect(save.saved()).toBeNull();
    expect(save.workspace()).toBe('{}');
  });
  it('finalizza una volta e conserva lo stesso corpo dopo un rifiuto temporaneo', async () => {
    save.change('{"a":1}');
    await settle();
    transport.failure = true;
    await save.finalize('t');
    const first = transport.calls.at(-1)!;
    transport.failure = false;
    await save.finalize('t');
    expect(transport.calls.at(-1)!.body).toEqual(first.body);
    expect(store.rows.size).toBe(0);
  });
  it('ripete la finalizzazione persistita dopo refresh nel contesto del grant', async () => {
    save.change('{"a":1}');
    await settle();
    transport.failure = true;
    await save.finalize('t');
    const pending = transport.calls.at(-1)!.body;
    transport.failure = false;
    await save.resumeFinalizations();
    expect(transport.calls.at(-1)!.body).toEqual(pending);
    expect(store.rows.size).toBe(0);
  });
  it('mantiene una sola richiesta in corso e salva la modifica arrivata durante il trasporto', async () => {
    transport.blocked = true;
    save.change('{"a":1}');
    const first = save.flush();
    // Il digest asincrono deve terminare prima che inizi la prima richiesta.
    await vi.waitFor(() => expect(transport.hold).not.toBeNull());
    save.change('{"a":2}');
    await settle();
    expect(transport.calls).toHaveLength(1);
    const body = transport.calls[0].body;
    transport.blocked = false;
    transport.hold!({
      attemptId: 't',
      saveId: body['saveId'] as string,
      rowVersion: '2',
      projectRevision: '1',
      workspaceHash: body['workspaceHash'] as string,
      lastSavedAtUtc: 'now',
      checkpointSaved: false,
    });
    await first;
    await save.flush();
    expect(transport.calls).toHaveLength(2);
    expect(transport.calls[1].body['workspaceJson']).toBe('{"a":2}');
    expect(transport.calls[1].body['baseRowVersion']).toBe('2');
  });
});
