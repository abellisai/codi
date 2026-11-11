using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Persistence;

namespace Qwerty.Codi.Application.Sessions;

public static class AttemptFinalization
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(30);

    public static async Task IssueAsync(IUnitOfWork work, ParticipantStateEntity state, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (state.AttemptId is null) return;
        var attempt = await work.Repo<IExerciseAttemptRepository>().GetAsync(state.AttemptId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Il tentativo non esiste.");
        var participant = await work.Repo<ISessionParticipantRepository>().GetAsync(state.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Il destinatario non esiste.");
        var nowMs = now.ToUnixTimeMilliseconds();
        var credentials = await work.Repo<ICredentialSessionRepository>().QueryAsync(
            row => row.ParticipantId == state.Id && row.RevokedAtUtcMs == null && row.ExpiresAtUtcMs > nowMs,
            cancellationToken).ConfigureAwait(false);
        var store = work.Repo<IAttemptFinalizationGrantRepository>();
        foreach (var credential in credentials)
        {
            if (await store.ExistsAsync(row => row.AttemptId == state.AttemptId &&
                row.CredentialSessionId == credential.Id && row.ConnectionEpoch == participant.ConnectionEpoch,
                cancellationToken).ConfigureAwait(false)) continue;
            await store.InsertAsync(new AttemptFinalizationGrantEntity
            {
                Id = Guid.NewGuid().ToString("D"),
                AttemptId = state.AttemptId,
                CredentialSessionId = credential.Id,
                ConnectionEpoch = participant.ConnectionEpoch,
                ExecutionGeneration = state.ExecutionGeneration,
                BaseRowVersion = attempt.RowVersion,
                ExpiresAtUtcMs = Math.Min(credential.ExpiresAtUtcMs, now.Add(Window).ToUnixTimeMilliseconds()),
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    public static async Task<IReadOnlyList<AttemptFinalizationGrantDto>> ReadAsync(IUnitOfWork work,
        string credentialId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var instant = now.ToUnixTimeMilliseconds();
        var rows = await work.Repo<IAttemptFinalizationGrantRepository>().QueryAsync(
            row => row.CredentialSessionId == credentialId && row.ExpiresAtUtcMs > instant,
            cancellationToken).ConfigureAwait(false);
        var result = new List<AttemptFinalizationGrantDto>();
        foreach (var row in rows)
        {
            var attempt = await work.Repo<IExerciseAttemptRepository>().GetAsync(row.AttemptId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Il tentativo conclusivo non esiste.");
            result.Add(new(Guid.Parse(row.Id), Guid.Parse(row.AttemptId), row.ConnectionEpoch,
                row.ExecutionGeneration, DateTimeOffset.FromUnixTimeMilliseconds(row.ExpiresAtUtcMs),
                Guid.Parse(attempt.SessionId), Guid.Parse(attempt.ParticipantId)));
        }
        return result;
    }
}
