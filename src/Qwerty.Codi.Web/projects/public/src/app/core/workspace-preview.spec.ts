import { TestBed } from '@angular/core/testing';
import { WorkspacePreview } from '@codi/api';
import { AttemptTransport } from './attempt-transport';
import { ParticipantRealtime } from './participant-realtime';
import {
  HTTP_PREVIEW_LIMIT_BYTES,
  PREVIEW_THROTTLE_MS,
  SIGNALR_PREVIEW_LIMIT_BYTES,
  WORKSPACE_DIGEST,
  WorkspacePreviewPublisher,
} from './workspace-preview';

describe('WorkspacePreviewPublisher', () => {
  let publisher: WorkspacePreviewPublisher;
  const realtime = { previewContext: vi.fn(), sendWorkspacePreview: vi.fn() };
  const transport = { preview: vi.fn() };
  const context = { connectionEpoch: '2', executionGeneration: '1' };
  const receipt = {
    participantId: 'p',
    sessionId: 's',
    attemptId: 't',
    projectRevision: '1',
    workspaceHash: 'h',
    receivedAtUtc: '2026-01-01T00:00:00Z',
  };

  beforeEach(() => {
    vi.useFakeTimers();
    realtime.previewContext.mockReset().mockReturnValue(context);
    realtime.sendWorkspacePreview.mockReset().mockResolvedValue(true);
    transport.preview.mockReset().mockResolvedValue(receipt);
    TestBed.configureTestingModule({
      providers: [
        { provide: ParticipantRealtime, useValue: realtime },
        { provide: AttemptTransport, useValue: transport },
        {
          provide: WORKSPACE_DIGEST,
          useValue: (text: string) => Promise.resolve(`h-${String(text.length)}`),
        },
      ],
    });
    publisher = TestBed.inject(WorkspacePreviewPublisher);
  });

  afterEach(() => {
    publisher.dispose();
    vi.useRealTimers();
  });

  /** Attende il passo di pubblicazione e le promesse che ne dipendono. */
  async function settle(): Promise<void> {
    await vi.advanceTimersByTimeAsync(PREVIEW_THROTTLE_MS);
    for (let index = 0; index < 20; index++) await Promise.resolve();
  }

  it('aggrega le modifiche ravvicinate in un solo invio', async () => {
    publisher.attach('t', '1');
    publisher.setOperative(true);
    publisher.change('{"a":1}');
    publisher.change('{"a":2}');
    publisher.change('{"a":3}');

    await settle();

    expect(realtime.sendWorkspacePreview).toHaveBeenCalledTimes(1);
    expect((realtime.sendWorkspacePreview.mock.calls[0][0] as WorkspacePreview).workspaceJson).toBe(
      '{"a":3}',
    );
    expect(transport.preview).not.toHaveBeenCalled();
  });

  it('avanza la revisione dell’anteprima e ripiega su HTTP oltre il limite del messaggio', async () => {
    publisher.attach('t', '4');
    publisher.setOperative(true);
    publisher.publishNow('{"a":1}');

    await settle();

    const first = realtime.sendWorkspacePreview.mock.calls[0][0] as WorkspacePreview;
    expect(first.projectRevision).toBe('5');
    expect(first.connectionEpoch).toBe('2');
    expect(first.executionGeneration).toBe('1');

    const large = JSON.stringify({ pad: 'x'.repeat(SIGNALR_PREVIEW_LIMIT_BYTES + 100) });
    publisher.change(large);
    await settle();

    expect(realtime.sendWorkspacePreview).toHaveBeenCalledTimes(1);
    expect(transport.preview).toHaveBeenCalledTimes(1);
    const [attemptId, body] = transport.preview.mock.calls[0] as [string, WorkspacePreview];
    expect(attemptId).toBe('t');
    expect(body.workspaceJson).toBe(large);
    expect(body.projectRevision).toBe('6');
  });

  it('non invia un progetto più grande del limite HTTP', async () => {
    publisher.attach('t', '1');
    publisher.setOperative(true);
    publisher.publishNow(JSON.stringify({ pad: 'x'.repeat(HTTP_PREVIEW_LIMIT_BYTES + 100) }));

    await settle();

    expect(realtime.sendWorkspacePreview).not.toHaveBeenCalled();
    expect(transport.preview).not.toHaveBeenCalled();
  });

  it('non pubblica senza un lavoro in corso o senza operatività', async () => {
    publisher.attach('t', '1');
    publisher.change('{"a":1}');
    await settle();
    expect(realtime.sendWorkspacePreview).not.toHaveBeenCalled();

    realtime.previewContext.mockReturnValue(null);
    publisher.setOperative(true);
    publisher.publishNow('{"a":2}');
    await settle();
    expect(realtime.sendWorkspacePreview).not.toHaveBeenCalled();
  });

  it('rimanda lo stesso progetto quando il salvataggio è confermato', async () => {
    publisher.attach('t', '1');
    publisher.setOperative(true);
    publisher.publishNow('{"a":1}');
    await settle();
    expect(realtime.sendWorkspacePreview).toHaveBeenCalledTimes(1);

    publisher.publishNow('{"a":1}', true);
    await settle();

    expect(realtime.sendWorkspacePreview).toHaveBeenCalledTimes(2);
  });
  it('sostituisce il rifiuto con la conferma quando il progetto viene corretto', async () => {
    publisher.attach('t', '1');
    publisher.setOperative(true);
    realtime.sendWorkspacePreview.mockResolvedValue(false);
    transport.preview.mockRejectedValueOnce(new Error('Progetto non valido'));
    publisher.change('{"invalid":true}');
    await settle();
    expect(publisher.status()).toContain('Anteprima non inviata');
    publisher.change('{"valid":true}');
    await settle();
    expect(publisher.status()).toBe('Anteprima inviata dal collegamento HTTP.');
  });
});
