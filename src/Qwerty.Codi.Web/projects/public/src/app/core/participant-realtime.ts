import { Injectable, inject, signal } from '@angular/core';
import {
  ApiService,
  CommandIssuedEnvelope,
  DesiredState,
  ExecutionLease,
  ParticipantMessage,
  ParticipantProfile,
  ParticipantSnapshot,
  WorkspacePreview,
  toApiError,
} from '@codi/api';
import { SignalRAntiforgeryClient } from '@codi/api/signalr-antiforgery';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { ParticipantProtocolState, CommandOutcome } from './participant-protocol';
import { ExerciseRuntime } from './exercise-runtime';

export type RealtimeStatus =
  | 'idle'
  | 'connecting'
  | 'connected'
  | 'reconnecting'
  | 'waiting'
  | 'superseded'
  | 'blocked'
  | 'ended'
  | 'revoked';

/** Trasporto del partecipante con applicazione seriale, conferme e scadenza monotona. */
@Injectable({ providedIn: 'root' })
export class ParticipantRealtime {
  private readonly api = inject(ApiService);
  readonly runtime = inject(ExerciseRuntime);
  private protocol = new ParticipantProtocolState();
  private connection: HubConnection | null = null;
  private heartbeatTimer: ReturnType<typeof setInterval> | null = null;
  private tickTimer: ReturnType<typeof setInterval> | null = null;
  private retryTimer: ReturnType<typeof setTimeout> | null = null;
  private readonly instanceId = crypto.randomUUID();
  private sequence = 0n;
  private telemetryPending = false;
  private lastTelemetry = 0;
  private workspaceHash: string | null = null;
  private application: (state: DesiredState) => Promise<void> = () => Promise.resolve();
  private queue: Promise<void> = Promise.resolve();
  private readonly visibilityChanged = () => {
    this.invalidateLease();
    if (document.visibilityState === 'visible' && this.connection !== null)
      void this.sendHeartbeat(this.connection);
  };

  readonly status = signal<RealtimeStatus>('idle');
  readonly desiredState = signal<DesiredState | null>(null);
  readonly leaseValidUntilUtc = signal<string | null>(null);
  readonly authorized = signal(false);
  readonly notice = signal('');
  readonly lastCommand = signal<{ commandName: string; outcome: string } | null>(null);
  readonly profile = signal<ParticipantProfile | null>(null);

  /**
   * Messaggi della chat annunciati dall'hub (§8), in ordine di arrivo. L'hub porta **il messaggio**,
   * non il thread: chi compone il thread legge la query all'apertura e a ogni riconnessione, perché
   * l'hub non ripete ciò che è arrivato mentre la pagina non c'era.
   */
  readonly chatIncoming = signal<readonly ParticipantMessage[]>([]);

  setStateApplier(application: (state: DesiredState) => Promise<void>): void {
    this.application = application;
  }
  currentSnapshot(): ParticipantSnapshot | null {
    return this.protocol.snapshot;
  }
  private persistedRevision: string | null = null;
  setPersistedRevision(revision: string | null): void {
    this.persistedRevision = revision;
  }
  setWorkspaceHash(hash: string | null): void {
    this.workspaceHash = hash;
  }

  /**
   * Contesto che l'anteprima deve dichiarare (§15.5): epoca della connessione e generazione in
   * corso. `null` quando non c'è un lavoro da descrivere: senza stato non si pubblica nulla.
   */
  previewContext(): { connectionEpoch: string; executionGeneration: string } | null {
    const snapshot = this.protocol.snapshot;
    const state = this.desiredState();
    if (snapshot === null || state?.view !== 'Exercise') return null;

    return {
      connectionEpoch: snapshot.connectionEpoch,
      executionGeneration: state.executionGeneration ?? '0',
    };
  }

  /**
   * Anteprima dal metodo hub quando il collegamento è pronto; `false` quando il chiamante deve
   * ripiegare sul collegamento HTTP (§15.5).
   */
  async sendWorkspacePreview(preview: WorkspacePreview): Promise<boolean> {
    const connection = this.connection;
    if (
      connection === null ||
      connection.state !== HubConnectionState.Connected ||
      this.status() !== 'connected'
    )
      return false;
    try {
      await connection.invoke('WorkspacePreviewUpdated', preview);
      return true;
    } catch {
      // Il publisher prova HTTP e possiede l'esito finale dell'anteprima.
      // Un tentativo hub rifiutato non deve lasciare un avviso globale persistente.
      return false;
    }
  }

  async start(): Promise<void> {
    if (this.connection !== null) return;
    this.protocol = new ParticipantProtocolState();
    this.queue = Promise.resolve();
    this.status.set('connecting');
    this.profile.set(null);
    this.chatIncoming.set([]);
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/v1/participant', {
        httpClient: new SignalRAntiforgeryClient('/hubs/v1/participant'),
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build();
    this.connection = connection;
    const enqueue = (action: () => Promise<void>) => {
      this.queue = this.queue
        .then(async () => {
          if (this.connection === connection) await action();
        })
        .catch((failure) => {
          if (this.connection !== connection) return;
          this.invalidateLease();
          this.notice.set(toApiError(failure).message);
        });
    };
    connection.on('SessionSnapshot', (snapshot: ParticipantSnapshot) =>
      enqueue(() => this.applySnapshot(connection, snapshot)),
    );
    connection.on('SessionEnded', (data: { command: CommandIssuedEnvelope }) => {
      if (this.connection !== connection) return;
      this.invalidateLease();
      enqueue(() => this.applyCommand(connection, data.command));
    });
    connection.on('CommandIssued', (command: CommandIssuedEnvelope) => {
      if (this.connection !== connection) return;
      this.invalidateLease();
      enqueue(() => this.applyCommand(connection, command));
    });
    // Messaggio della chat (§8): non cambia il lavoro dell'alunno, quindi non passa dalla coda delle
    // applicazioni e non tocca l'autorizzazione temporanea: si accoda e basta.
    connection.on('ParticipantMessageReceived', (message: ParticipantMessage) => {
      if (this.connection !== connection) return;
      this.chatIncoming.update((rows) => [...rows, message]);
    });
    connection.on('LeaseGranted', (lease: ExecutionLease) => {
      if (
        this.connection === connection &&
        document.visibilityState === 'visible' &&
        this.protocol.acceptLease(lease, performance.now())
      ) {
        this.leaseValidUntilUtc.set(lease.validUntilUtc);
        this.authorized.set(true);
        this.acceptProfile(lease.profile);
      }
    });
    connection.on('ConnectionSuperseded', () => {
      if (this.connection !== connection) return;
      if (this.desiredState()?.view === 'Goodbye') return;
      this.finish('superseded');
      void connection.stop();
    });
    connection.on('ParticipationRevoked', () => {
      if (this.connection !== connection) return;
      this.finish('revoked');
      void connection.stop();
    });
    connection.on('ActiveConnectionExists', (data: { retryAfterSeconds: string }) => {
      if (this.connection !== connection) return;
      this.finish('waiting');
      this.notice.set('Un’altra pagina è già in uso. Attendi il collegamento.');
      this.retryTimer = setTimeout(
        () => {
          if (this.connection === connection) void this.initialize(connection);
        },
        Math.max(1, Number(data.retryAfterSeconds)) * 1000,
      );
    });
    connection.onreconnecting(() => {
      if (this.connection === connection) this.finish('reconnecting');
    });
    connection.onreconnected(() => {
      if (this.connection !== connection) return;
      void this.initialize(connection);
    });
    connection.onclose(() => {
      if (this.connection !== connection) return;
      if (this.status() === 'connected' || this.status() === 'reconnecting') this.finish('blocked');
    });
    document.addEventListener('visibilitychange', this.visibilityChanged);
    try {
      await connection.start();
      if (this.connection !== connection) return;
      await this.initialize(connection);
    } catch (failure) {
      if (this.connection !== connection) return;
      this.finish('blocked');
      this.notice.set(`Collegamento non riuscito: ${toApiError(failure).message}`);
      if (this.connection === connection) this.connection = null;
      await connection.stop();
      document.removeEventListener('visibilitychange', this.visibilityChanged);
    }
  }

  async stop(): Promise<void> {
    this.finish('idle');
    const connection = this.connection;
    this.connection = null;
    this.desiredState.set(null);
    this.profile.set(null);
    this.workspaceHash = null;
    this.chatIncoming.set([]);
    this.runtime.stop();
    document.removeEventListener('visibilitychange', this.visibilityChanged);
    if (connection !== null) await connection.stop();
  }

  private acceptProfile(profile: ParticipantProfile | null | undefined): void {
    if (
      !profile ||
      profile.participantId !== this.protocol.snapshot?.participantId ||
      !/^\d+$/.test(profile.rowVersion)
    )
      return;
    const current = this.profile();
    if (current && BigInt(profile.rowVersion) <= BigInt(current.rowVersion)) return;
    this.profile.set(profile);
  }

  private async initialize(connection: HubConnection): Promise<void> {
    if (this.connection !== connection) return;
    this.invalidateLease();
    try {
      await connection.invoke('InitializeClient', {
        protocolVersion: 1,
        clientInstanceId: this.instanceId,
        appVersion: 't7',
        engineVersion: 'gridpath-1',
      });
    } catch (failure) {
      if (this.connection !== connection) return;
      this.finish('blocked');
      this.notice.set(`Sincronizzazione non riuscita: ${toApiError(failure).message}`);
    }
  }

  private async applySnapshot(
    connection: HubConnection,
    incoming: ParticipantSnapshot,
  ): Promise<void> {
    if (!this.protocol.acceptSnapshot(incoming)) return;
    this.acceptProfile(incoming.profile);
    this.invalidateLease();
    await this.application(incoming.desiredState);
    if (this.connection !== connection) return;
    const uncertain = await this.runtime.initialize(incoming.desiredState);
    this.desiredState.set(incoming.desiredState);
    await connection.invoke('SnapshotApplied', {
      sessionRevision: incoming.effectiveRevision,
      connectionEpoch: incoming.connectionEpoch,
    });
    if (uncertain && !incoming.desiredState.recoveryBlocked) {
      const recovered = await this.api.executeCommand<ParticipantSnapshot>(
        'ReportRecoveryBlocked',
        {
          connectionEpoch: incoming.connectionEpoch,
          attemptId: incoming.desiredState.attemptId,
          effectiveRevision: incoming.effectiveRevision,
        },
      );
      if (this.connection !== connection || !this.protocol.acceptSnapshot(recovered)) return;
      this.desiredState.set(recovered.desiredState);
      await this.runtime.initialize(recovered.desiredState);
    }
    if (this.connection !== connection) return;
    this.status.set('connected');
    this.notice.set('');
    this.startTimers(connection);
    await this.sendHeartbeat(connection);
  }

  private async applyCommand(
    connection: HubConnection,
    command: CommandIssuedEnvelope,
  ): Promise<void> {
    let outcome: CommandOutcome | 'apply' = this.protocol.commandOutcome(command);
    if (outcome === 'apply') {
      try {
        await this.application(command.desiredState);
        if (this.connection !== connection) return;
        const applied = await this.runtime.apply(command.desiredState);
        this.desiredState.set(command.desiredState);
        // Una pausa non confermata dal worker è applicata ma non garantita (§13.7, §12.2).
        outcome = applied === 'recoveryRequired' ? 'AppliedWithRecoveryRequired' : 'Applied';
        this.protocol.applied(command, outcome);
      } catch (failure) {
        outcome = 'Failed';
        this.notice.set(toApiError(failure).message);
        this.protocol.applied(command, outcome);
      }
    }
    this.lastCommand.set({ commandName: command.commandName, outcome });
    await connection.invoke('CommandAcknowledged', {
      commandId: command.commandId,
      protocolVersion: 1,
      effectiveRevision: command.effectiveRevision,
      connectionEpoch: command.connectionEpoch,
      outcome,
      runtimeStatus: this.runtime.status(),
      lastPersistedProjectRevision: this.persistedRevision,
      errorCode: outcome === 'Failed' ? 'ClientApplicationFailed' : null,
    });
    if (command.desiredState.view === 'Goodbye' && outcome === 'Applied') {
      this.finish('ended');
      await connection.stop();
      return;
    }
    if (outcome === 'Applied' || outcome === 'AppliedWithRecoveryRequired')
      await this.sendHeartbeat(connection);
  }

  private startTimers(connection: HubConnection): void {
    this.clearTimers();
    this.heartbeatTimer = setInterval(() => {
      void this.sendHeartbeat(connection);
    }, 3000);
    this.tickTimer = setInterval(() => {
      const state = this.desiredState();
      const permitted =
        this.status() === 'connected' &&
        document.visibilityState === 'visible' &&
        performance.now() < this.protocol.leaseUntil &&
        state?.view === 'Exercise' &&
        state.interactionEnabled &&
        !state.teacherPaused &&
        !state.recoveryBlocked;
      this.authorized.set(permitted);
      this.runtime.tick(
        permitted,
        permitted ? Math.max(0, this.protocol.leaseUntil - performance.now()) : 0,
      );
      void this.publishObservation(connection);
    }, 300);
  }

  private async sendHeartbeat(connection: HubConnection): Promise<void> {
    const snapshot = this.protocol.snapshot;
    if (
      this.connection !== connection ||
      connection.state !== HubConnectionState.Connected ||
      snapshot === null ||
      this.desiredState()?.view === 'Goodbye' ||
      this.status() !== 'connected'
    )
      return;
    const nonce = crypto.randomUUID();
    this.protocol.heartbeat(nonce, performance.now());
    try {
      await connection.invoke('Heartbeat', {
        sessionRevision: snapshot.effectiveRevision,
        connectionEpoch: snapshot.connectionEpoch,
        nonce,
        pageVisible: document.visibilityState === 'visible',
        serverInstanceId: snapshot.serverInstanceId,
      });
    } catch (failure) {
      if (this.connection !== connection) return;
      this.invalidateLease();
      this.notice.set(`Battito non riuscito: ${toApiError(failure).message}`);
    }
  }

  private async publishObservation(connection: HubConnection): Promise<void> {
    const snapshot = this.protocol.snapshot;
    if (
      this.connection !== connection ||
      this.telemetryPending ||
      this.status() !== 'connected' ||
      snapshot === null ||
      performance.now() >= this.protocol.leaseUntil ||
      performance.now() - this.lastTelemetry < 500
    )
      return;
    this.telemetryPending = true;
    this.lastTelemetry = performance.now();
    try {
      await connection.invoke('ActivityStateUpdated', {
        protocolVersion: 1,
        serverInstanceId: snapshot.serverInstanceId,
        connectionEpoch: snapshot.connectionEpoch,
        effectiveRevision: snapshot.effectiveRevision,
        attemptId: snapshot.desiredState.attemptId,
        executionGeneration: snapshot.desiredState.executionGeneration ?? '0',
        sequence: String(++this.sequence),
        runtimeStatus: this.runtime.status(),
        reason: 'GridPathPlayer',
        runId: this.runtime.runId(),
        workspaceHash: this.workspaceHash,
        scene: this.runtime.scene(),
      });
      if (this.notice().startsWith('Stato non inviato:')) this.notice.set('');
    } catch (failure) {
      if (
        this.connection !== connection ||
        snapshot !== this.protocol.snapshot ||
        performance.now() >= this.protocol.leaseUntil
      )
        return;
      this.notice.set(`Stato non inviato: ${toApiError(failure).message}`);
    } finally {
      this.telemetryPending = false;
    }
  }

  private invalidateLease(): void {
    this.protocol.invalidateLease();
    this.authorized.set(false);
    this.leaseValidUntilUtc.set(null);
    this.runtime.revokePermit();
  }
  private clearTimers(): void {
    if (this.heartbeatTimer !== null) clearInterval(this.heartbeatTimer);
    if (this.tickTimer !== null) clearInterval(this.tickTimer);
    if (this.retryTimer !== null) clearTimeout(this.retryTimer);
    this.heartbeatTimer = null;
    this.tickTimer = null;
    this.retryTimer = null;
  }
  private finish(status: RealtimeStatus): void {
    this.clearTimers();
    this.invalidateLease();
    this.status.set(status);
  }
}
