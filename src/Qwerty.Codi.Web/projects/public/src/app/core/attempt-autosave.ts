import { Injectable, inject, signal } from '@angular/core';
import type { Attempt, AttemptCheckpoint, AttemptSave, ParticipantSnapshot } from '@codi/api';
import { toApiError } from '@codi/api';
import { AttemptTransport } from './attempt-transport';
import { AttemptRecoveryStore, RecoveryCopy } from './attempt-recovery-store';

interface SaveState {
  attempt: Attempt;
  copy: RecoveryCopy;
  epoch: string;
  busy: Promise<void> | null;
  changes: Promise<void>;
  dirty: boolean;
  conflict: boolean;
  finalizing: boolean;
  firstChangeAt: number;
  timer: ReturnType<typeof setTimeout> | null;
}

/** Coalescenza, copia locale e ricevute. Una richiesta in corso per tentativo. */
@Injectable({ providedIn: 'root' })
export class AttemptAutosave {
  private readonly transport = inject(AttemptTransport);
  private readonly store = inject(AttemptRecoveryStore);
  private current: SaveState | null = null;
  private readonly states = new Map<string, SaveState>();
  private operative = false;
  readonly status = signal('Nessun lavoro da salvare.');
  readonly workspace = signal<string | null>(null);
  readonly saved = signal<AttemptSave | null>(null);
  readonly conflict = signal(false);

  async attach(snapshot: ParticipantSnapshot, attempt: Attempt): Promise<void> {
    const key = `${snapshot.sessionId}:${snapshot.participantId}:${attempt.attemptId}`;
    if (this.current?.copy.key === key && this.current.epoch === snapshot.connectionEpoch) return;
    await this.store.prune().catch(() => undefined);
    const stored = await this.store.get(key).catch(() => null);
    const confirmed =
      stored !== null &&
      stored.workspaceHash === attempt.workspaceHash &&
      BigInt(stored.projectRevision) <= BigInt(attempt.projectRevision) &&
      (stored.checkpoint === null || JSON.stringify(stored.checkpoint) === attempt.checkpointJson);
    if (confirmed) await this.store.remove(key);
    const copy: RecoveryCopy =
      stored !== null && !confirmed
        ? stored
        : {
            key,
            sessionId: snapshot.sessionId,
            participantId: snapshot.participantId,
            attemptId: attempt.attemptId,
            workspaceJson: attempt.workspaceJson,
            workspaceHash: attempt.workspaceHash,
            projectRevision: attempt.projectRevision,
            baseRowVersion: attempt.rowVersion ?? '1',
            checkpoint: null,
            pending: null,
            expiresAt: Date.now() + 86400000,
          };
    const state: SaveState = {
      attempt,
      copy,
      epoch: snapshot.connectionEpoch,
      busy: null,
      changes: Promise.resolve(),
      dirty: stored !== null && !confirmed,
      conflict:
        stored !== null && !confirmed && copy.baseRowVersion !== (attempt.rowVersion ?? '1'),
      finalizing: false,
      firstChangeAt: 0,
      timer: null,
    };
    // Un nuovo handshake richiede una nuova richiesta, dopo il confronto con il server.
    if (state.dirty && !state.conflict) copy.pending = null;
    this.current = state;
    this.states.set(attempt.attemptId, state);
    if (this.states.size > 16) this.states.delete(this.states.keys().next().value!);
    this.workspace.set(state.conflict ? attempt.workspaceJson : copy.workspaceJson);
    this.conflict.set(state.conflict);
    this.status.set(
      state.conflict
        ? 'Esiste una copia locale in conflitto. Chiedi al docente.'
        : state.dirty
          ? 'Ho recuperato modifiche ancora da salvare.'
          : 'Lavoro salvato.',
    );
    this.saved.set(null);
  }

  setOperative(operative: boolean): void {
    this.operative = operative;
    if (operative && this.current?.dirty) this.schedule(this.current, true);
  }

  change(workspaceJson: string): void {
    const state = this.current;
    if (state === null || !this.operative || state.conflict || state.finalizing) return;
    state.changes = state.changes
      .then(async () => {
        if (workspaceJson === state.copy.workspaceJson) return;
        state.copy.workspaceJson = workspaceJson;
        state.copy.workspaceHash = await workspaceDigest(workspaceJson);
        state.copy.projectRevision = (BigInt(state.copy.projectRevision) + 1n).toString();
        state.copy.checkpoint = null;
        state.copy.expiresAt = Date.now() + 86400000;
        state.dirty = true;
        this.workspace.set(workspaceJson);
        this.status.set('Modifiche da salvare.');
        await this.keepLocal(state.copy);
        this.schedule(state, false);
      })
      .catch(() => {
        this.status.set('La copia locale non è disponibile. Mantieni aperta la pagina.');
      });
  }

  checkpoint(checkpoint: AttemptCheckpoint): void {
    const state = this.current;
    if (state === null || state.conflict || state.finalizing) return;
    state.changes = state.changes.then(async () => {
      state.copy.checkpoint = {
        ...checkpoint,
        workspaceHash: state.copy.workspaceHash,
        projectRevision: state.copy.projectRevision,
      };
      state.dirty = true;
      await this.keepLocal(state.copy);
      this.schedule(state, true);
    });
  }

  private schedule(state: SaveState, immediate: boolean): void {
    if (state.timer !== null) clearTimeout(state.timer);
    if (state.firstChangeAt === 0) state.firstChangeAt = Date.now();
    const delay = immediate
      ? 0
      : Math.max(0, Math.min(1000, 5000 - (Date.now() - state.firstChangeAt)));
    state.timer = setTimeout(() => {
      state.timer = null;
      void this.flushState(state);
    }, delay);
  }

  async flush(): Promise<void> {
    if (this.current !== null) {
      await this.current.changes;
      await this.flushState(this.current);
    }
  }

  private async flushState(state: SaveState): Promise<void> {
    if (state.busy !== null) return state.busy;
    if (!state.dirty || state.conflict || state.finalizing || !this.operative) return;
    state.busy = this.send(state).finally(() => {
      state.busy = null;
    });
    await state.busy;
    if (
      state.copy.pending === null &&
      state.dirty &&
      !state.conflict &&
      this.operative &&
      !state.finalizing
    )
      this.schedule(state, true);
  }

  private async send(state: SaveState): Promise<void> {
    const copy = state.copy;
    if (copy.pending === null) {
      const changed =
        copy.workspaceHash !== state.attempt.workspaceHash ||
        copy.projectRevision !== state.attempt.projectRevision;
      const kind = changed ? 'workspace' : 'checkpoint';
      if (!changed && copy.checkpoint === null) {
        state.dirty = false;
        return;
      }
      copy.pending = {
        kind,
        body: {
          saveId: crypto.randomUUID(),
          connectionEpoch: state.epoch,
          baseRowVersion: state.attempt.rowVersion ?? '1',
          ...(changed
            ? {
                clientProjectRevision: copy.projectRevision,
                workspaceJson: copy.workspaceJson,
                workspaceHash: copy.workspaceHash,
              }
            : { checkpoint: copy.checkpoint }),
        },
      };
      await this.keepLocal(copy);
    }
    const pending = copy.pending;
    if (this.current === state) this.status.set('Salvataggio in corso…');
    try {
      const response = await this.transport.save(copy.attemptId, pending.kind, pending.body);
      if (response.attemptId !== copy.attemptId || response.saveId !== pending.body['saveId'])
        throw new Error('La ricevuta appartiene a un altro salvataggio.');
      state.attempt = {
        ...state.attempt,
        rowVersion: response.rowVersion,
        projectRevision: response.projectRevision,
        workspaceHash: response.workspaceHash,
        lastSavedAtUtc: response.lastSavedAtUtc,
      };
      copy.baseRowVersion = response.rowVersion;
      copy.pending = null;
      if (
        pending.kind === 'checkpoint' &&
        JSON.stringify(copy.checkpoint) === JSON.stringify(pending.body['checkpoint'])
      )
        copy.checkpoint = null;
      state.dirty =
        copy.workspaceHash !== response.workspaceHash ||
        copy.projectRevision !== response.projectRevision ||
        copy.checkpoint !== null;
      state.firstChangeAt = 0;
      if (state.dirty) await this.keepLocal(copy);
      else await this.store.remove(copy.key).catch(() => undefined);
      if (this.current === state) {
        this.saved.set(response);
        this.status.set(state.dirty ? 'Modifiche da salvare.' : 'Lavoro salvato.');
      }
    } catch (failure) {
      const error = toApiError(failure);
      state.conflict =
        error.status === 409 ||
        error.status === 412 ||
        error.status === 401 ||
        error.status === 403;
      if (this.current === state) {
        this.conflict.set(state.conflict);
        this.status.set(
          state.conflict
            ? 'Salvataggio sospeso. La copia locale resta disponibile per il docente.'
            : 'Connessione interrotta. La copia locale conserva il lavoro.',
        );
      }
      // Il prossimo rinnovo operativo o un tentativo esplicito ritenta lo stesso corpo.
      if (!state.conflict && this.operative && !state.finalizing) {
        state.timer = setTimeout(() => {
          state.timer = null;
          void this.flushState(state);
        }, 2000);
      }
    }
  }

  async finalize(attemptId: string): Promise<void> {
    const state = this.states.get(attemptId);
    if (state === undefined || state.finalizing) return;
    state.finalizing = true;
    if (state.timer !== null) clearTimeout(state.timer);
    await state.changes;
    if (state.busy !== null) await state.busy;
    try {
      const grant = (await this.transport.grants()).find(
        (row) => row.attemptId === attemptId && row.connectionEpoch === state.epoch,
      );
      if (grant === undefined) {
        state.finalizing = false;
        return;
      }
      const body =
        state.copy.pending?.kind === 'finalization'
          ? state.copy.pending.body
          : {
              grantId: grant.grantId,
              saveId: crypto.randomUUID(),
              connectionEpoch: grant.connectionEpoch,
              baseRowVersion: state.attempt.rowVersion ?? '1',
              clientProjectRevision: state.copy.projectRevision,
              workspaceJson: state.copy.workspaceJson,
              workspaceHash: state.copy.workspaceHash,
              checkpoint:
                state.copy.checkpoint !== null &&
                state.copy.checkpoint.executionGeneration === grant.executionGeneration
                  ? state.copy.checkpoint
                  : null,
            };
      state.copy.pending = { kind: 'finalization', body };
      await this.keepLocal(state.copy);
      const response = await this.transport.save(attemptId, 'finalization', body);
      if (response.attemptId !== attemptId || response.saveId !== body['saveId'])
        throw new Error('Ricevuta conclusiva incoerente.');
      await this.store.remove(state.copy.key);
      if (this.current === state) {
        this.saved.set(response);
        this.status.set('Ultimo lavoro salvato.');
      }
    } catch {
      state.finalizing = false;
      if (this.current === state)
        this.status.set('Ultimo salvataggio non confermato. La copia locale resta disponibile.');
    }
  }

  private async keepLocal(copy: RecoveryCopy): Promise<void> {
    try {
      await this.store.put(copy);
    } catch {
      this.status.set('La copia locale non è disponibile. Mantieni aperta la pagina.');
    }
  }

  /** La credenziale verificata dal server delimita le copie accessibili dopo il refresh. */
  async resumeFinalizations(): Promise<void> {
    try {
      for (const grant of await this.transport.grants()) {
        const key = `${grant.sessionId}:${grant.participantId}:${grant.attemptId}`;
        const copy = await this.store.get(key);
        if (
          copy === null ||
          copy.pending?.kind !== 'finalization' ||
          copy.pending.body['grantId'] !== grant.grantId ||
          copy.pending.body['connectionEpoch'] !== grant.connectionEpoch
        )
          continue;
        const receipt = await this.transport.save(
          grant.attemptId,
          'finalization',
          copy.pending.body,
        );
        if (receipt.attemptId === grant.attemptId && receipt.saveId === copy.pending.body['saveId'])
          await this.store.remove(key);
      }
    } catch {
      /* Le copie non confermate restano disponibili per il docente. */
    }
  }

  localCopy(): RecoveryCopy | null {
    return this.current?.copy ?? null;
  }

  dispose(): void {
    this.operative = false;
    for (const state of this.states.values()) if (state.timer !== null) clearTimeout(state.timer);
  }
}

export async function workspaceDigest(text: string): Promise<string> {
  const hash = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text));
  return Array.from(new Uint8Array(hash), (value) => value.toString(16).padStart(2, '0')).join('');
}
