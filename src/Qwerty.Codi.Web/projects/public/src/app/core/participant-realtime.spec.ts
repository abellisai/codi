import { TestBed } from '@angular/core/testing';
import {
  ApiService,
  CommandIssuedEnvelope,
  DesiredState,
  ParticipantSnapshot,
  WorkspacePreview,
} from '@codi/api';
import { vi } from 'vitest';
import { ParticipantRealtime } from './participant-realtime';

const transport = vi.hoisted(() => ({ current: null as unknown as Transport }));
interface Transport {
  state: string;
  handlers: Map<string, (data: unknown) => void>;
  calls: { method: string; data: Record<string, unknown> }[];
  emit(method: string, data: unknown): void;
}
vi.mock('@microsoft/signalr', () => {
  class HttpClient {}
  class DefaultHttpClient extends HttpClient {}
  class Connection {
    state = 'Connected';
    handlers = new Map<string, (data: unknown) => void>();
    calls: { method: string; data: Record<string, unknown> }[] = [];
    on(method: string, callback: (data: unknown) => void): void {
      this.handlers.set(method, callback);
    }
    emit(method: string, data: unknown): void {
      this.handlers.get(method)?.(data);
    }
    onreconnecting(): void {
      /* Il test non simula una riconnessione. */
    }
    onreconnected(): void {
      /* Il test non simula una riconnessione. */
    }
    onclose(): void {
      /* La chiusura esplicita usa stop. */
    }
    start(): Promise<void> {
      return Promise.resolve();
    }
    stop(): Promise<void> {
      return Promise.resolve();
    }
    async invoke(method: string, data: Record<string, unknown>): Promise<void> {
      this.calls.push({ method, data });
    }
  }
  class Builder {
    withUrl(): this {
      return this;
    }
    withAutomaticReconnect(): this {
      return this;
    }
    configureLogging(): this {
      return this;
    }
    build(): Connection {
      const connection = new Connection();
      transport.current = connection;
      return connection;
    }
  }
  return {
    HttpClient,
    DefaultHttpClient,
    NullLogger: { instance: {} },
    HubConnectionBuilder: Builder,
    HubConnectionState: { Connected: 'Connected' },
    LogLevel: { Warning: 3 },
  };
});
const ready: DesiredState = {
  view: 'Exercise',
  interactionEnabled: true,
  assignmentId: 'a',
  attemptId: 't',
  teacherPaused: false,
  recoveryBlocked: false,
  executionGeneration: '0',
  executionDirective: null,
};
const snapshot: ParticipantSnapshot = {
  protocolVersion: 1,
  serverInstanceId: 'server',
  sessionId: 'session',
  participantId: 'p',
  effectiveRevision: '2',
  connectionEpoch: '1',
  issuedAtUtc: '',
  desiredState: ready,
};
const command: CommandIssuedEnvelope = {
  ...snapshot,
  commandId: 'c',
  commandName: 'RunProgram',
  effectiveRevision: '3',
  desiredState: {
    ...ready,
    executionDirective: {
      directiveId: 'd',
      kind: 'Run',
      executionGeneration: '0',
      issuedAtUtc: '',
    },
  },
};
async function flush(): Promise<void> {
  for (let index = 0; index < 30; index++) await Promise.resolve();
}

describe('ParticipantRealtime', () => {
  let service: ParticipantRealtime;
  const api = { executeCommand: vi.fn() };
  beforeEach(() => {
    vi.useFakeTimers();
    api.executeCommand.mockReset();
    Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'visible' });
    TestBed.configureTestingModule({ providers: [{ provide: ApiService, useValue: api }] });
    service = TestBed.inject(ParticipantRealtime);
  });
  afterEach(async () => {
    await service.stop();
    vi.useRealTimers();
  });

  async function connect(): Promise<Transport> {
    await service.start();
    transport.current.emit('SessionSnapshot', snapshot);
    await flush();
    return transport.current;
  }
  it('aggiorna il profilo al rinnovo e scarta profili estranei o precedenti', async () => {
    await service.start();
    const connection = transport.current;
    connection.emit('SessionSnapshot', {
      ...snapshot,
      profile: { participantId: 'p', displayName: 'Nome iniziale', rowVersion: '9007199254740993' },
    });
    await flush();
    expect(service.profile()?.displayName).toBe('Nome iniziale');
    const renew = (name: string, version: string, participantId = 'p') => {
      vi.advanceTimersByTime(3000);
      const heartbeat = connection.calls.filter((call) => call.method === 'Heartbeat').at(-1)!;
      connection.emit('LeaseGranted', {
        nonce: heartbeat.data['nonce'],
        effectiveRevision: '2',
        connectionEpoch: '1',
        serverInstanceId: 'server',
        maxDurationSeconds: '10',
        validUntilUtc: '',
        profile: { participantId, displayName: name, rowVersion: version },
      });
    };
    renew('Luca Rossi', '9007199254740994');
    expect(service.profile()?.displayName).toBe('Luca Rossi');
    renew('Nome precedente', '9007199254740993');
    expect(service.profile()?.displayName).toBe('Luca Rossi');
    renew('Altro bambino', '9007199254740995', 'other');
    expect(service.profile()?.displayName).toBe('Luca Rossi');
    await service.stop();
    expect(service.profile()).toBeNull();
  });

  it('affida al publisher il ripiego dell’anteprima senza lasciare un avviso globale', async () => {
    const connection = await connect();
    const preview: WorkspacePreview = {
      protocolVersion: 1,
      attemptId: 't',
      connectionEpoch: '1',
      executionGeneration: '0',
      projectRevision: '1',
      workspaceHash: 'h',
      workspaceJson: '{}',
    };
    const invoke = vi.spyOn(
      connection as unknown as { invoke: (method: string, data: unknown) => Promise<void> },
      'invoke',
    );
    invoke.mockRejectedValueOnce(new Error('Anteprima rifiutata'));
    expect(await service.sendWorkspacePreview(preview)).toBe(false);
    expect(service.notice()).toBe('');
    expect(await service.sendWorkspacePreview(preview)).toBe(true);
    expect(service.notice()).toBe('');
  });
  it('attende il caricamento prima di SnapshotApplied e richiede la prima autorizzazione', async () => {
    let complete!: () => void;
    service.setStateApplier(
      () =>
        new Promise<void>((resolve) => {
          complete = resolve;
        }),
    );
    await service.start();
    const connection = transport.current;
    connection.emit('SessionSnapshot', snapshot);
    await flush();
    expect(connection.calls.some((call) => call.method === 'SnapshotApplied')).toBe(false);
    complete();
    await flush();
    expect(connection.calls.map((call) => call.method)).toContain('SnapshotApplied');
    const heartbeat = connection.calls.find((call) => call.method === 'Heartbeat')!;
    expect(heartbeat.data['nonce']).toBeTruthy();
    expect(heartbeat.data['serverInstanceId']).toBe('server');
    expect(service.status()).toBe('connected');
  });
  it('applica Run una volta e conferma entrambe le consegne duplicate', async () => {
    const connection = await connect();
    connection.emit('CommandIssued', command);
    await flush();
    const id = service.runtime.runId();
    connection.emit('CommandIssued', command);
    await flush();
    expect(service.runtime.runId()).toBe(id);
    const confirmations = connection.calls.filter((call) => call.method === 'CommandAcknowledged');
    expect(confirmations).toHaveLength(2);
    expect(confirmations.every((call) => call.data['outcome'] === 'Applied')).toBe(true);
  });
  it('conferma Failed quando il progetto non termina il caricamento', async () => {
    const connection = await connect();
    service.setStateApplier(async () => {
      throw new Error('load failed');
    });
    connection.emit('CommandIssued', command);
    await flush();
    expect(
      connection.calls.find((call) => call.method === 'CommandAcknowledged')?.data['outcome'],
    ).toBe('Failed');
    expect(service.runtime.status()).toBe('Ready');
    expect(service.authorized()).toBe(false);
  });
  it('rifiuta la lease precedente alla pausa e sospende una pagina nascosta', async () => {
    const connection = await connect();
    const heartbeat = connection.calls.find((call) => call.method === 'Heartbeat')!;
    const lease = {
      nonce: heartbeat.data['nonce'],
      serverInstanceId: 'server',
      connectionEpoch: '1',
      effectiveRevision: '2',
      maxDurationSeconds: '10',
      validUntilUtc: '',
    };
    connection.emit('LeaseGranted', lease);
    expect(service.authorized()).toBe(true);
    connection.emit('CommandIssued', {
      ...command,
      desiredState: { ...ready, teacherPaused: true },
    });
    await flush();
    connection.emit('LeaseGranted', lease);
    expect(service.authorized()).toBe(false);
    Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'hidden' });
    document.dispatchEvent(new Event('visibilitychange'));
    expect(service.authorized()).toBe(false);
  });
  it('segnala il recupero senza ripetere una direttiva sconosciuta', async () => {
    api.executeCommand.mockResolvedValue({
      ...snapshot,
      desiredState: { ...command.desiredState, recoveryBlocked: true, interactionEnabled: false },
    });
    await service.start();
    const connection = transport.current;
    connection.emit('SessionSnapshot', { ...snapshot, desiredState: command.desiredState });
    await flush();
    expect(api.executeCommand).toHaveBeenCalledWith('ReportRecoveryBlocked', {
      connectionEpoch: '1',
      attemptId: 't',
      effectiveRevision: '2',
    });
    expect(service.runtime.status()).toBe('Ready');
    expect(service.desiredState()?.recoveryBlocked).toBe(true);
  });
  it('non avvia i timer se la pagina termina durante il caricamento', async () => {
    let complete!: () => void;
    service.setStateApplier(
      () =>
        new Promise<void>((resolve) => {
          complete = resolve;
        }),
    );
    await service.start();
    const connection = transport.current;
    connection.emit('SessionSnapshot', snapshot);
    await flush();
    await service.stop();
    complete();
    await flush();
    expect(service.status()).toBe('idle');
    expect(connection.calls.some((call) => call.method === 'Heartbeat')).toBe(false);
  });
  it('pubblica osservazioni limitate senza mutazioni HTTP del progetto', async () => {
    const connection = await connect();
    const heartbeat = connection.calls.find((call) => call.method === 'Heartbeat')!;
    connection.emit('LeaseGranted', {
      nonce: heartbeat.data['nonce'],
      serverInstanceId: 'server',
      connectionEpoch: '1',
      effectiveRevision: '2',
      maxDurationSeconds: '10',
      validUntilUtc: '',
    });
    service.setWorkspaceHash('hash');
    await vi.advanceTimersByTimeAsync(1200);
    await flush();
    const observations = connection.calls.filter((call) => call.method === 'ActivityStateUpdated');
    expect(observations.length).toBeGreaterThan(0);
    expect(observations.length).toBeLessThanOrEqual(3);
    expect(observations[0].data['workspaceHash']).toBe('hash');
    expect(api.executeCommand).not.toHaveBeenCalled();
    Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'hidden' });
    document.dispatchEvent(new Event('visibilitychange'));
    const count = observations.length;
    await vi.advanceTimersByTimeAsync(1200);
    expect(connection.calls.filter((call) => call.method === 'ActivityStateUpdated')).toHaveLength(
      count,
    );
  });
  it('ignora revoche e comandi del collegamento precedente', async () => {
    const previous = await connect();
    await service.stop();
    await connect();
    previous.emit('ParticipationRevoked', {});
    previous.emit('ConnectionSuperseded', {});
    previous.emit('CommandIssued', command);
    await flush();
    expect(service.status()).toBe('connected');
    expect(service.desiredState()).toEqual(ready);
    expect(service.lastCommand()).toBeNull();
  });
  it('dichiara AppliedWithRecoveryRequired quando il motore non conferma la pausa', async () => {
    const connection = await connect();
    // Prima si avvia la corsa: solo una corsa in esecuzione richiede la conferma del worker.
    connection.emit('CommandIssued', command);
    await flush();
    // Poi arriva la pausa del docente, che il worker non conferma entro la soglia.
    connection.emit('CommandIssued', {
      ...command,
      commandId: 'pause',
      commandName: 'Pause',
      effectiveRevision: '4',
      desiredState: { ...ready, teacherPaused: true, interactionEnabled: false },
    });
    await flush();
    await vi.advanceTimersByTimeAsync(600);
    await flush();

    const acknowledgements = connection.calls.filter(
      (call) => call.method === 'CommandAcknowledged',
    );
    expect(acknowledgements.at(-1)?.data['outcome']).toBe('AppliedWithRecoveryRequired');
  });

  it('conferma Goodbye e termina i battiti senza dichiarare una sostituzione', async () => {
    const connection = await connect();
    const count = connection.calls.filter((call) => call.method === 'Heartbeat').length;
    connection.emit('CommandIssued', {
      ...command,
      commandName: 'EndSession',
      desiredState: {
        ...ready,
        view: 'Goodbye',
        interactionEnabled: false,
        teacherPaused: true,
      },
    });
    await flush();
    connection.emit('ConnectionSuperseded', {});
    await vi.advanceTimersByTimeAsync(6000);
    expect(service.status()).toBe('ended');
    expect(service.desiredState()?.view).toBe('Goodbye');
    expect(
      connection.calls.find((call) => call.method === 'CommandAcknowledged')?.data['outcome'],
    ).toBe('Applied');
    expect(connection.calls.filter((call) => call.method === 'Heartbeat')).toHaveLength(count);
    expect(service.runtime.status()).toBe('Stopped');
  });

  it('raccoglie i messaggi della chat annunciati dall’hub senza toccare il lavoro', async () => {
    const connection = await connect();
    const revision = service.desiredState()?.executionGeneration;

    connection.emit('ParticipantMessageReceived', {
      messageId: 'm1',
      sessionId: 'session',
      participantId: 'p',
      author: 'Teacher',
      text: 'Bravo, continua così',
      writtenAtUtc: '2026-10-13T09:00:00Z',
    });
    await flush();

    // Il messaggio non è un comando: lo stato desiderato e il lavoro restano quelli di prima.
    expect(service.chatIncoming()).toHaveLength(1);
    expect(service.chatIncoming()[0]?.text).toBe('Bravo, continua così');
    expect(service.desiredState()?.executionGeneration).toBe(revision);
    expect(connection.calls.some((call) => call.method === 'CommandAcknowledged')).toBe(false);

    // Il thread muore con il collegamento: spegnere la pagina non lascia messaggi di una lezione.
    await service.stop();
    expect(service.chatIncoming()).toEqual([]);
  });
});
