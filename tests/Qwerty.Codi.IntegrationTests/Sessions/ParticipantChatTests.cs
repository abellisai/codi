using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Exercises;
using Qwerty.Codi.Application.Sessions;
using Qwerty.Codi.Domain.Sessions;
using Qwerty.Codi.IntegrationTests.Realtime;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Sessions;

/// <summary>
/// Chat fra il docente e il singolo alunno (§8, §15.1) dal percorso reale del dispatcher. Il thread
/// vive in memoria, quindi qui si verifica ciò che il caso d'uso decide: chi può scrivere, quando il
/// testo è rifiutato, che la stessa chiave non duplica un messaggio, che il conteggio dei non letti
/// si azzera solo con la lettura del docente, e che il thread sparisce quando la lezione finisce o
/// l'alunno non è più un destinatario.
///
/// Il trasporto è sostituito dal doppio che registra gli avvisi: il caso d'uso non conosce SignalR,
/// quindi si verifica a quale parte l'applicazione ha deciso di portare il messaggio.
/// </summary>
public sealed class ParticipantChatTests : IAsyncLifetime
{
    private OutboxScenario _scenario = null!;
    private Seed _seed = null!;
    private JoinSessionOutcome _luca = null!;
    private JoinSessionOutcome _anna = null!;
    private RecordingParticipantMessageNotifier _notifier = null!;

    private static readonly string Definition = JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        exerciseType = "GridPath",
        engineVersion = "gridpath-1",
        title = "Stella",
        instructions = "Arriva alla stella.",
        scene = new { width = 5, height = 4, start = new { x = 0, y = 1, direction = "East" }, obstacles = Array.Empty<object>(), goal = new { x = 3, y = 1 } },
        toolbox = new { allowedBlocks = new[] { "aula_start", "aula_move" } },
        limits = new { maxBlocks = 30, maxNestingDepth = 8, maxExecutionSteps = 1000, maxActiveDurationMs = 120000 },
        completion = new { type = "ReachCell", x = 3, y = 1 },
        feedback = new { success = "Bravo!", collision = "Ops!" },
    });

    private CodiRequestContext LucaContext => ContextOf(_luca);

    private static CodiRequestContext ContextOf(JoinSessionOutcome joined) =>
        new(true, [CodiPolicy.AdmittedParticipant, CodiPolicy.ActiveParticipantConnection],
            joined.Joined.CredentialSessionId, participantId: joined.Joined.ParticipantId);

    public async Task InitializeAsync()
    {
        _scenario = await OutboxScenario.CreateAsync();
        _seed = await _scenario.StartedSessionAsync();
        _luca = await _scenario.JoinAsync(_seed, "Luca");
        _anna = await _scenario.JoinAsync(_seed, "Anna", idempotencyKey: "chiave-di-anna");
        _notifier = (RecordingParticipantMessageNotifier)_scenario.Host.Services
            .GetRequiredService<IParticipantMessageNotifier>();

        // L'attività assegnata e accesa: è la condizione perché la pagina dell'alunno non sia in
        // attesa, e quindi perché la chat sia aperta come il resto dei controlli (§14.3).
        var exercise = await _scenario.Host.CommandAsAsync<ExerciseDto>(
            CreateExerciseCommand.RequestName,
            new { exerciseType = "GridPath", title = "Stella" },
            _scenario.RegiaContext());
        var draft = await _scenario.Host.CommandAsAsync<ExerciseDraftDto>(
            SaveExerciseDraftCommand.RequestName,
            new { exerciseId = exercise.Id, definitionJson = Definition, workspaceJson = "{}" },
            _scenario.RegiaContext());
        var version = await _scenario.Host.CommandAsAsync<ExerciseVersionDto>(
            PublishExerciseVersionCommand.RequestName,
            new { exerciseId = exercise.Id, baseRowVersion = draft.RowVersion },
            _scenario.RegiaContext());
        await _scenario.Host.CommandAsAsync<CommandAcceptedDto>(
            AssignExerciseCommand.RequestName,
            new { sessionId = _seed.SessionId, expectedSessionRevision = await RevisionAsync(), exerciseVersionId = version.Id },
            _scenario.RegiaContext());
        await _scenario.Host.CommandAsAsync<CommandAcceptedDto>(
            EnableActivityCommand.RequestName,
            new { sessionId = _seed.SessionId, expectedSessionRevision = await RevisionAsync() },
            _scenario.RegiaContext());
    }

    public async Task DisposeAsync() => await _scenario.DisposeAsync();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PostTeacherMessage_ConAlunnoAmmesso_ConservaIlMessaggioEAvvisaIlPartecipante()
    {
        // Act
        var stored = await PostTeacherAsync("Buongiorno, hai bisogno di aiuto?", "chiave-1");

        // Assert: il testo è quello normalizzato dal dominio, l'autore è il docente e l'avviso va
        // solo al gruppo di Luca.
        Assert.Equal("Buongiorno, hai bisogno di aiuto?", stored.Text);
        Assert.Equal(MessageAuthor.Teacher.ToString(), stored.Author);
        Assert.Equal(_seed.SessionId, stored.SessionId);
        Assert.Equal(_luca.Joined.ParticipantId, stored.ParticipantId);
        Assert.Equal(CodiUseCaseHost.Now, stored.WrittenAtUtc);
        Assert.Single(_notifier.ToParticipant);
        Assert.Empty(_notifier.ToTeachers);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PostParticipantMessage_ConAlunnoAttivo_ConservaIlMessaggioEAvvisaIlDocente()
    {
        // Arrange: l'operatività della pagina di Luca è quella che il trasporto ha registrato.
        AcquireConnection(_luca);

        // Act
        var stored = await PostParticipantAsync(_luca, "Non capisco il blocco ripetuto", "chiave-figlio-1");

        // Assert
        Assert.Equal("Non capisco il blocco ripetuto", stored.Text);
        Assert.Equal(MessageAuthor.Participant.ToString(), stored.Author);
        Assert.Equal(_luca.Joined.ParticipantId, stored.ParticipantId);
        Assert.Single(_notifier.ToTeachers);
        Assert.Empty(_notifier.ToParticipant);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PostParticipantMessage_ConStessaChiave_RestituisceLoStessoMessaggioSenzaDuplicarlo()
    {
        // Arrange: una risposta persa non è un messaggio nuovo, è la stessa richiesta rienviata.
        AcquireConnection(_luca);
        var first = await PostParticipantAsync(_luca, "Posso finire a casa?", "chiave-ripetuta");

        // Act
        var repeated = await PostParticipantAsync(_luca, "Posso finire a casa?", "chiave-ripetuta");

        // Assert: lo stesso messaggio, nessun duplicato e nessun secondo avviso.
        Assert.Equal(first.MessageId, repeated.MessageId);
        Assert.Single(_notifier.ToTeachers);
        Assert.Single((await OwnThreadAsync(_luca)).Messages);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PostParticipantMessage_ConAlunnoInPausaDocente_RifiutaComeIlRestoDellaPagina()
    {
        // Arrange: la pausa di classe ferma i controlli degli alunni (§14.3), e la chat segue la
        // pagina per decisione dell'utente del 13 ottobre 2026.
        AcquireConnection(_luca);
        await _scenario.Host.CommandAsAsync<SessionDto>(
            PauseCommand.RequestName,
            new { sessionId = _seed.SessionId, expectedSessionRevision = await RevisionAsync() },
            _scenario.RegiaContext());

        // Act
        var failure = await _scenario.Host.ExpectFailureAsAsync(
            PostParticipantMessageCommand.RequestName,
            new { text = "Sono in pausa?", clientMessageKey = "chiave-pausa" },
            LucaContext);

        // Assert
        Assert.Equal(CodiErrorCodes.SessionStateConflict, failure.Code);
        Assert.Empty(_notifier.ToTeachers);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PostParticipantMessage_ConAlunnoPortatoInAttesa_Rifiuta()
    {
        // Arrange: l'attesa è la vista dove i controlli degli alunni non sono accesi, e la chat si
        // disabilita insieme a loro.
        AcquireConnection(_luca);
        await _scenario.Host.CommandAsAsync<CommandAcceptedDto>(
            ShowWaitingCommand.RequestName,
            new { sessionId = _seed.SessionId, expectedSessionRevision = await RevisionAsync() },
            _scenario.RegiaContext());

        // Act
        var failure = await _scenario.Host.ExpectFailureAsAsync(
            PostParticipantMessageCommand.RequestName,
            new { text = "Ciao maestro", clientMessageKey = "chiave-attesa" },
            LucaContext);

        // Assert
        Assert.Equal(CodiErrorCodes.SessionStateConflict, failure.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ciao\nCiao")]
    [Trait("Category", "Integration")]
    public async Task PostParticipantMessage_ConTestoNonUsabile_RifiutaConValidazione(string text)
    {
        AcquireConnection(_luca);

        var failure = await _scenario.Host.ExpectFailureAsAsync(
            PostParticipantMessageCommand.RequestName,
            new { text, clientMessageKey = "chiave-forma" },
            LucaContext);

        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PostParticipantMessage_SenzaChiaveDelClient_RifiutaConValidazione()
    {
        AcquireConnection(_luca);

        var failure = await _scenario.Host.ExpectFailureAsAsync(
            PostParticipantMessageCommand.RequestName,
            new { text = "Ciao", clientMessageKey = " " },
            LucaContext);

        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PostTeacherMessage_QuandoIlThreadEAllaCapienza_RifiutaSenzaScartare()
    {
        // Arrange: la capienza è un limite del processo (§15.6), e il messaggio oltre viene rifiutato
        // invece di essere accodato a un elenco illimitato.
        var store = _scenario.Host.Services.GetRequiredService<IParticipantMessageStore>();
        var capacity = store.MaxMessagesPerParticipant;
        for (var index = 0; index < capacity; index++)
        {
            await PostTeacherAsync($"messaggio {index}", $"chiave-capienza-{index}");
        }

        // Act
        var failure = await _scenario.Host.ExpectFailureAsAsync(
            PostTeacherMessageCommand.RequestName,
            new
            {
                sessionId = _seed.SessionId,
                participantId = _luca.Joined.ParticipantId,
                text = "messaggio oltre la capienza",
                clientMessageKey = "chiave-oltre",
            },
            _scenario.RegiaContext());

        // Assert
        Assert.Equal(CodiErrorCodes.MessageThreadFull, failure.Code);
        Assert.Equal(capacity, store.Read(_luca.Joined.ParticipantId, markRead: false).Messages.Count);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PostTeacherMessage_SuSessionTerminata_Rifiuta()
    {
        // Act
        await _scenario.Host.CommandAsAsync<SessionDto>(
            EndSessionCommand.RequestName,
            new { sessionId = _seed.SessionId, expectedSessionRevision = await RevisionAsync() },
            _scenario.RegiaContext());

        var failure = await _scenario.Host.ExpectFailureAsAsync(
            PostTeacherMessageCommand.RequestName,
            new
            {
                sessionId = _seed.SessionId,
                participantId = _luca.Joined.ParticipantId,
                text = "Ciao",
                clientMessageKey = "chiave-dopo-il-termine",
            },
            _scenario.RegiaContext());

        Assert.Equal(CodiErrorCodes.SessionStateConflict, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PostTeacherMessage_SuPartecipanteEscluso_RifiutaEDEliminaIlThread()
    {
        // Arrange
        await PostTeacherAsync("Ciao Luca", "chiave-esclusione");

        // Act
        await _scenario.Host.CommandAsAsync<SessionParticipantDto>(
            ExcludeParticipantCommand.RequestName,
            new { sessionId = _seed.SessionId, participantId = _luca.Joined.ParticipantId, baseRowVersion = 1L },
            _scenario.RegiaContext());

        // Assert: il thread non appartiene più a un destinatario ammesso (§15.1).
        Assert.Empty(_scenario.Host.Services.GetRequiredService<IParticipantMessageStore>()
            .Read(_luca.Joined.ParticipantId, markRead: false).Messages);

        var failure = await _scenario.Host.ExpectFailureAsAsync(
            PostTeacherMessageCommand.RequestName,
            new
            {
                sessionId = _seed.SessionId,
                participantId = _luca.Joined.ParticipantId,
                text = "Ciao",
                clientMessageKey = "chiave-dopo-l-esclusione",
            },
            _scenario.RegiaContext());

        Assert.Equal(CodiErrorCodes.SessionStateConflict, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task EndSession_EliminaIThreadDellaChatDellaSessione()
    {
        // Arrange
        await PostTeacherAsync("Ciao Luca", "chiave-fine");
        AcquireConnection(_anna);
        await PostParticipantAsync(_anna, "Ciao maestro", "chiave-fine-figlio");

        // Act
        await _scenario.Host.CommandAsAsync<SessionDto>(
            EndSessionCommand.RequestName,
            new { sessionId = _seed.SessionId, expectedSessionRevision = await RevisionAsync() },
            _scenario.RegiaContext());

        // Assert: il testo scritto dai bambini non resta nel processo dopo la lezione.
        var store = _scenario.Host.Services.GetRequiredService<IParticipantMessageStore>();
        Assert.Empty(store.Read(_luca.Joined.ParticipantId, markRead: false).Messages);
        Assert.Empty(store.Read(_anna.Joined.ParticipantId, markRead: false).Messages);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LeaveSession_EliminaIlThreadDellAlunnoUscito()
    {
        // Arrange
        await PostTeacherAsync("Ciao Luca", "chiave-uscita");

        // Act
        Assert.True(await _scenario.Host.CommandAsAsync<bool>(
            LeaveSessionCommand.RequestName,
            new { },
            LucaContext));

        // Assert
        Assert.Empty(_scenario.Host.Services.GetRequiredService<IParticipantMessageStore>()
            .Read(_luca.Joined.ParticipantId, markRead: false).Messages);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetParticipantMessages_LetturaDelDocenteAzzeraINonLettiMostratiSullaCard()
    {
        // Arrange: due messaggi dell'alunno, letti dalla griglia come non letti.
        AcquireConnection(_luca);
        await PostParticipantAsync(_luca, "Primo", "chiave-nonletti-1");
        await PostParticipantAsync(_luca, "Secondo", "chiave-nonletti-2");
        Assert.Equal(2, (await ReadGridAsync()).UnreadMessages);

        // Act
        var thread = await _scenario.Host.QueryOneAsAsync<ParticipantThreadDto>(
            GetParticipantMessagesQuery.RequestName,
            new { sessionId = _seed.SessionId, participantId = _luca.Joined.ParticipantId },
            _scenario.RegiaContext());

        // Assert: la lettura ha un effetto osservabile, ed è l'unico della chat.
        Assert.Equal(2, thread.UnreadByTeacher);
        Assert.Equal(2, thread.Messages.Count);
        Assert.Equal(0, (await ReadGridAsync()).UnreadMessages);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetOwnMessages_LetturaDellAlunnoNonAzzeraIConteggiDelDocente()
    {
        // Arrange
        AcquireConnection(_luca);
        await PostParticipantAsync(_luca, "Il mio messaggio", "chiave-propri");

        // Act: l'alunno legge il proprio thread.
        var own = await OwnThreadAsync(_luca);

        // Assert: il server non misura se un bambino ha letto, quindi non finge di farlo.
        Assert.Equal(0, own.UnreadByTeacher);
        Assert.Single(own.Messages);
        Assert.Equal(1, (await ReadGridAsync()).UnreadMessages);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetOwnMessages_ConIdentitaDiUnAltroAlunno_LeggeSoloIlProprioThread()
    {
        // Arrange: il thread di Luca esiste, quello di Anna no. L'identità arriva dal contesto e non
        // dai parametri (§9.3).
        AcquireConnection(_luca);
        await PostParticipantAsync(_luca, "Solo di Luca", "chiave-isolamento");

        // Act
        var annaThread = await OwnThreadAsync(_anna);

        // Assert
        Assert.Equal(_anna.Joined.ParticipantId, annaThread.ParticipantId);
        Assert.Empty(annaThread.Messages);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetParticipantMessages_ConContestoDiPartecipante_RifiutaConPermessoNegato()
    {
        // Assert sulla policy: la lettura del thread di un alunno è una lettura di regia, e un alunno
        // non legge il thread di un altro alunno.
        var failure = await _scenario.Host.ExpectQueryFailureAsAsync(
            GetParticipantMessagesQuery.RequestName,
            new { sessionId = _seed.SessionId, participantId = _luca.Joined.ParticipantId },
            LucaContext);

        Assert.Equal(CodiErrorCodes.PermissionDenied, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetParticipantMessages_SuSessionDiUnAltroDocente_RifiutaConRisorsaAssente()
    {
        var otherAdmin = new CodiRequestContext(
            isAuthenticated: true,
            [CodiPolicy.Admin, CodiPolicy.SessionController],
            adminUserId: Guid.NewGuid());

        var failure = await _scenario.Host.ExpectQueryFailureAsAsync(
            GetParticipantMessagesQuery.RequestName,
            new { sessionId = _seed.SessionId, participantId = _luca.Joined.ParticipantId },
            otherAdmin);

        Assert.Equal(CodiErrorCodes.ResourceNotFound, failure.Code);
    }

    private Task<ParticipantMessageDto> PostTeacherAsync(string text, string key) =>
        _scenario.Host.CommandAsAsync<ParticipantMessageDto>(
            PostTeacherMessageCommand.RequestName,
            new
            {
                sessionId = _seed.SessionId,
                participantId = _luca.Joined.ParticipantId,
                text,
                clientMessageKey = key,
            },
            _scenario.RegiaContext());

    private Task<ParticipantMessageDto> PostParticipantAsync(JoinSessionOutcome joined, string text, string key) =>
        _scenario.Host.CommandAsAsync<ParticipantMessageDto>(
            PostParticipantMessageCommand.RequestName,
            new { text, clientMessageKey = key },
            ContextOf(joined));

    private Task<ParticipantThreadDto> OwnThreadAsync(JoinSessionOutcome joined) =>
        _scenario.Host.QueryOneAsAsync<ParticipantThreadDto>(
            GetOwnMessagesQuery.RequestName,
            new { },
            ContextOf(joined));

    private async Task<SessionParticipantOverviewDto> ReadGridAsync()
    {
        var grid = Assert.IsAssignableFrom<IReadOnlyList<SessionParticipantOverviewDto>>(
            await _scenario.Host.ExecuteAsAsync(
                CodiRequestKind.Query,
                ListSessionParticipantsQuery.RequestName,
                new { sessionId = _seed.SessionId },
                _scenario.RegiaContext()));

        return grid.Single(row => row.ParticipantId == _luca.Joined.ParticipantId);
    }

    /// <summary>
    /// Operatività della pagina dichiarata al registro in memoria: senza di essa il contesto non
    /// concede <c>ActiveParticipantConnection</c> e il messaggio non sarebbe ammissibile.
    /// </summary>
    private void AcquireConnection(JoinSessionOutcome joined) =>
        _scenario.Host.Services.GetRequiredService<IParticipantConnectionRegistry>()
            .Acquire(
                new ParticipantConnectionRequest(
                    joined.Joined.ParticipantId,
                    _seed.SessionId,
                    Guid.NewGuid(),
                    "connessione-di-prova",
                    1L,
                    2L),
                CodiUseCaseHost.Now);

    private Task<long> RevisionAsync() =>
        _scenario.ScalarAsync<long>($"SELECT session_revision FROM lesson_session WHERE id = '{_seed.SessionId:D}';");
}
