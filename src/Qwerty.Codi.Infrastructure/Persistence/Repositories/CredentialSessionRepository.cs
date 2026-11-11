using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Credentials;
using Qwerty.Codi.Infrastructure.Persistence.UnitOfWork;
using RepoDb;

namespace Qwerty.Codi.Infrastructure.Persistence.Repositories;

/// <summary>La revoca condizionata integra il CRUD comune, senza SQL duplicato.</summary>
internal sealed class CredentialSessionRepository(DbContext context)
    : BaseRepository<CredentialSessionEntity, string>(context), ICredentialSessionRepository
{
    public async Task<CredentialSession?> TryRevokeAsync(
        CredentialSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.RevokedAtUtc is null)
            throw new ArgumentException("La credenziale non risulta revocata: la revoca appartiene al dominio.", nameof(session));

        var row = CredentialSessionEntity.FromDomain(session);
        var fields = Field.Parse<CredentialSessionEntity>(entity => new
        {
            entity.RevokedAtUtcMs,
            entity.UpdatedAtUtcMs,
            entity.RowVersion,
        });
        return await UpdateFieldsAsync(row, fields, entity => entity.RevokedAtUtcMs == null, cancellationToken)
            .ConfigureAwait(false) ? session.WithRowVersion(row.RowVersion) : null;
    }
}
