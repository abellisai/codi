using Qwerty.Codi.Application.Abstractions;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>Scaduta la finestra terminale, revoca le credenziali della lezione.</summary>
public sealed class FinalizationCleanup(IUnitOfWorkContext factory, ISessionCoordinator coordinator, TimeProvider time)
{
    public async Task ExpireAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var threshold = now.Subtract(AttemptFinalization.Window).ToUnixTimeMilliseconds();
        string[] sessions;
        await using (var read = await factory.CreateAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
            sessions = (await read.Repo<ILessonSessionRepository>().QueryAsync(
                row => row.Status == "Ended" && row.EndedAtUtcMs <= threshold, cancellationToken).ConfigureAwait(false))
                .Select(row => row.Id).ToArray();
        foreach (var id in sessions)
        {
            await using var coordination = await coordinator.AcquireAsync(Guid.Parse(id), cancellationToken).ConfigureAwait(false);
            await using var work = await factory.CreateAsync(useTransaction: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            var participants = await work.Repo<ISessionParticipantRepository>().QueryAsync(row => row.SessionId == id, cancellationToken).ConfigureAwait(false);
            var credentials = work.Repo<ICredentialSessionRepository>();
            foreach (var participant in participants)
                foreach (var credential in await credentials.QueryAsync(row => row.ParticipantId == participant.Id &&
                    row.RevokedAtUtcMs == null, cancellationToken).ConfigureAwait(false))
                {
                    var domain = credential.ToDomain();
                    domain.Revoke(now);
                    await credentials.TryRevokeAsync(domain, cancellationToken).ConfigureAwait(false);
                }
            await work.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
