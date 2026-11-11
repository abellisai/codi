using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Sessions;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Realtime;

/// <summary>
/// Conferma applicativa dei comandi consegnati (§12.2). Ciò che si verifica è che il server non
/// deduce l'applicazione dall'invio: la consegna cambia stato solo quando il client dichiara che cosa
/// ha applicato, e ogni esito di §12.2 ha l'effetto che la specifica gli attribuisce — compresa la
/// pausa di recupero e il rifiuto di una dichiarazione che il server non conferma.
/// </summary>
public sealed class CommandAcknowledgementTests : IAsyncLifetime
{
    private OutboxScenario _scenario = null!;

    public async Task InitializeAsync() => _scenario = await OutboxScenario.CreateAsync();

    public async Task DisposeAsync() => await _scenario.DisposeAsync();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Conferma_Applicata_PortaLaConsegnaAdApplicataConGliIstanti()
    {
        var delivery = await SentCommandAsync();

        var result = await AcknowledgeAsync(delivery, CommandAcknowledgementOutcomes.Applied);

        Assert.Equal(CommandDeliveryStates.Applied, result.DeliveryStatus);
        Assert.False(result.RequiresRecovery);

        var row = await DeliveryRowAsync(delivery);
        Assert.Equal(CommandDeliveryStates.Applied, row["status"]);
        Assert.Equal(CodiUseCaseHost.Now.ToUnixTimeMilliseconds(), row["ack_at_utc_ms"]);
        Assert.Equal(CodiUseCaseHost.Now.ToUnixTimeMilliseconds(), row["applied_at_utc_ms"]);
        Assert.Null(row["error_code"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Conferma_ConRecuperoRichiesto_MaterializzaLaPausaDiRecupero()
    {
        // §12.3: il client non è certo di ciò che ha eseguito, quindi il lavoro si sospende e il
        // docente decide se riprendere con un checkpoint o riavviare.
        var delivery = await SentCommandAsync();

        var result = await AcknowledgeAsync(delivery, CommandAcknowledgementOutcomes.AppliedWithRecoveryRequired);

        Assert.True(result.RequiresRecovery);

        var row = await DeliveryRowAsync(delivery);
        Assert.Equal(CommandDeliveryStates.Applied, row["status"]);
        Assert.Equal(1L, row["requires_recovery"]);

        var state = await StateRowAsync(delivery.ParticipantId);
        Assert.Equal(1L, state["recovery_blocked"]);
        Assert.Equal(0L, state["interaction_enabled"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Conferma_SuperataDaUnaRevisionePiuRecente_MarcaLaConsegnaSuperata()
    {
        // Il client rifiuta di applicare ciò che è già vecchio: il server conferma la revisione
        // obsoleta e non riporta indietro lo stato (§12.2).
        var delivery = await SentCommandAsync();
        await _scenario.ShowWaitingAsync(delivery.Seed, delivery.EffectiveRevision);

        var result = await AcknowledgeAsync(delivery, CommandAcknowledgementOutcomes.RejectedStale);

        Assert.Equal(CommandDeliveryStates.Superseded, result.DeliveryStatus);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Conferma_SuperataSenzaRevisionePiuRecente_NonCambiaLaConsegna()
    {
        // Il client dichiara superato un comando che il server considera ancora corrente: la
        // dichiarazione non si trasforma in un rifiuto registrato, perché non è vero che lo stato sia
        // avanzato — resta il fatto che il client non l'ha applicato.
        var delivery = await SentCommandAsync();

        var result = await AcknowledgeAsync(delivery, CommandAcknowledgementOutcomes.RejectedStale);

        Assert.Equal(CommandDeliveryStates.Sent, result.DeliveryStatus);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Conferma_DiFallimento_RegistraLoStatoEIlCodice()
    {
        var delivery = await SentCommandAsync();

        var result = await AcknowledgeAsync(delivery, CommandAcknowledgementOutcomes.Failed, errorCode: "WorkerCrashed");

        Assert.Equal(CommandDeliveryStates.Failed, result.DeliveryStatus);

        var row = await DeliveryRowAsync(delivery);
        Assert.Equal("WorkerCrashed", row["error_code"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Conferma_Ripetuta_NonRiscriveERitornaLoStatoReale()
    {
        // Ripetere la conferma non cambia nulla: la consegna è già applicata e il server continua a
        // dire la verità sulla propria riga.
        var delivery = await SentCommandAsync();
        await AcknowledgeAsync(delivery, CommandAcknowledgementOutcomes.Applied);

        var repeated = await AcknowledgeAsync(delivery, CommandAcknowledgementOutcomes.Failed, errorCode: "Tardi");

        Assert.Equal(CommandDeliveryStates.Applied, repeated.DeliveryStatus);

        var row = await DeliveryRowAsync(delivery);
        Assert.Equal(CommandDeliveryStates.Applied, row["status"]);
        Assert.Null(row["error_code"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Conferma_DopoLaFineDellaLezione_EAncoraAmmessa()
    {
        // A-04, §12.7: l'ultima conferma arriva mentre la sessione si chiude. Il client deve poter
        // dire che cosa ha applicato, e il server non deve riaprire permessi operativi per questo.
        var delivery = await SentCommandAsync();
        await EndSessionAsync(delivery);

        var result = await AcknowledgeAsync(delivery, CommandAcknowledgementOutcomes.Applied);

        Assert.Equal(CommandDeliveryStates.Applied, result.DeliveryStatus);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Conferma_ConEpocaSuperata_Rifiuta()
    {
        // §12.5: una pagina che ha perso l'operatività non riabilita la propria consegna.
        var delivery = await SentCommandAsync();

        var rejection = await Assert.ThrowsAsync<CodiRequestException>(
            () => AcknowledgeAsync(delivery with { ConnectionEpoch = 99 }, CommandAcknowledgementOutcomes.Applied));

        Assert.Equal(CodiErrorCodes.SessionStateConflict, rejection.Code);
        Assert.Equal(CommandDeliveryStates.Sent, (await DeliveryRowAsync(delivery))["status"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Conferma_ConRevisioneDiversaDaQuellaDelComando_Rifiuta()
    {
        var delivery = await SentCommandAsync();

        var rejection = await Assert.ThrowsAsync<CodiRequestException>(
            () => AcknowledgeAsync(delivery with { EffectiveRevision = delivery.EffectiveRevision + 1 }, CommandAcknowledgementOutcomes.Applied));

        Assert.Equal(CodiErrorCodes.ValidationFailed, rejection.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Conferma_ConEsitoFuoriContratto_Rifiuta()
    {
        var delivery = await SentCommandAsync();

        var rejection = await Assert.ThrowsAsync<CodiRequestException>(
            () => AcknowledgeAsync(delivery, "QuasiApplicato"));

        Assert.Equal(CodiErrorCodes.ValidationFailed, rejection.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Conferma_DiUnComandoDiUnAltroPartecipante_RifiutaSenzaToccareLaConsegna()
    {
        var delivery = await SentCommandAsync();
        var other = await _scenario.JoinAsync(delivery.Seed, "Anna", "chiave-di-anna");

        var rejection = await Assert.ThrowsAsync<CodiRequestException>(
            () => AcknowledgeAsync(delivery, CommandAcknowledgementOutcomes.Applied, participantId: other.Joined.ParticipantId));

        // La consegna di Luca non esiste per Anna: è una risorsa assente, non un permesso negato.
        Assert.Equal(CodiErrorCodes.ResourceNotFound, rejection.Code);
        Assert.Equal(CommandDeliveryStates.Sent, (await DeliveryRowAsync(delivery))["status"]);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task MancataConferma_DopoLAttesa_MarcaLaConsegnaEUnAckTardivoLaApplica()
    {
        // §12.2: dopo cinque secondi la mancata conferma si evidenzia, non si condanna. Un ACK
        // tardivo torna ad applicare la consegna, perché l'evidenza non è un rifiuto.
        var delivery = await SentCommandAsync();

        var later = CodiUseCaseHost.Now.Add(ParticipantProtocol.CommandAcknowledgementTimeout).AddSeconds(1);
        var sweep = new OutboxDispatcher(
            _scenario.Host.Services.GetRequiredService<IUnitOfWorkContext>(),
            new RecordingCommandDeliveryNotifier(),
            new AdjustableTimeProvider(later));

        var result = await sweep.DispatchPendingAsync(CancellationToken.None);

        Assert.Equal(1, result.DeliveriesUnconfirmed);
        Assert.Equal(CommandDeliveryStates.Unconfirmed, (await DeliveryRowAsync(delivery))["status"]);

        var confirmed = await AcknowledgeAsync(delivery, CommandAcknowledgementOutcomes.Applied);

        Assert.Equal(CommandDeliveryStates.Applied, confirmed.DeliveryStatus);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Conferma_ConProtocolloDiverso_Rifiuta()
    {
        var delivery = await SentCommandAsync();

        var rejection = await Assert.ThrowsAsync<CodiRequestException>(
            () => AcknowledgeAsync(delivery, CommandAcknowledgementOutcomes.Applied, protocolVersion: 99));

        Assert.Equal(CodiErrorCodes.ValidationFailed, rejection.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Conferma_SenzaIdentita_Rifiuta()
    {
        var delivery = await SentCommandAsync();

        var rejection = await Assert.ThrowsAsync<CodiRequestException>(
            () => AcknowledgeAsync(delivery, CommandAcknowledgementOutcomes.Applied, anonymous: true));

        Assert.Equal(CodiErrorCodes.AuthenticationRequired, rejection.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Conferma_DiUnComandoInesistente_Rifiuta()
    {
        var delivery = await SentCommandAsync();

        var rejection = await Assert.ThrowsAsync<CodiRequestException>(
            () => AcknowledgeAsync(delivery with { CommandId = Guid.NewGuid() }, CommandAcknowledgementOutcomes.Applied));

        Assert.Equal(CodiErrorCodes.ResourceNotFound, rejection.Code);
    }

    /// <summary>Comando di regia consegnato al partecipante: la coda da cui parte ogni conferma.</summary>
    private async Task<SentCommand> SentCommandAsync()
    {
        var seed = await _scenario.StartedSessionAsync();
        var joined = await _scenario.JoinAsync(seed, "Luca");
        var accepted = await _scenario.ShowWaitingAsync(seed, expectedSessionRevision: 2L);

        var dispatcher = new OutboxDispatcher(
            _scenario.Host.Services.GetRequiredService<IUnitOfWorkContext>(),
            new RecordingCommandDeliveryNotifier(),
            _scenario.Host.Services.GetRequiredService<TimeProvider>());

        var dispatched = await dispatcher.DispatchPendingAsync(CancellationToken.None);
        Assert.Equal(1, dispatched.DeliveriesSent);

        return new SentCommand(
            seed,
            joined.Joined.CredentialSessionId,
            joined.Joined.ParticipantId,
            accepted.CommandId,
            accepted.SessionRevision,
            ConnectionEpoch: 0);
    }

    private async Task<CommandAcknowledgementDto> AcknowledgeAsync(
        SentCommand delivery,
        string outcome,
        string? errorCode = null,
        Guid? participantId = null,
        int protocolVersion = ParticipantProtocol.CurrentVersion,
        bool anonymous = false)
    {
        var context = anonymous
            ? CodiRequestContext.Anonymous
            : new CodiRequestContext(
                isAuthenticated: true,
                [CodiPolicy.AdmittedParticipant, CodiPolicy.ActiveParticipantConnection],
                delivery.CredentialSessionId,
                adminUserId: null,
                participantId ?? delivery.ParticipantId);

        return await _scenario.Host.CommandAsAsync<CommandAcknowledgementDto>(
            AcknowledgeCommandCommand.RequestName,
            new
            {
                commandId = delivery.CommandId,
                protocolVersion,
                effectiveRevision = delivery.EffectiveRevision,
                connectionEpoch = delivery.ConnectionEpoch,
                outcome,
                runtimeStatus = "Paused",
                lastPersistedProjectRevision = (long?)null,
                errorCode,
            },
            context);
    }

    /// <summary>Chiude la lezione come farebbe il docente dal dispatcher admin.</summary>
    private async Task EndSessionAsync(SentCommand delivery) =>
        await _scenario.Host.CommandAsAsync<SessionDto>(
            EndSessionCommand.RequestName,
            new { sessionId = delivery.Seed.SessionId, expectedSessionRevision = delivery.EffectiveRevision },
            _scenario.RegiaContext());

    private async Task<IReadOnlyDictionary<string, object?>> DeliveryRowAsync(SentCommand delivery) =>
        await _scenario.RowAsync(
            "SELECT status, requires_recovery, ack_at_utc_ms, applied_at_utc_ms, error_code FROM command_delivery " +
            $"WHERE command_id = '{delivery.CommandId:D}' AND participant_id = '{delivery.ParticipantId:D}';");

    private async Task<IReadOnlyDictionary<string, object?>> StateRowAsync(Guid participantId) =>
        await _scenario.RowAsync(
            "SELECT recovery_blocked, interaction_enabled FROM participant_state " +
            $"WHERE participant_id = '{participantId:D}';");

    /// <summary>Comando consegnato, con i dati che servono a confermarlo.</summary>
    private sealed record SentCommand(
        Seed Seed,
        Guid CredentialSessionId,
        Guid ParticipantId,
        Guid CommandId,
        long EffectiveRevision,
        long ConnectionEpoch);
}
