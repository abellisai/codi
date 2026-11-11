using Mediator;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Admin;
using Qwerty.Codi.Domain.Credentials;

namespace Qwerty.Codi.Application.Admin;

/// <summary>
/// Verifica le credenziali amministrative e, se corrispondono, emette una credenziale
/// persistita (§9.1). Il conteggio dei tentativi falliti e il blocco temporaneo vivono
/// sull'account: questo caso d'uso li fa scattare e li persiste.
/// </summary>
public sealed class LoginAdminHandler(
    IUnitOfWorkContext unitOfWorkContext,
    IAdminPasswordHasher passwordHasher,
    TimeProvider timeProvider)
    : IRequestHandler<LoginAdminCommand, AdminSignedInDto>
{
    /// <summary>
    /// Impronta di riferimento, calcolata una volta sola: verificare contro un'impronta qualsiasi
    /// costa quanto verificare contro quella vera, così il tempo di risposta non rivela se il
    /// nome esiste. Non protegge da un attacco determinato, toglie la risposta più facile.
    /// </summary>
    private readonly Lazy<string> _referenceHash = new(() => passwordHasher.Hash("impronta-di-riferimento"));

    public async ValueTask<AdminSignedInDto> Handle(
        LoginAdminCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!AdminUsername.IsValid(request.Username))
        {
            // Un nome inutilizzabile non può corrispondere a nessun account: stessa risposta e
            // stesso costo di una password sbagliata.
            passwordHasher.Verify(_referenceHash.Value, request.Password ?? string.Empty);

            throw CodiRequestException.CredentialsRejected();
        }

        var normalizedUsername = AdminUsername.Normalize(request.Username);

        // Ogni conflitto richiede una nuova lettura e la rivalutazione del blocco. Si ritenta
        // soltanto l'UPDATE condizionato, che non ha scritto nulla; la credenziale viene emessa
        // una sola volta dopo aver persistito con successo lo stato dell'account (§7.6).
        for (var attempt = 0; attempt < 16; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AdminUser? user;
            await using (var read = await unitOfWorkContext.CreateAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                var rows = await read.Repo<IAdminUserRepository>().QueryAsync(
                    entity => entity.NormalizedUsername == normalizedUsername, cancellationToken).ConfigureAwait(false);
                user = rows.SingleOrDefault()?.ToDomain();
            }

            if (user is null || !user.IsActive)
            {
                passwordHasher.Verify(_referenceHash.Value, request.Password ?? string.Empty);
                throw CodiRequestException.CredentialsRejected();
            }

            var now = timeProvider.GetUtcNow();
            if (user.IsLockedAt(now))
            {
                throw CodiRequestException.AccountLocked(user.LockedUntilUtc!.Value - now);
            }

            var passwordMatches = passwordHasher.Verify(user.PasswordHash, request.Password ?? string.Empty);
            var locked = false;
            if (passwordMatches)
            {
                user.RegisterSuccessfulAccess(now);
            }
            else
            {
                locked = user.RegisterFailedAttempt(now);
            }

            // Hashing fuori dalla transazione; UPDATE e nuova credenziale condividono poi
            // connessione e writer. Un fallimento nell'inserimento annulla anche l'UPDATE.
            await using var write = await unitOfWorkContext.CreateAsync(
                useTransaction: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (await write.Repo<IAdminUserRepository>().TryUpdateAsync(user, cancellationToken).ConfigureAwait(false) is null)
            {
                continue;
            }

            if (!passwordMatches)
            {
                // Il rifiuto è un esito previsto: il tentativo deve restare persistito.
                await write.CommitAsync(cancellationToken).ConfigureAwait(false);
                throw locked
                    ? CodiRequestException.AccountLocked(user.LockedUntilUtc!.Value - now)
                    : CodiRequestException.CredentialsRejected();
            }

            var session = CredentialSession.IssueForAdmin(user.Id, now, AdminCredentialPolicy.SessionLifetime);
            await write.Repo<ICredentialSessionRepository>().InsertAsync(
                CredentialSessionEntity.FromDomain(session), cancellationToken).ConfigureAwait(false);
            await write.CommitAsync(cancellationToken).ConfigureAwait(false);

            return new AdminSignedInDto(
                session.Id, user.Id, user.Username, session.ExpiresAtUtc, session.SecurityStamp);
        }

        throw new CodiRequestException(
            CodiErrorCodes.RowVersionMismatch,
            "L'account è stato modificato contemporaneamente: ripetere l'accesso.");
    }
}

/// <summary>
/// Revoca la credenziale dell'accesso corrente. Restituisce falso se non c'era nulla da revocare:
/// ripetere l'uscita, uscire da una credenziale già scaduta o presentare l'identificativo di un
/// altro soggetto producono lo stesso esito, perché il chiamante non deve poter dedurre quale
/// dei tre casi si è verificato.
/// </summary>
public sealed class LogoutAdminHandler(
    IUnitOfWorkContext unitOfWorkContext,
    TimeProvider timeProvider)
    : IRequestHandler<LogoutAdminCommand, bool>
{
    public async ValueTask<bool> Handle(LogoutAdminCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.CredentialSessionId is not { } sessionId ||
            request.Context.AdminUserId is not { } adminUserId)
        {
            throw CodiRequestException.AuthenticationRequired(LogoutAdminCommand.RequestName);
        }

        await using var work = await unitOfWorkContext.CreateAsync(
            useTransaction: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        var sessions = work.Repo<ICredentialSessionRepository>();
        var row = await sessions.GetAsync(sessionId.ToString("D"), cancellationToken).ConfigureAwait(false);
        var session = row?.ToDomain();

        if (session is null ||
            session.PrincipalKind != CredentialPrincipalKind.Admin ||
            session.AdminUserId != adminUserId ||
            !session.Revoke(timeProvider.GetUtcNow()))
        {
            return false;
        }

        var revoked = await sessions.TryRevokeAsync(session, cancellationToken).ConfigureAwait(false);
        await work.CommitAsync(cancellationToken).ConfigureAwait(false);
        return revoked is not null;
    }
}

/// <summary>
/// Crea il primo account amministrativo di un'installazione (§9.1). Le condizioni sono verificate
/// nell'ordine in cui possono fallire, e ogni esito ha il suo codice: nome inutilizzabile,
/// password assente, amministratore già attivo, nome già occupato.
/// </summary>
public sealed class BootstrapAdminAccountHandler(
    IUnitOfWorkContext unitOfWorkContext,
    IAdminPasswordHasher passwordHasher,
    TimeProvider timeProvider)
    : IRequestHandler<BootstrapAdminAccountCommand, AdminAccountCreatedDto>
{
    public async ValueTask<AdminAccountCreatedDto> Handle(
        BootstrapAdminAccountCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!AdminUsername.IsValid(request.Username))
        {
            throw new CodiRequestException(
                CodiErrorCodes.InvalidName,
                "Il nome utente contiene caratteri non ammessi o è vuoto.");
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed,
                "La password del primo amministratore è obbligatoria.");
        }

        // Preparazione prima di acquisire il writer; controllo e inserimento sono atomici
        // anche quando due bootstrap con nomi diversi arrivano contemporaneamente.
        var user = AdminUser.Create(
            request.Username,
            passwordHasher.Hash(request.Password),
            timeProvider.GetUtcNow());
        await using var work = await unitOfWorkContext.CreateAsync(
            useTransaction: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        var users = work.Repo<IAdminUserRepository>();
        if (await users.ExistsAsync(entity => entity.IsActiveFlag == 1, cancellationToken).ConfigureAwait(false))
        {
            throw new CodiRequestException(
                CodiErrorCodes.BootstrapNotAllowed,
                "Esiste già un amministratore attivo: il bootstrap è ammesso solo su un'installazione senza account.");
        }

        if (!await users.TryInsertAsync(user, cancellationToken).ConfigureAwait(false))
        {
            // L'account non è attivo ma il nome è occupato: l'unicità della tabella ha l'ultima
            // parola, e il caso d'uso non prova a indovinare quale sia il conflitto.
            throw new CodiRequestException(
                CodiErrorCodes.AdminAccountAlreadyExists,
                "Esiste già un account con questo nome.");
        }

        await work.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new AdminAccountCreatedDto(user.Id, user.Username);
    }
}
