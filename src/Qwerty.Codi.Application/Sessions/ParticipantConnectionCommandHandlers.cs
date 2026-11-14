using Qwerty.Codi.Application.Dto;
using System.Text.Json;
using Mediator;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Sessions;

namespace Qwerty.Codi.Application.Sessions;

public sealed class ReportRecoveryBlockedHandler(IUnitOfWorkContext factory, ISessionCoordinator coordinator,
    IParticipantConnectionRegistry connections, IServerInstance server, TimeProvider time)
    : IRequestHandler<ReportRecoveryBlockedCommand, ParticipantSnapshotDto>
{
    public async ValueTask<ParticipantSnapshotDto> Handle(ReportRecoveryBlockedCommand request, CancellationToken cancellationToken)
    {
        var participantId = request.Context.ParticipantId ?? throw CodiRequestException.AuthenticationRequired(ReportRecoveryBlockedCommand.RequestName);
        var connection = connections.Get(participantId) ?? throw CodiRequestException.ResourceNotFound();
        await using var coordination = await coordinator.AcquireAsync(connection.SessionId, cancellationToken).ConfigureAwait(false);
        await using var work = await factory.CreateAsync(useTransaction: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        var participant = await work.Repo<ISessionParticipantRepository>().GetAsync(participantId.ToString("D"), cancellationToken).ConfigureAwait(false);
        var session = await work.Repo<ILessonSessionRepository>().GetAsync(connection.SessionId.ToString("D"), cancellationToken).ConfigureAwait(false);
        var state = await work.Repo<IParticipantStateRepository>().GetAsync(participantId.ToString("D"), cancellationToken).ConfigureAwait(false);
        if (participant is null || session is null || state is null || participant.ConnectionEpoch != request.ConnectionEpoch ||
            connection.Epoch != request.ConnectionEpoch || !connection.SnapshotApplied || state.EffectiveRevision != request.EffectiveRevision ||
            participant.AdmissionStatusText != "Admitted" || participant.LeftAtUtcMs is not null || session.Status == "Ended" || state.AttemptId != request.AttemptId?.ToString("D"))
            throw new CodiRequestException(CodiErrorCodes.SessionStateConflict, "Il recupero riferisce uno stato superato.");
        state.ResumeContextJson ??= JsonSerializer.Serialize(DesiredStateProjection.Create(state), CodiJson.Payload);
        state.RecoveryBlockedFlag = 1;
        state.InteractionEnabledFlag = 0;
        state.UpdatedAtUtcMs = time.GetUtcNow().ToUnixTimeMilliseconds();
        await SessionCommandPreamble.UpdateStateAsync(work, state, cancellationToken).ConfigureAwait(false);
        await work.CommitAsync(cancellationToken).ConfigureAwait(false);
        return ParticipantSnapshotFactory.Create(server, session.ToDomain(), participantId, participant.ConnectionEpoch, time.GetUtcNow(), state,
            new ParticipantProfileDto(participantId, participant.DisplayName, participant.RowVersion));
    }
}

/// <summary>
/// Handshake applicativo (§11.2 passo 4): acquisisce l'operatività del partecipante autenticato,
/// incrementa l'epoca sulla partecipazione e restituisce lo stato con cui il client deve
/// sincronizzarsi. Lettura e scrittura stanno nella stessa transazione, così l'epoca letta dal
/// client è quella che il server ha realmente persistito.
///
/// Il protocollo si verifica **prima** della transazione: un client con una versione incompatibile
/// non deve ricevere un'epoca nuova, perché non saprebbe cosa farne, e la risposta gli dice di
/// aggiornarsi (§11.5). La versione dichiarata è quella del protocollo, non dell'applicazione: le
/// versioni dell'app e del motore viaggiano nell'involucro e servono a diagnosi.
/// </summary>
public sealed class InitializeParticipantConnectionHandler(
    IUnitOfWorkContext unitOfWorkContext,
    IServerInstance serverInstance,
    TimeProvider timeProvider) : IRequestHandler<InitializeParticipantConnectionCommand, ParticipantSnapshotDto>
{
    public async ValueTask<ParticipantSnapshotDto> Handle(
        InitializeParticipantConnectionCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.ParticipantId is not { } participantId)
        {
            throw CodiRequestException.AuthenticationRequired(InitializeParticipantConnectionCommand.RequestName);
        }

        EnsureSupportedProtocol(request.ProtocolVersion);
        EnsureDeclaredVersion(request.AppVersion, nameof(request.AppVersion));
        EnsureDeclaredVersion(request.EngineVersion, nameof(request.EngineVersion));

        var nowUtc = timeProvider.GetUtcNow();
        var participantKey = participantId.ToString("D");

        await using var work = await unitOfWorkContext
            .CreateAsync(useTransaction: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var participantRow = await work.Repo<ISessionParticipantRepository>()
            .GetAsync(participantKey, cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var participant = participantRow.ToDomain();

        var sessionRow = await work.Repo<ILessonSessionRepository>()
            .GetAsync(participant.SessionId.ToString("D"), cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var session = sessionRow.ToDomain();

        // Il trasporto ha già concesso `AdmittedParticipant`: se qui la partecipazione risulta
        // esclusa o la sessione terminata, lo stato è cambiato fra la verifica e la transazione.
        // Acquisire l'operatività su una lezione finita sarebbe un permesso che non esiste più.
        if (!participant.IsAdmitted || session.Status == SessionStatus.Ended)
        {
            throw new CodiRequestException(
                CodiErrorCodes.SessionStateConflict,
                "La partecipazione o la sessione non ammettono più una connessione operativa.");
        }

        var epoch = participant.AcquireConnection(nowUtc);

        var participantStore = work.Repo<ISessionParticipantRepository>();
        participantRow = SessionParticipantEntity.FromDomain(participant);
        if (!await participantStore
                .UpdateAsync(participantRow, cancellationToken)
                .ConfigureAwait(false))
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        var state = await work.Repo<IParticipantStateRepository>()
            .GetAsync(participantKey, cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        await work.CommitAsync(cancellationToken).ConfigureAwait(false);

        return ParticipantSnapshotFactory.Create(
            serverInstance,
            session,
            participant.Id,
            epoch,
            nowUtc,
            state,
            new ParticipantProfileDto(participant.Id, participant.DisplayName, participantRow.RowVersion));
    }

    /// <summary>
    /// Il server rifiuta un protocollo che non conosce (§11.5): il client mostra la schermata di
    /// aggiornamento e non tenta di interpretare uno stato che non sa leggere.
    /// </summary>
    private static void EnsureSupportedProtocol(int protocolVersion)
    {
        if (protocolVersion != ParticipantProtocol.CurrentVersion)
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed,
                $"Il protocollo {protocolVersion} non è supportato: il client deve aggiornarsi alla versione {ParticipantProtocol.CurrentVersion}.");
        }
    }

    /// <summary>
    /// Le versioni dichiarate dal client sono dati di diagnosi, non condizioni di accesso: si
    /// verifica soltanto che ci siano, entro un limite che impedisce di usarle come deposito.
    /// </summary>
    private static void EnsureDeclaredVersion(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);

        if (string.IsNullOrWhiteSpace(value) || value.Length > ParticipantProtocol.MaxVersionLength)
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed,
                "Le versioni dichiarate dal client devono essere presenti e brevi.");
        }
    }
}
