import { Injectable, InjectionToken, inject, signal } from '@angular/core';
import { WorkspacePreview, toApiError } from '@codi/api';
import { AttemptTransport } from './attempt-transport';
import { ParticipantRealtime } from './participant-realtime';
import { workspaceDigest } from './attempt-autosave';

/** Oltre questo limite l'anteprima non entra nel messaggio SignalR e passa dal collegamento HTTP. */
export const SIGNALR_PREVIEW_LIMIT_BYTES = 24 * 1024;

/** Oltre questo limite il server rifiuta l'anteprima: meglio non inviarla che farla respingere. */
export const HTTP_PREVIEW_LIMIT_BYTES = 256 * 1024;

/** Modifiche ravvicinate diventano un solo invio: il docente vede al massimo un'anteprima al secondo. */
export const PREVIEW_THROTTLE_MS = 1000;

/**
 * Impronta del progetto: la stessa che il server calcola sul contenuto, così il docente può
 * riconoscere se l'anteprima corrisponde al progetto salvato. È iniettabile perché la si possa
 * sostituire dove si prova il comportamento della pubblicazione.
 */
export const WORKSPACE_DIGEST = new InjectionToken<(text: string) => Promise<string>>(
  'WORKSPACE_DIGEST',
  { factory: () => workspaceDigest },
);

/**
 * Anteprima dei blocchi verso il docente (§15.5). Non è un salvataggio: aggrega le modifiche entro
 * un secondo e le invia dal metodo hub, ripiegando sul collegamento HTTP quando il progetto supera
 * il limite del messaggio (§11.3). La freschezza è quella della ricezione, e il contenuto inviato è
 * il progetto corrente dell'alunno: un'anteprima che non corrisponde al progetto salvato viene
 * mostrata al docente come parziale.
 */
@Injectable({ providedIn: 'root' })
export class WorkspacePreviewPublisher {
  private readonly realtime = inject(ParticipantRealtime);
  private readonly transport = inject(AttemptTransport);
  private readonly digest = inject(WORKSPACE_DIGEST);
  private attemptId: string | null = null;
  private revision = 0n;
  private latest: string | null = null;
  private operative = false;
  private inFlight = false;
  private timer: ReturnType<typeof setTimeout> | null = null;
  private lastSentAt = 0;

  /** Stato dell'ultimo invio, in parole: la pagina lo mostra come informazione di servizio. */
  readonly status = signal('Anteprima non ancora inviata.');

  /** Progetto già inviato: serve a non ripetere un invio identico. */
  private sent: string | null = null;

  /** Tentativo corrente. Cambiando tentativo l'anteprima precedente non vale più. */
  attach(attemptId: string, projectRevision: string): void {
    if (this.attemptId === attemptId) return;
    this.attemptId = attemptId;
    this.revision = BigInt(projectRevision);
    this.latest = null;
    this.sent = null;
    this.status.set('Anteprima non ancora inviata.');
  }

  detach(): void {
    this.clearTimer();
    this.attemptId = null;
    this.latest = null;
    this.sent = null;
  }

  /** Operativa quando il collegamento è autorizzato e il progetto dell'alunno è modificabile. */
  setOperative(operative: boolean): void {
    this.operative = operative;
    if (operative && this.latest !== null && this.latest !== this.sent) this.schedule(0);
  }

  /** Anteprima immediata: dopo il caricamento il docente vede subito i blocchi dell'alunno. */
  publishNow(workspaceJson: string, force = false): void {
    this.latest = workspaceJson;
    if (force || workspaceJson !== this.sent) this.schedule(0);
  }

  /** Modifica del progetto: si aggrega entro il passo di pubblicazione. */
  change(workspaceJson: string): void {
    if (this.attemptId === null) return;
    this.latest = workspaceJson;
    if (this.operative && workspaceJson !== this.sent) this.schedule(PREVIEW_THROTTLE_MS);
  }

  dispose(): void {
    this.clearTimer();
  }

  private schedule(delay: number): void {
    this.clearTimer();
    const wait =
      delay === 0 ? 0 : Math.max(0, PREVIEW_THROTTLE_MS - (Date.now() - this.lastSentAt));
    this.timer = setTimeout(() => {
      this.timer = null;
      void this.flush();
    }, wait);
  }

  private clearTimer(): void {
    if (this.timer !== null) clearTimeout(this.timer);
    this.timer = null;
  }

  private async flush(): Promise<void> {
    if (this.inFlight) {
      this.schedule(PREVIEW_THROTTLE_MS);
      return;
    }
    const json = this.latest;
    const attemptId = this.attemptId;
    const context = this.realtime.previewContext();
    if (json === null || attemptId === null || !context || !this.operative) return;
    this.inFlight = true;
    try {
      const workspaceHash = await this.digest(json);
      if (attemptId !== this.attemptId) return;
      const preview: WorkspacePreview = {
        protocolVersion: 1,
        attemptId,
        connectionEpoch: context.connectionEpoch,
        executionGeneration: context.executionGeneration,
        projectRevision: (++this.revision).toString(),
        workspaceHash,
        workspaceJson: json,
      };
      const size = byteLength(preview);
      if (size > HTTP_PREVIEW_LIMIT_BYTES) {
        this.status.set('Il progetto è troppo grande per essere mostrato al docente.');
        this.sent = json;
        return;
      }
      const fromHub =
        size <= SIGNALR_PREVIEW_LIMIT_BYTES
          ? await this.realtime.sendWorkspacePreview(preview)
          : false;
      if (!fromHub) await this.transport.preview(attemptId, preview);
      this.lastSentAt = Date.now();
      this.sent = json;
      this.status.set(
        fromHub ? 'Anteprima inviata al docente.' : 'Anteprima inviata dal collegamento HTTP.',
      );
    } catch (failure) {
      this.status.set(`Anteprima non inviata: ${toApiError(failure).message}`);
    } finally {
      this.inFlight = false;
      // Nel frattempo il progetto può essere cambiato di nuovo: si riprende dal più recente.
      if (this.latest !== json && this.latest !== this.sent) this.schedule(PREVIEW_THROTTLE_MS);
    }
  }
}

/** Dimensione del messaggio sul filo, come la misura il server. */
function byteLength(preview: WorkspacePreview): number {
  return new TextEncoder().encode(JSON.stringify(preview)).length;
}
