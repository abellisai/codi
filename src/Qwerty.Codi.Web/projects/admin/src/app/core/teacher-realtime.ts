import { Injectable, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { ParticipantMessage, TeacherSnapshot, WorkspacePreviewReceipt } from '@codi/api';
import { SignalRAntiforgeryClient } from '@codi/api/signalr-antiforgery';

@Injectable({ providedIn: 'root' })
export class TeacherRealtime {
  readonly snapshot = signal<TeacherSnapshot | null>(null);
  readonly online = signal(false);

  /**
   * Anteprime annunciate dall'hub (§15.5), una per alunno. Contiene identità, revisione, impronta
   * e istante di ricezione: il contenuto si legge a parte, e solo per l'alunno che il docente ha
   * davanti. Nella griglia non si aprono dodici editor (§14.3).
   */
  readonly previews = signal<readonly WorkspacePreviewReceipt[]>([]);

  /**
   * Messaggi della chat annunciati dall'hub (§8, §15.1), nell'ordine di arrivo. L'hub non porta il
   * thread: porta solo il messaggio nuovo, perché il thread vive in memoria e non si replaya. Chi
   * lo usa decide a quale alunno appartiene e lo deduplica.
   */
  readonly chat = signal<readonly ParticipantMessage[]>([]);

  private connection: HubConnection | null = null;
  private generation = 0;

  async observe(sessionId: string): Promise<void> {
    const generation = ++this.generation;
    const prior = this.connection;
    this.connection = null;
    this.online.set(false);
    this.snapshot.set(null);
    this.previews.set([]);
    this.chat.set([]);
    if (prior !== null) await prior.stop();
    if (generation !== this.generation) return;
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/v1/teacher', { httpClient: new SignalRAntiforgeryClient() })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build();
    this.connection = connection;
    // Il server invia gli eventi granulari insieme allo snapshot completo.
    // Lo snapshot resta l'unica sorgente dello stato della regia.
    for (const event of [
      'ParticipantJoined',
      'ParticipantPresenceChanged',
      'ParticipantStateUpdated',
      'CommandStatusChanged',
    ]) {
      connection.on(event, () => undefined);
    }
    connection.on('TeacherSnapshot', (snapshot: TeacherSnapshot) => {
      if (
        generation !== this.generation ||
        snapshot.protocolVersion !== 1 ||
        snapshot.session.id !== sessionId
      )
        return;
      const current = this.snapshot();
      if (
        current !== null &&
        current.serverInstanceId === snapshot.serverInstanceId &&
        BigInt(snapshot.session.sessionRevision) < BigInt(current.session.sessionRevision)
      )
        return;
      this.snapshot.set(snapshot);
      this.online.set(true);
    });
    connection.on('ObservationRevoked', () => {
      if (generation === this.generation) this.online.set(false);
      void connection.stop();
    });
    connection.on('WorkspacePreviewAvailable', (receipt: WorkspacePreviewReceipt) => {
      if (generation !== this.generation || receipt.sessionId !== sessionId) return;
      this.previews.update((rows) => [
        ...rows.filter((row) => row.participantId !== receipt.participantId),
        receipt,
      ]);
    });
    // Messaggio nuovo della chat (§8, §15.1): l'hub non manda il thread, solo il messaggio.
    connection.on('ParticipantMessagePosted', (message: ParticipantMessage) => {
      if (generation !== this.generation || message.sessionId !== sessionId) return;
      this.chat.update((rows) => [...rows, message]);
    });
    connection.onreconnecting(() => {
      if (generation === this.generation) this.online.set(false);
    });
    connection.onclose(() => {
      if (generation === this.generation) this.online.set(false);
    });
    connection.onreconnected(async () => {
      if (generation !== this.generation) return;
      this.online.set(false);
      try {
        await connection.invoke('ObserveSession', sessionId);
      } catch {
        this.online.set(false);
      }
    });
    try {
      await connection.start();
      if (generation !== this.generation) {
        await connection.stop();
        return;
      }
      await connection.invoke('ObserveSession', sessionId);
    } catch {
      this.online.set(false);
    }
  }

  async stop(): Promise<void> {
    ++this.generation;
    const connection = this.connection;
    this.connection = null;
    this.online.set(false);
    this.snapshot.set(null);
    if (connection !== null) await connection.stop();
  }
}
