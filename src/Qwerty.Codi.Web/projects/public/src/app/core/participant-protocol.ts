import type { CommandIssuedEnvelope, ExecutionLease, ParticipantSnapshot } from '@codi/api';

export type CommandOutcome = 'Applied' | 'AppliedWithRecoveryRequired' | 'RejectedStale' | 'Failed';

/** Ordinamento e autorizzazioni. Nessun timer o trasporto in questo stato verificabile. */
export class ParticipantProtocolState {
  snapshot: ParticipantSnapshot | null = null;
  leaseUntil = 0;
  private readonly acknowledgements = new Map<string, CommandOutcome>();
  private readonly heartbeats = new Map<
    string,
    { sent: number; revision: string; epoch: string }
  >();
  private readonly retiredServers = new Set<string>();

  acceptSnapshot(incoming: ParticipantSnapshot): boolean {
    if (
      incoming.protocolVersion !== 1 ||
      !counter(incoming.effectiveRevision) ||
      !counter(incoming.connectionEpoch)
    )
      return false;
    const current = this.snapshot;
    if (current !== null) {
      if (
        incoming.sessionId !== current.sessionId ||
        incoming.participantId !== current.participantId
      )
        return false;
      if (this.retiredServers.has(incoming.serverInstanceId)) return false;
      if (
        incoming.serverInstanceId === current.serverInstanceId &&
        (older(incoming.connectionEpoch, current.connectionEpoch) ||
          older(incoming.effectiveRevision, current.effectiveRevision))
      )
        return false;
      if (incoming.serverInstanceId !== current.serverInstanceId)
        this.retiredServers.add(current.serverInstanceId);
    }
    this.invalidateLease();
    this.snapshot = incoming;
    return true;
  }

  commandOutcome(command: CommandIssuedEnvelope): CommandOutcome | 'apply' {
    const current = this.snapshot;
    if (command.protocolVersion !== 1 || !counter(command.effectiveRevision)) return 'Failed';
    if (
      current === null ||
      command.sessionId !== current.sessionId ||
      command.participantId !== current.participantId ||
      command.serverInstanceId !== current.serverInstanceId ||
      command.connectionEpoch !== current.connectionEpoch ||
      older(command.effectiveRevision, current.effectiveRevision)
    )
      return 'RejectedStale';
    const known = this.acknowledgements.get(command.commandId);
    if (known !== undefined) return known;
    if (command.effectiveRevision === current.effectiveRevision) return 'Applied';
    this.invalidateLease();
    return 'apply';
  }

  applied(command: CommandIssuedEnvelope, outcome: CommandOutcome): void {
    if (
      this.snapshot !== null &&
      (outcome === 'Applied' || outcome === 'AppliedWithRecoveryRequired')
    ) {
      this.snapshot = {
        ...this.snapshot,
        effectiveRevision: command.effectiveRevision,
        desiredState: command.desiredState,
      };
    }
    this.acknowledgements.set(command.commandId, outcome);
    if (this.acknowledgements.size > 128)
      this.acknowledgements.delete(this.acknowledgements.keys().next().value!);
  }

  heartbeat(nonce: string, sent: number): void {
    if (this.snapshot === null) return;
    this.heartbeats.set(nonce, {
      sent,
      revision: this.snapshot.effectiveRevision,
      epoch: this.snapshot.connectionEpoch,
    });
    if (this.heartbeats.size > 8) this.heartbeats.delete(this.heartbeats.keys().next().value!);
  }

  acceptLease(lease: ExecutionLease, now: number): boolean {
    const sent = this.heartbeats.get(lease.nonce);
    this.heartbeats.delete(lease.nonce);
    const current = this.snapshot;
    const seconds = Number(lease.maxDurationSeconds);
    if (
      sent === undefined ||
      current === null ||
      lease.serverInstanceId !== current.serverInstanceId ||
      lease.connectionEpoch !== current.connectionEpoch ||
      lease.effectiveRevision !== current.effectiveRevision ||
      sent.epoch !== current.connectionEpoch ||
      sent.revision !== current.effectiveRevision ||
      !Number.isFinite(seconds) ||
      seconds <= 0 ||
      seconds > 10 ||
      now >= sent.sent + seconds * 1000
    )
      return false;
    this.leaseUntil = sent.sent + seconds * 1000;
    return true;
  }

  invalidateLease(): void {
    this.leaseUntil = 0;
    this.heartbeats.clear();
  }
}

function counter(value: string): boolean {
  return /^\d+$/.test(value);
}
function older(left: string, right: string): boolean {
  return BigInt(left) < BigInt(right);
}
