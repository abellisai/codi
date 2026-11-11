import { Injectable } from '@angular/core';
import type { AttemptCheckpoint } from './models';

/** Una copia per tentativo. Nessuna credenziale entra nell'archivio locale. */
export interface RecoveryCopy {
  key: string;
  sessionId: string;
  participantId: string;
  attemptId: string;
  workspaceJson: string;
  workspaceHash: string;
  projectRevision: string;
  baseRowVersion: string;
  checkpoint: AttemptCheckpoint | null;
  pending: {
    kind: 'workspace' | 'checkpoint' | 'finalization';
    body: Record<string, unknown>;
  } | null;
  expiresAt: number;
}

@Injectable({ providedIn: 'root' })
export class AttemptRecoveryStore {
  private database: Promise<IDBDatabase> | null = null;

  private open(): Promise<IDBDatabase> {
    return (this.database ??= new Promise((resolve, reject) => {
      // Identificatore persistente: conservarlo rende accessibili anche le copie precedenti alla rinomina in Codì.
      const request = indexedDB.open('aulaviva-attempt-recovery', 1);
      request.onupgradeneeded = () =>
        request.result.createObjectStore('copies', { keyPath: 'key' });
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => {
        this.database = null;
        reject(request.error);
      };
    }));
  }

  private async transaction<T>(
    mode: IDBTransactionMode,
    action: (store: IDBObjectStore) => IDBRequest<T>,
  ): Promise<T> {
    const database = await this.open();
    return new Promise((resolve, reject) => {
      const transaction = database.transaction('copies', mode);
      const request = action(transaction.objectStore('copies'));
      transaction.oncomplete = () => resolve(request.result);
      transaction.onerror = () => reject(transaction.error);
      transaction.onabort = () => reject(transaction.error);
    });
  }

  async get(key: string): Promise<RecoveryCopy | null> {
    const copy = await this.transaction<RecoveryCopy | undefined>('readonly', (store) =>
      store.get(key),
    );
    if (copy === undefined) return null;
    if (copy.expiresAt <= Date.now()) {
      await this.remove(key);
      return null;
    }
    return copy;
  }

  async put(copy: RecoveryCopy): Promise<void> {
    await this.prune();
    await this.transaction('readwrite', (store) => store.put(copy));
  }

  async remove(key: string): Promise<void> {
    await this.transaction('readwrite', (store) => store.delete(key));
  }

  /** Solo l'area docente chiama questa lettura, dopo una verifica del contesto server. */
  async list(): Promise<readonly RecoveryCopy[]> {
    await this.prune();
    return this.transaction<RecoveryCopy[]>('readonly', (store) => store.getAll());
  }

  async prune(): Promise<void> {
    const rows = await this.transaction<RecoveryCopy[]>('readonly', (store) => store.getAll());
    const ordered = rows.sort((a, b) => b.expiresAt - a.expiresAt);
    for (const [index, copy] of ordered.entries())
      if (copy.expiresAt <= Date.now() || index >= 63) await this.remove(copy.key);
  }
}
