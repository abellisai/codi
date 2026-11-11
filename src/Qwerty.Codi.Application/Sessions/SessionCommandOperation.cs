using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Persistence;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>Coordina la sessione e registra l'esito nella transazione del comando.</summary>
internal sealed class SessionCommandOperation : IUnitOfWork
{
    private readonly IUnitOfWork _work;
    private readonly IAsyncDisposable _coordination;
    private readonly IdempotencyRecordEntity? _record;
    private readonly string? _replay;

    private SessionCommandOperation(IUnitOfWork work, IAsyncDisposable coordination,
        IdempotencyRecordEntity? record, string? replay)
    {
        _work = work;
        _coordination = coordination;
        _record = record;
        _replay = replay;
    }

    public static async Task<SessionCommandOperation> OpenAsync(
        IUnitOfWorkContext factory, ISessionCoordinator coordinator, TimeProvider time,
        Guid sessionId, CodiRequestContext context, string name, object request, CancellationToken cancellationToken)
    {
        var coordination = await coordinator.AcquireAsync(sessionId, cancellationToken).ConfigureAwait(false);
        IUnitOfWork? work = null;
        try
        {
            work = await factory.CreateAsync(useTransaction: true, cancellationToken: cancellationToken).ConfigureAwait(false);
            // La proprietà si verifica anche prima di restituire un esito registrato.
            await SessionGridGuard.LoadOwnedSessionAsync(work, sessionId,
                context.AdminUserId ?? throw CodiRequestException.AuthenticationRequired(name), cancellationToken).ConfigureAwait(false);
            if (context.IdempotencyKey is not { } key)
            {
                return new(work, coordination, null, null);
            }

            if (string.IsNullOrWhiteSpace(key) || key.Length > 128)
            {
                throw CodiRequestException.InvalidPayload(name);
            }

            var scope = $"regia:{context.AdminUserId:D}:{sessionId:D}";
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                name + ":" + JsonSerializer.Serialize(request, request.GetType(), CodiJson.Payload))));
            var records = work.Repo<IIdempotencyRecordRepository>();
            var stored = await records.FindAsync(scope, key, cancellationToken).ConfigureAwait(false);
            if (stored is not null && stored.ExpiresAtUtcMs > time.GetUtcNow().ToUnixTimeMilliseconds())
            {
                if (stored.RequestHash != hash)
                {
                    throw CodiRequestException.IdempotencyKeyConflict();
                }

                return new(work, coordination, null, stored.ResponseJson);
            }

            return new(work, coordination, new IdempotencyRecordEntity
            {
                ScopeKey = scope,
                RequestKey = key,
                RequestHash = hash,
                ExpiresAtUtcMs = time.GetUtcNow().AddHours(24).ToUnixTimeMilliseconds(),
            }, null);
        }
        catch
        {
            if (work is not null) await work.DisposeAsync().ConfigureAwait(false);
            await coordination.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public bool TryReplay<T>(out T response)
    {
        response = _replay is null ? default! : JsonSerializer.Deserialize<T>(_replay, CodiJson.Payload)!;
        return _replay is not null;
    }

    public static async Task<T> CompleteAsync<T>(IUnitOfWork work, T response, CancellationToken cancellationToken)
    {
        if (work is SessionCommandOperation { _record: { } record })
        {
            record.ResponseJson = JsonSerializer.Serialize(response, CodiJson.Payload);
            record.ResponseCode = response is ICodiAcceptedOutcome ? 202 : 200;
            await work.Repo<IIdempotencyRecordRepository>().ReplaceAsync(record, cancellationToken).ConfigureAwait(false);
        }

        await work.CommitAsync(cancellationToken).ConfigureAwait(false);
        return response;
    }

    public TRepository Repo<TRepository>() where TRepository : class, IRepository => _work.Repo<TRepository>();
    public Task CommitAsync(CancellationToken cancellationToken = default) => _work.CommitAsync(cancellationToken);
    public Task RollbackAsync(CancellationToken cancellationToken = default) => _work.RollbackAsync(cancellationToken);
    public async ValueTask DisposeAsync()
    {
        try { await _work.DisposeAsync().ConfigureAwait(false); }
        finally { await _coordination.DisposeAsync().ConfigureAwait(false); }
    }
}
