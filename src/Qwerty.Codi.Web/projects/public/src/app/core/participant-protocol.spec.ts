import type {
  CommandIssuedEnvelope,
  DesiredState,
  ExecutionLease,
  ParticipantSnapshot,
} from '@codi/api';
import { ParticipantProtocolState } from './participant-protocol';

const desired: DesiredState = {
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
  effectiveRevision: '9007199254740993',
  connectionEpoch: '1',
  issuedAtUtc: '',
  desiredState: desired,
};
const command: CommandIssuedEnvelope = {
  ...snapshot,
  commandId: 'c',
  commandName: 'Pause',
  effectiveRevision: '9007199254740994',
  desiredState: { ...desired, teacherPaused: true },
};
const lease: ExecutionLease = {
  nonce: 'n',
  serverInstanceId: 'server',
  effectiveRevision: snapshot.effectiveRevision,
  connectionEpoch: '1',
  maxDurationSeconds: '10',
  validUntilUtc: '2099-01-01',
};

describe('ParticipantProtocolState', () => {
  let state: ParticipantProtocolState;
  beforeEach(() => {
    state = new ParticipantProtocolState();
    state.acceptSnapshot(snapshot);
  });

  it('ordina le revisioni oltre 53 bit senza arrotondamento', () => {
    expect(state.acceptSnapshot({ ...snapshot, effectiveRevision: '9007199254740992' })).toBe(
      false,
    );
    expect(state.commandOutcome(command)).toBe('apply');
    state.applied(command, 'Applied');
    expect(state.snapshot?.effectiveRevision).toBe(command.effectiveRevision);
    expect(
      state.commandOutcome({
        ...command,
        commandId: 'old',
        effectiveRevision: snapshot.effectiveRevision,
      }),
    ).toBe('RejectedStale');
  });
  it('conferma il duplicato senza rieseguire la direttiva', () => {
    state.applied(command, 'Applied');
    expect(state.commandOutcome(command)).toBe('Applied');
    expect(state.snapshot?.desiredState).toEqual(command.desiredState);
  });
  it('conserva l’esito negativo del duplicato', () => {
    state.applied(command, 'Failed');
    expect(state.commandOutcome(command)).toBe('Failed');
    expect(state.snapshot?.effectiveRevision).toBe(snapshot.effectiveRevision);
  });
  it('rifiuta identità, epoca e istanza precedenti', () => {
    expect(state.commandOutcome({ ...command, participantId: 'other' })).toBe('RejectedStale');
    expect(state.commandOutcome({ ...command, connectionEpoch: '0' })).toBe('RejectedStale');
    expect(state.commandOutcome({ ...command, serverInstanceId: 'old' })).toBe('RejectedStale');
    expect(state.acceptSnapshot({ ...snapshot, sessionId: 'other' })).toBe(false);
  });
  it('rifiuta lo snapshot del server precedente dopo un riavvio', () => {
    expect(state.acceptSnapshot({ ...snapshot, serverInstanceId: 'new' })).toBe(true);
    expect(state.acceptSnapshot(snapshot)).toBe(false);
  });
  it('rifiuta lo snapshot completato dopo una nuova epoca', () => {
    state.acceptSnapshot({ ...snapshot, connectionEpoch: '2' });
    expect(state.acceptSnapshot(snapshot)).toBe(false);
  });
  it('calcola la scadenza dal tempo monotono di invio', () => {
    state.heartbeat('n', 1000);
    expect(state.acceptLease(lease, 5000)).toBe(true);
    expect(state.leaseUntil).toBe(11000);
    expect(state.acceptLease(lease, 6000)).toBe(false);
  });
  it('rifiuta una lease scaduta e una risposta senza richiesta', () => {
    state.heartbeat('n', 1000);
    expect(state.acceptLease(lease, 11000)).toBe(false);
    expect(state.acceptLease({ ...lease, nonce: 'unknown' }, 1000)).toBe(false);
  });
  it('rifiuta la lease ricevuta dopo una pausa', () => {
    state.heartbeat('n', 1000);
    state.commandOutcome(command);
    expect(state.acceptLease(lease, 2000)).toBe(false);
    expect(state.leaseUntil).toBe(0);
  });
  it('rifiuta contatori e durate incompatibili', () => {
    expect(state.acceptSnapshot({ ...snapshot, effectiveRevision: 'NaN' })).toBe(false);
    expect(state.commandOutcome({ ...command, protocolVersion: 2 })).toBe('Failed');
    state.heartbeat('n', 1000);
    expect(state.acceptLease({ ...lease, maxDurationSeconds: '11' }, 1001)).toBe(false);
  });
});
