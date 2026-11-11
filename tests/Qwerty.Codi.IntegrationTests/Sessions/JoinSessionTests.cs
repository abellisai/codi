using Qwerty.Codi.Application.Dto;
using System.Data.Common;
using System.Globalization;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Lessons;
using Qwerty.Codi.Application.Organization;
using Qwerty.Codi.Application.Sessions;
using Qwerty.Codi.Domain.Sessions;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Sessions;

/// <summary>
/// Ingresso idempotente di un alunno nella sessione (§9.4) dal percorso reale: registro, mediator,
/// casi d'uso e database migrato. I test coprono il percorso positivo, la ripetizione della stessa
/// richiesta con la stessa chiave, il riuso della chiave con contenuto diverso, i rifiuti previsti
/// — cookie di ammissione mancante, chiave mancante, nome inutilizzabile, sessione non avviata o
/// terminata, ingressi chiusi, catena disallineata o archiviata — la credenziale revocata dopo una
/// risposta persa e la lettura del contesto del partecipante.
///
/// Lo **scope** dell'ingresso è ciò che distingue due tentativi dello stesso browser: qui è
/// dichiarato dal caso di prova, mentre nel trasporto arriva dal ticket di ammissione. La stessa
/// chiave con due scope diversi è due ingressi, ed è la ragione per cui il registro conserva
/// l'impronta dello scope e non la chiave soltanto.
/// </summary>
public sealed class JoinSessionTests : IAsyncLifetime
{
    private CodiUseCaseHost _host = null!;

    private Guid _adminId;

    public async Task InitializeAsync()
    {
        _host = await CodiUseCaseHost.CreateAsync();
        _adminId = await InsertAdminUserAsync();
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConIngressoNuovo_CreaPartecipazioneStatoECredenziale()
    {
        // Arrange
        var session = await StartedSessionAsync();

        // Act
        var outcome = await JoinAsync(session, "  Luca  ");

        // Assert: l'esito è il riferimento alle entità create e la vista con cui partire.
        Assert.NotEqual(Guid.Empty, outcome.Joined.ParticipantId);
        Assert.Equal(session.SessionId, outcome.Joined.SessionId);
        Assert.Equal("Luca", outcome.Joined.DisplayName);
        Assert.Equal(nameof(ParticipantView.Waiting), outcome.Joined.View);
        Assert.NotEqual(Guid.Empty, outcome.Joined.CredentialSessionId);
        Assert.False(string.IsNullOrWhiteSpace(outcome.CredentialStamp));

        var participant = await ColumnsAsync(
            "SELECT display_name, admission_status, connection_epoch, station_id, row_version FROM session_participant WHERE id = $id;",
            outcome.Joined.ParticipantId);
        Assert.Equal("Luca", participant[0]);
        Assert.Equal("Admitted", participant[1]);
        Assert.Equal(0L, participant[2]);
        Assert.IsType<DBNull>(participant[3]);
        Assert.Equal(1L, participant[4]);

        var state = await ColumnsAsync(
            "SELECT view, effective_revision, interaction_enabled, teacher_paused, recovery_blocked, execution_generation FROM participant_state WHERE participant_id = $id;",
            outcome.Joined.ParticipantId);
        Assert.Equal("Waiting", state[0]);
        // Lo stato iniziale è la copia del default di classe alla revisione della sessione (§6.2):
        // dopo l'avvio la sessione è alla revisione 2.
        Assert.Equal(2L, state[1]);
        Assert.Equal(0L, state[2]);
        Assert.Equal(0L, state[3]);
        Assert.Equal(0L, state[4]);
        Assert.Equal(0L, state[5]);

        var credential = await ColumnsAsync(
            "SELECT principal_kind, participant_id, admin_user_id, revoked_at_utc_ms FROM credential_session WHERE id = $id;",
            outcome.Joined.CredentialSessionId);
        Assert.Equal("Participant", credential[0]);
        Assert.Equal(outcome.Joined.ParticipantId.ToString("D"), credential[1]);
        Assert.IsType<DBNull>(credential[2]);
        Assert.IsType<DBNull>(credential[3]);

        Assert.Equal(1L, await ScalarAsync<long>("SELECT COUNT(*) FROM idempotency_record;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_RipetutoConLaStessaChiave_RestituisceLaStessaPartecipazione()
    {
        // Arrange: il caso del doppio clic su «Entra» e della risposta persa (§9.4).
        var session = await StartedSessionAsync();
        var first = await JoinAsync(session, "Luca");

        // Act
        var second = await JoinAsync(session, "Luca");

        // Assert
        Assert.Equal(first.Joined.ParticipantId, second.Joined.ParticipantId);
        Assert.Equal(first.Joined.CredentialSessionId, second.Joined.CredentialSessionId);
        Assert.Equal(first.CredentialStamp, second.CredentialStamp);
        Assert.Equal(1L, await ScalarAsync<long>("SELECT COUNT(*) FROM session_participant;"));
        Assert.Equal(1L, await ScalarAsync<long>("SELECT COUNT(*) FROM participant_state;"));
        Assert.Equal(1L, await ScalarAsync<long>("SELECT COUNT(*) FROM credential_session;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConLaStessaChiaveEContenutoDiverso_RifiutaConConflittoDiIdempotenza()
    {
        // Arrange: la chiave identifica una richiesta, non un browser: riusarla con un altro nome
        // non recupera l'esito registrato (§9.4).
        var session = await StartedSessionAsync();
        await JoinAsync(session, "Luca");

        // Act
        var failure = await _host.ExpectFailureAsAsync(
            JoinSessionCommand.RequestName,
            Body(session, "Anna"),
            CodiUseCaseHost.JoinContext("scope-di-prova", "chiave-di-prova"));

        // Assert
        Assert.Equal(CodiErrorCodes.IdempotencyKeyConflict, failure.Code);
        Assert.Equal(1L, await ScalarAsync<long>("SELECT COUNT(*) FROM session_participant;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConScopeDiverso_CreaUnaSecondaPartecipazione()
    {
        // Arrange: due browser distinti possono usare per caso la stessa chiave senza interferire.
        var session = await StartedSessionAsync();
        var first = await JoinAsync(session, "Luca");

        // Act
        var second = await _host.CommandAsAsync<JoinSessionOutcome>(
            JoinSessionCommand.RequestName,
            Body(session, "Anna"),
            CodiUseCaseHost.JoinContext("altro-scope", "chiave-di-prova"));

        // Assert
        Assert.NotEqual(first.Joined.ParticipantId, second.Joined.ParticipantId);
        Assert.Equal(2L, await ScalarAsync<long>("SELECT COUNT(*) FROM session_participant;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_SenzaCookieDiAmmissione_RifiutaConDatiNonValidi()
    {
        // Arrange: senza il ticket del bootstrap l'ingresso non ha scope, quindi non sarebbe
        // idempotente: è una richiesta non utilizzabile, non un ingresso riuscito a metà.
        var session = await StartedSessionAsync();

        // Act
        var failure = await _host.ExpectFailureAsAsync(
            JoinSessionCommand.RequestName,
            Body(session, "Luca"),
            CodiUseCaseHost.JoinContext(admissionScope: "  ", idempotencyKey: "chiave-di-prova"));

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
        Assert.Equal(0L, await ScalarAsync<long>("SELECT COUNT(*) FROM session_participant;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_SenzaChiaveDiIdempotenza_RifiutaConDatiNonValidi()
    {
        // Arrange
        var session = await StartedSessionAsync();

        // Act
        var failure = await _host.ExpectFailureAsAsync(
            JoinSessionCommand.RequestName,
            Body(session, "Luca"),
            CodiUseCaseHost.JoinContext(admissionScope: "scope-di-prova", idempotencyKey: string.Empty));

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConNomeNonUtilizzabile_RifiutaConNomeNonValido()
    {
        // Arrange: il limite è di trenta grafemi (§7.4).
        var session = await StartedSessionAsync();

        // Act
        var failure = await _host.ExpectFailureAsAsync(
            JoinSessionCommand.RequestName,
            Body(session, new string('a', ParticipantName.MaxGraphemes + 1)),
            CodiUseCaseHost.JoinContext("scope-di-prova", "chiave-di-prova"));

        // Assert
        Assert.Equal(CodiErrorCodes.InvalidName, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConSessioneInLobby_RifiutaConStatoDellaSessione()
    {
        // Arrange: la lezione non è ancora iniziata, quindi non c'è nulla in cui entrare (§6.1).
        var session = await PrepareAsync();

        // Act
        var failure = await _host.ExpectFailureAsAsync(
            JoinSessionCommand.RequestName,
            Body(session, "Luca"),
            CodiUseCaseHost.JoinContext("scope-di-prova", "chiave-di-prova"));

        // Assert
        Assert.Equal(CodiErrorCodes.SessionStateConflict, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConIngressiChiusi_RifiutaConIngressiChiusi()
    {
        // Arrange: la sessione è avviata ma non accetta nuovi ingressi (§6.1).
        var session = await StartedSessionAsync();
        await ExecuteSqlAsync($"UPDATE lesson_session SET admissions_open = 0 WHERE id = '{session.SessionId:D}';");

        // Act
        var failure = await _host.ExpectFailureAsAsync(
            JoinSessionCommand.RequestName,
            Body(session, "Luca"),
            CodiUseCaseHost.JoinContext("scope-di-prova", "chiave-di-prova"));

        // Assert
        Assert.Equal(CodiErrorCodes.AdmissionsClosed, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConSessioneTerminata_RifiutaConStatoDellaSessione()
    {
        // Arrange
        var session = await StartedSessionAsync();
        await _host.CommandAsAsync<SessionDto>(
            EndSessionCommand.RequestName,
            new { sessionId = session.SessionId, expectedSessionRevision = 2L },
            RegiaContext());

        // Act
        var failure = await _host.ExpectFailureAsAsync(
            JoinSessionCommand.RequestName,
            Body(session, "Luca"),
            CodiUseCaseHost.JoinContext("scope-di-prova", "chiave-di-prova"));

        // Assert
        Assert.Equal(CodiErrorCodes.SessionStateConflict, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConClasseDiversaDaQuellaDellaSessione_RifiutaConRisorsaAssente()
    {
        // Arrange: la selezione del client non si sostituisce a quella della sessione (§9.4).
        var session = await StartedSessionAsync();
        var other = await CreateClassroomAsync(session.SchoolId, "Terza B");

        // Act
        var failure = await _host.ExpectFailureAsAsync(
            JoinSessionCommand.RequestName,
            new
            {
                schoolId = session.SchoolId,
                classroomId = other,
                sessionId = session.SessionId,
                displayName = "Luca",
            },
            CodiUseCaseHost.JoinContext("scope-di-prova", "chiave-di-prova"));

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConScuolaDiversaDaQuellaDellaClasse_RifiutaConRisorsaAssente()
    {
        // Arrange
        var session = await StartedSessionAsync();

        // Act
        var failure = await _host.ExpectFailureAsAsync(
            JoinSessionCommand.RequestName,
            new
            {
                schoolId = Guid.NewGuid(),
                classroomId = session.ClassroomId,
                sessionId = session.SessionId,
                displayName = "Luca",
            },
            CodiUseCaseHost.JoinContext("scope-di-prova", "chiave-di-prova"));

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConChiaveDellaPostazione_AssociaLaPostazioneDelBrowser()
    {
        // Arrange: due alunni che entrano dallo stesso PC devono risultare sulla stessa postazione.
        var session = await StartedSessionAsync();
        var key = "chiave-casuale-del-browser";

        // Act
        var first = await JoinAsync(session, "Luca", key);
        var second = await JoinAsync(session, "Anna", key, idempotencyKey: "chiave-di-prova-2");

        // Assert
        var firstStation = await ScalarAsync<string>(
            $"SELECT station_id FROM session_participant WHERE id = '{first.Joined.ParticipantId:D}';");
        var secondStation = await ScalarAsync<string>(
            $"SELECT station_id FROM session_participant WHERE id = '{second.Joined.ParticipantId:D}';");

        Assert.Equal(firstStation, secondStation);
        Assert.Equal(1L, await ScalarAsync<long>("SELECT COUNT(*) FROM station;"));
        Assert.Equal(
            session.ClassroomId.ToString("D"),
            await ScalarAsync<string>($"SELECT classroom_id FROM station WHERE id = '{firstStation}';"));

        // La chiave in chiaro non entra nel registro: si conserva la sua impronta (§7.2).
        Assert.DoesNotContain(
            key,
            await ScalarAsync<string>("SELECT browser_key_hash FROM station;"),
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConChiaveDellaPostazioneNonCasuale_RifiutaConDatiNonValidi()
    {
        // Arrange: la chiave identifica il PC, quindi non può essere un testo qualunque.
        var session = await StartedSessionAsync();

        // Act
        var failure = await _host.ExpectFailureAsAsync(
            JoinSessionCommand.RequestName,
            Body(session, "Luca", browserKey: "corta"),
            CodiUseCaseHost.JoinContext("scope-di-prova", "chiave-di-prova"));

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConDefaultDiClasseDiverso_EntraNellaVistaDelDefault()
    {
        // Arrange: il default di classe è ciò che un nuovo ingresso riceve (§6.5). Qui dichiara la
        // vista dell'esercizio, come se il docente avesse già assegnato un'attività alla classe.
        var session = await StartedSessionAsync();
        await ExecuteSqlAsync(
            $"UPDATE lesson_session SET class_default_json = '{{\"view\":\"Exercise\"}}' WHERE id = '{session.SessionId:D}';");

        // Act
        var outcome = await JoinAsync(session, "Luca");

        // Assert
        Assert.Equal(nameof(ParticipantView.Exercise), outcome.Joined.View);
        Assert.Equal(
            "Exercise",
            await ScalarAsync<string>(
                $"SELECT view FROM participant_state WHERE participant_id = '{outcome.Joined.ParticipantId:D}';"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConDefaultDiClasseIlleggibile_NonInventaLaVista()
    {
        // Arrange: il documento lo scrive il server, quindi un valore illeggibile è un difetto
        // interno: l'ingresso non deve riuscire con una vista inventata. Il valore è JSON valido
        // — la colonna lo impone — ma dichiara una vista che non esiste.
        var session = await StartedSessionAsync();
        await ExecuteSqlAsync(
            $"UPDATE lesson_session SET class_default_json = '{{\"view\":\"Attesa\"}}' WHERE id = '{session.SessionId:D}';");

        // Act / Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => JoinAsync(session, "Luca"));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT COUNT(*) FROM session_participant;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_RipetutoDopoLaChiusuraDegliIngressi_RecuperaLaPartecipazione()
    {
        // Arrange: la risposta si è persa e nel frattempo gli ingressi si sono chiusi. L'alunno è
        // dentro: negargli l'esito registrato sarebbe dirgli che non è entrato mentre è in classe.
        var session = await StartedSessionAsync();
        var first = await JoinAsync(session, "Luca");
        await ExecuteSqlAsync($"UPDATE lesson_session SET admissions_open = 0 WHERE id = '{session.SessionId:D}';");

        // Act
        var replay = await JoinAsync(session, "Luca");

        // Assert
        Assert.Equal(first.Joined.ParticipantId, replay.Joined.ParticipantId);
        Assert.Equal(1L, await ScalarAsync<long>("SELECT COUNT(*) FROM session_participant;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConScopeDiversoDopoLaChiusura_RifiutaConIngressiChiusi()
    {
        // Arrange: un ingresso nuovo, invece, non può entrare a ingressi chiusi.
        var session = await StartedSessionAsync();
        await JoinAsync(session, "Luca");
        await ExecuteSqlAsync($"UPDATE lesson_session SET admissions_open = 0 WHERE id = '{session.SessionId:D}';");

        // Act
        var failure = await _host.ExpectFailureAsAsync(
            JoinSessionCommand.RequestName,
            Body(session, "Anna"),
            CodiUseCaseHost.JoinContext("altro-scope", "chiave-di-prova"));

        // Assert
        Assert.Equal(CodiErrorCodes.AdmissionsClosed, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task QueryAsync_GetParticipantContext_ConPartecipante_RestituisceIlContesto()
    {
        // Arrange
        var session = await StartedSessionAsync();
        var outcome = await JoinAsync(session, "Luca");
        var context = CodiUseCaseHost.ParticipantContext(
            outcome.Joined.CredentialSessionId, outcome.Joined.ParticipantId);

        // Act
        var participant = await _host.QueryOneAsAsync<ParticipantContextDto>(
            GetParticipantContextQuery.RequestName, new { }, context);

        // Assert
        Assert.Equal(outcome.Joined.ParticipantId, participant.ParticipantId);
        Assert.Equal(session.SessionId, participant.SessionId);
        Assert.Equal("Luca", participant.DisplayName);
        Assert.Equal("Admitted", participant.AdmissionStatus);
        Assert.Equal(nameof(ParticipantView.Waiting), participant.View);
        Assert.Equal(nameof(SessionStatus.Active), participant.SessionStatus);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task QueryAsync_GetParticipantContext_SenzaIdentita_RifiutaConAccessoRichiesto()
    {
        // Act
        var failure = await Assert.ThrowsAsync<CodiRequestException>(() => _host.ExecuteAsAsync(
            CodiRequestKind.Query,
            GetParticipantContextQuery.RequestName,
            new { },
            CodiUseCaseHost.JoinContext("scope-di-prova", "chiave-di-prova")));

        // Assert
        Assert.Equal(CodiErrorCodes.AuthenticationRequired, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task QueryAsync_GetParticipantContext_ConPartecipanteInesistente_RifiutaConRisorsaAssente()
    {
        // Arrange: una credenziale valida che riferisce una partecipazione rimossa è un 404, non un
        // contesto inventato.
        var context = CodiUseCaseHost.ParticipantContext(Guid.NewGuid(), Guid.NewGuid());

        // Act
        var failure = await Assert.ThrowsAsync<CodiRequestException>(() => _host.ExecuteAsAsync(
            CodiRequestKind.Query, GetParticipantContextQuery.RequestName, new { }, context));

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_RipetutoConCredenzialeRevocata_NonRiapreLaPartecipazione()
    {
        // Arrange: la credenziale del primo ingresso è stata revocata — è una decisione della
        // regia, non una risposta persa. Il registro conserva l'esito, ma ripetere l'ingresso non
        // riapre la credenziale: la revoca non si annulla con una ripetizione (§9.2, §9.4).
        var session = await StartedSessionAsync();
        var first = await JoinAsync(session, "Luca");
        await ExecuteSqlAsync(
            $"UPDATE credential_session SET revoked_at_utc_ms = {CodiUseCaseHost.Now.ToUnixTimeMilliseconds()} " +
            $"WHERE id = '{first.Joined.CredentialSessionId:D}';");

        // Act
        var failure = await _host.ExpectFailureAsAsync(
            JoinSessionCommand.RequestName,
            Body(session, "Luca"),
            CodiUseCaseHost.JoinContext("scope-di-prova", "chiave-di-prova"));

        // Assert
        Assert.Equal(CodiErrorCodes.SessionStateConflict, failure.Code);
        Assert.Equal(1L, await ScalarAsync<long>("SELECT COUNT(*) FROM session_participant;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_UscitaVolontaria_RevocaLaCredenzialeSenzaCancellareLaPartecipazione()
    {
        // Arrange
        var session = await StartedSessionAsync();
        var luca = await JoinAsync(session, "Luca");
        var context = CodiUseCaseHost.ParticipantContext(
            luca.Joined.CredentialSessionId, luca.Joined.ParticipantId);

        // Act
        var left = await _host.CommandAsAsync<bool>(LeaveSessionCommand.RequestName, new { }, context);

        // Assert: la credenziale è revocata e la partecipazione resta, con il suo lavoro (§9.2, §10.3).
        Assert.True(left);
        Assert.Equal(
            1L,
            await ScalarAsync<long>(
                $"SELECT COUNT(*) FROM credential_session WHERE id = '{luca.Joined.CredentialSessionId:D}' AND revoked_at_utc_ms IS NOT NULL;"));
        Assert.Equal(1L, await ScalarAsync<long>("SELECT COUNT(*) FROM session_participant;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_UscitaRipetutaConLaStessaCredenziale_NonFaNulla()
    {
        // Arrange: la revoca è già registrata, quindi ripetere l'uscita non è un errore ma non fa nulla.
        var session = await StartedSessionAsync();
        var luca = await JoinAsync(session, "Luca");
        var context = CodiUseCaseHost.ParticipantContext(
            luca.Joined.CredentialSessionId, luca.Joined.ParticipantId);
        Assert.True(await _host.CommandAsAsync<bool>(LeaveSessionCommand.RequestName, new { }, context));

        // Act
        var again = await _host.CommandAsAsync<bool>(LeaveSessionCommand.RequestName, new { }, context);

        // Assert
        Assert.False(again);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_UscitaConCredenzialeDiUnAltroPartecipante_RifiutaConAccessoRichiesto()
    {
        // Arrange: una credenziale valida attribuita a un altro partecipante è un contesto
        // incoerente: l'uscita è un'azione di chi è dentro, non un modo per uscire al posto altrui.
        var session = await StartedSessionAsync();
        var luca = await JoinAsync(session, "Luca");
        var anna = await JoinAsync(session, "Anna", idempotencyKey: "chiave-ingresso-anna");
        var context = CodiUseCaseHost.ParticipantContext(
            luca.Joined.CredentialSessionId, anna.Joined.ParticipantId);

        // Act
        var failure = await _host.ExpectFailureAsAsync(LeaveSessionCommand.RequestName, new { }, context);

        // Assert
        Assert.Equal(CodiErrorCodes.AuthenticationRequired, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_UscitaSenzaIdentitaNelContesto_RifiutaConAccessoRichiesto()
    {
        // Arrange: una concessione di partecipante senza gli identificativi della credenziale non
        // basta a eseguire un'uscita: manca ciò che identifica chi esce.
        var context = new CodiRequestContext(isAuthenticated: true, [CodiPolicy.AdmittedParticipant]);

        // Act
        var failure = await _host.ExpectFailureAsAsync(LeaveSessionCommand.RequestName, new { }, context);

        // Assert
        Assert.Equal(CodiErrorCodes.AuthenticationRequired, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConClasseArchiviata_RifiutaConRisorsaAssente()
    {
        // Arrange: la classe della sessione è stata archiviata dopo l'avvio. La selezione del
        // client non deve rivelare quali identificativi sono validi, e una classe archiviata è
        // per l'ingresso come una classe inesistente (§9.4).
        var session = await StartedSessionAsync();
        await ArchiveAsync(ArchiveClassroomCommand.RequestName, session.ClassroomId);

        // Act / Assert
        await ExpectResourceNotFoundAsync(session);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConScuolaArchiviata_RifiutaConRisorsaAssente()
    {
        // Arrange: la classe è attiva, la scuola no.
        var session = await StartedSessionAsync();
        await ArchiveAsync(ArchiveSchoolCommand.RequestName, session.SchoolId);

        // Act / Assert
        await ExpectResourceNotFoundAsync(session);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConLezioneArchiviata_RifiutaConRisorsaAssente()
    {
        // Arrange: lezione e corso sono la testa della catena: archiviare la lezione toglie
        // disponibilità all'attività senza toccare classe e scuola.
        var session = await StartedSessionAsync();
        await ArchiveAsync(
            ArchiveLessonCommand.RequestName,
            Guid.Parse(await ScalarAsync<string>(
                $"SELECT lesson_id FROM lesson_session WHERE id = '{session.SessionId:D}';")));

        // Act / Assert
        await ExpectResourceNotFoundAsync(session);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConCorsoArchiviato_RifiutaConRisorsaAssente()
    {
        // Arrange: il corso è l'ultimo anello della catena e il suo stato si legge dalla lezione.
        var session = await StartedSessionAsync();
        var lessonId = await ScalarAsync<string>(
            $"SELECT lesson_id FROM lesson_session WHERE id = '{session.SessionId:D}';");
        await ArchiveAsync(
            ArchiveCourseCommand.RequestName,
            Guid.Parse(await ScalarAsync<string>(
                $"SELECT course_id FROM lesson WHERE id = '{lessonId}';")));

        // Act / Assert
        await ExpectResourceNotFoundAsync(session);
    }

    /// <summary>Catena reale, senza avviare la sessione: serve ai casi sulla <c>Lobby</c>.</summary>
    private async Task<Fixture> PrepareAsync(string className = "Terza A")
    {
        var school = await _host.CommandAsAsync<SchoolDto>(
            CreateSchoolCommand.RequestName, new { name = "Scuola Manzoni" }, RegiaContext());
        var classroomId = await CreateClassroomAsync(school.Id, className);
        var course = await _host.CommandAsAsync<CourseDto>(
            CreateCourseCommand.RequestName, new { title = "Informatica di base" }, RegiaContext());
        var lesson = await _host.CommandAsAsync<LessonDto>(
            CreateLessonCommand.RequestName, new { courseId = course.Id, title = "Prima lezione" }, RegiaContext());
        var session = await _host.CommandAsAsync<SessionDto>(
            CreateSessionCommand.RequestName,
            new { lessonId = lesson.Id, classroomId },
            RegiaContext());

        return new Fixture(school.Id, classroomId, session.Id);
    }

    private async Task<Guid> CreateClassroomAsync(Guid schoolId, string className)
    {
        var classroom = await _host.CommandAsAsync<ClassroomDto>(
            CreateClassroomCommand.RequestName,
            new { schoolId, name = className, schoolYear = "2026/2027" },
            RegiaContext());

        return classroom.Id;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConCodiceDiIngresso_CreaLaPartecipazioneSenzaDichiarareLaCatena()
    {
        // Arrange: la classe è in play e il docente ha un codice da dettare.
        var session = await StartedSessionAsync();
        var code = await JoinCodeAsync(session.SessionId);

        // Act: l'alunno dichiara il codice e il nome, nient'altro.
        var outcome = await JoinByCodeAsync(code, "  Luca  ");

        // Assert
        Assert.Equal(session.SessionId, outcome.Joined.SessionId);
        Assert.Equal("Luca", outcome.Joined.DisplayName);
        Assert.Equal(nameof(ParticipantView.Waiting), outcome.Joined.View);
        Assert.NotEqual(Guid.Empty, outcome.Joined.CredentialSessionId);
        Assert.False(string.IsNullOrWhiteSpace(outcome.CredentialStamp));
        Assert.Equal(1L, await ScalarAsync<long>("SELECT COUNT(*) FROM session_participant;"));
        Assert.Equal(1L, await ScalarAsync<long>("SELECT COUNT(*) FROM idempotency_record;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConCodiceDiUnaLezioneInPausa_AmmessoConLaClasseInPausa()
    {
        // Arrange: la pausa sospende l'attività e non l'ingresso (§6.1), quindi il codice già
        // dettato resta spendibile.
        var session = await StartedSessionAsync();
        var code = await JoinCodeAsync(session.SessionId);
        await _host.CommandAsAsync<SessionDto>(
            PauseCommand.RequestName,
            new { sessionId = session.SessionId, expectedSessionRevision = 2L },
            RegiaContext());

        // Act
        var outcome = await JoinByCodeAsync(code, "Luca");

        // Assert
        Assert.Equal(session.SessionId, outcome.Joined.SessionId);
        Assert.Equal(
            1L,
            await ScalarAsync<long>(
                $"SELECT teacher_paused FROM participant_state WHERE participant_id = '{outcome.Joined.ParticipantId:D}';"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConCodiceErrato_RifiutaSenzaCreareNulla()
    {
        // Arrange
        var session = await StartedSessionAsync();
        var code = await JoinCodeAsync(session.SessionId);

        // Act
        var failure = await ExpectCodeFailureAsync(DifferentCode(code), "Luca");

        // Assert: un codice mai assegnato e uno scritto male hanno lo stesso esito, e nulla è creato.
        Assert.Equal(CodiErrorCodes.InvalidJoinCode, failure.Code);
        Assert.Equal(0L, await ScalarAsync<long>("SELECT COUNT(*) FROM session_participant;"));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT COUNT(*) FROM credential_session;"));
    }

    [Theory]
    [InlineData("1234")]
    [InlineData("123456789")]
    [InlineData("1234567a")]
    [InlineData("01234567")]
    [InlineData("1234 678")]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConCodiceMalformato_RifiutaSenzaCercare(string joinCode)
    {
        // Arrange: un codice malformato non è un codice, e non si cerca nel database.
        await StartedSessionAsync();

        // Act
        var failure = await ExpectCodeFailureAsync(joinCode, "Luca");

        // Assert
        Assert.Equal(CodiErrorCodes.InvalidJoinCode, failure.Code);
        Assert.Equal(0L, await ScalarAsync<long>("SELECT COUNT(*) FROM session_participant;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConCodiceDiUnaLezioneTerminata_RifiutaConConflittoDiStato()
    {
        // Arrange: il codice resta nella riga dopo lo Stop, ma la lezione non è più in play.
        var session = await StartedSessionAsync();
        var code = await JoinCodeAsync(session.SessionId);
        await _host.CommandAsAsync<SessionDto>(
            EndSessionCommand.RequestName,
            new { sessionId = session.SessionId, expectedSessionRevision = 2L },
            RegiaContext());

        // Act
        var failure = await ExpectCodeFailureAsync(code, "Luca");

        // Assert
        Assert.Equal(CodiErrorCodes.SessionStateConflict, failure.Code);
        Assert.Equal(0L, await ScalarAsync<long>("SELECT COUNT(*) FROM session_participant;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConCodiceEIngressiChiusi_RifiutaConIngressiChiusi()
    {
        // Arrange: la lezione è avviata ma non accetta più ingressi.
        var session = await StartedSessionAsync();
        var code = await JoinCodeAsync(session.SessionId);
        await _host.CommandAsAsync<SessionDto>(
            CloseAdmissionsCommand.RequestName,
            new { sessionId = session.SessionId, expectedSessionRevision = 2L },
            RegiaContext());

        // Act
        var failure = await ExpectCodeFailureAsync(code, "Luca");

        // Assert
        Assert.Equal(CodiErrorCodes.AdmissionsClosed, failure.Code);
        Assert.Equal(0L, await ScalarAsync<long>("SELECT COUNT(*) FROM session_participant;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConCodice_RipetutoConLaStessaChiave_RecuperaLaStessaPartecipazione()
    {
        // Arrange: il doppio clic e la risposta persa valgono anche per la forma con codice.
        var session = await StartedSessionAsync();
        var code = await JoinCodeAsync(session.SessionId);
        var first = await JoinByCodeAsync(code, "Luca");

        // Act
        var second = await JoinByCodeAsync(code, "Luca");

        // Assert
        Assert.Equal(first.Joined.ParticipantId, second.Joined.ParticipantId);
        Assert.Equal(first.Joined.CredentialSessionId, second.Joined.CredentialSessionId);
        Assert.Equal(first.CredentialStamp, second.CredentialStamp);
        Assert.Equal(1L, await ScalarAsync<long>("SELECT COUNT(*) FROM session_participant;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConCodice_RipetutoDopoLaFineDellaLezione_RecuperaLIngressoRegistrato()
    {
        // Arrange: la risposta si è persa e nel frattempo il docente ha chiuso la lezione. La
        // ricerca del codice non filtra lo stato proprio per questo: l'ingresso già registrato si
        // recupera, come per la forma a catena.
        var session = await StartedSessionAsync();
        var code = await JoinCodeAsync(session.SessionId);
        var first = await JoinByCodeAsync(code, "Luca");
        await _host.CommandAsAsync<SessionDto>(
            EndSessionCommand.RequestName,
            new { sessionId = session.SessionId, expectedSessionRevision = 2L },
            RegiaContext());

        // Act
        var second = await JoinByCodeAsync(code, "Luca");

        // Assert
        Assert.Equal(first.Joined.ParticipantId, second.Joined.ParticipantId);
        Assert.Equal(1L, await ScalarAsync<long>("SELECT COUNT(*) FROM session_participant;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConLaStessaChiaveDopoUnIngressoConCodice_RifiutaConConflitto()
    {
        // Arrange: la stessa chiave con un'altra forma non è la stessa richiesta (§9.4).
        var session = await StartedSessionAsync();
        var code = await JoinCodeAsync(session.SessionId);
        await JoinByCodeAsync(code, "Luca");

        // Act
        var failure = await _host.ExpectFailureAsAsync(
            JoinSessionCommand.RequestName,
            Body(session, "Luca"),
            CodiUseCaseHost.JoinContext("scope-di-prova", "chiave-di-prova"));

        // Assert
        Assert.Equal(CodiErrorCodes.IdempotencyKeyConflict, failure.Code);
        Assert.Equal(1L, await ScalarAsync<long>("SELECT COUNT(*) FROM session_participant;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConCodiceECatenaInsieme_RifiutaConDatiNonValidi()
    {
        // Arrange: due forme nella stessa richiesta lascerebbero al server la scelta di quale credere.
        var session = await StartedSessionAsync();
        var code = await JoinCodeAsync(session.SessionId);

        // Act
        var failure = await _host.ExpectFailureAsAsync(
            JoinSessionCommand.RequestName,
            new
            {
                schoolId = session.SchoolId,
                classroomId = session.ClassroomId,
                sessionId = session.SessionId,
                joinCode = code,
                displayName = "Luca",
            },
            CodiUseCaseHost.JoinContext("scope-di-prova", "chiave-di-prova"));

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
        Assert.Equal(0L, await ScalarAsync<long>("SELECT COUNT(*) FROM session_participant;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_SenzaCodiceESenzaCatena_RifiutaConDatiNonValidi()
    {
        // Act: senza dichiarare come si entra non si entra.
        var failure = await _host.ExpectFailureAsAsync(
            JoinSessionCommand.RequestName,
            new { displayName = "Luca" },
            CodiUseCaseHost.JoinContext("scope-di-prova", "chiave-di-prova"));

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
        Assert.Equal(0L, await ScalarAsync<long>("SELECT COUNT(*) FROM session_participant;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConCatenaParziale_RifiutaConDatiNonValidi()
    {
        // Arrange: la catena è fatta di tre anelli; uno solo non identifica una sessione.
        var session = await StartedSessionAsync();

        // Act
        var failure = await _host.ExpectFailureAsAsync(
            JoinSessionCommand.RequestName,
            new { classroomId = session.ClassroomId, displayName = "Luca" },
            CodiUseCaseHost.JoinContext("scope-di-prova", "chiave-di-prova"));

        // Assert
        Assert.Equal(CodiErrorCodes.ValidationFailed, failure.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConDieciCodiciErrati_SospendeIlTentativoSuccessivo()
    {
        // Arrange §17.3: dieci codici errati in un minuto fermano il browser per il resto della
        // finestra. I tentativi portano chiavi diverse perché sono tentativi distinti.
        var session = await StartedSessionAsync();
        var code = await JoinCodeAsync(session.SessionId);
        var wrong = DifferentCode(code);

        for (var attempt = 0; attempt < JoinCodeAttemptPolicy.MaximumFailures; attempt++)
        {
            var refusal = await ExpectCodeFailureAsync(wrong, "Luca", idempotencyKey: $"tentativo-{attempt}");
            Assert.Equal(CodiErrorCodes.InvalidJoinCode, refusal.Code);
        }

        // Act: nemmeno il codice giusto passa, finché la finestra non scade.
        var blocked = await ExpectCodeFailureAsync(code, "Luca", idempotencyKey: "tentativo-finale");

        // Assert
        Assert.Equal(CodiErrorCodes.TooManyJoinAttempts, blocked.Code);
        Assert.Equal(0L, await ScalarAsync<long>("SELECT COUNT(*) FROM session_participant;"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConIngressoRiuscito_AzzeraIlConteggioDeiTentativiErrati()
    {
        // Arrange: si arriva a un tentativo dal limite, poi l'alunno entra davvero.
        var session = await StartedSessionAsync();
        var code = await JoinCodeAsync(session.SessionId);
        var wrong = DifferentCode(code);

        for (var attempt = 0; attempt < JoinCodeAttemptPolicy.MaximumFailures - 1; attempt++)
        {
            await ExpectCodeFailureAsync(wrong, "Luca", idempotencyKey: $"tentativo-{attempt}");
        }

        await JoinByCodeAsync(code, "Luca", idempotencyKey: "ingresso-riuscito");

        // Act: dopo un ingresso riuscito il conteggio riparte: il blocco non è una condanna.
        var refusal = await ExpectCodeFailureAsync(wrong, "Anna", idempotencyKey: "dopo-il-successo");

        // Assert
        Assert.Equal(CodiErrorCodes.InvalidJoinCode, refusal.Code);
        Assert.Equal(1L, await ScalarAsync<long>("SELECT COUNT(*) FROM session_participant;"));
    }

    /// <summary>Catena reale con la sessione in play: è la condizione perché si possa entrare.</summary>
    private async Task<Fixture> StartedSessionAsync()
    {
        var fixture = await PrepareAsync();
        await _host.CommandAsAsync<SessionDto>(
            StartSessionCommand.RequestName,
            new { sessionId = fixture.SessionId, expectedSessionRevision = 1L },
            RegiaContext());

        return fixture;
    }

    /// <summary>Contesto di regia: la credenziale amministrativa concede anche SessionController (D3).</summary>
    private CodiRequestContext RegiaContext() =>
        new(
            isAuthenticated: true,
            [CodiPolicy.Admin, CodiPolicy.SessionController],
            adminUserId: _adminId);

    /// <summary>Archivia un elemento della catena dal percorso reale, alla prima versione di riga.</summary>
    private async Task ArchiveAsync(string requestName, Guid id) =>
        await _host.ExecuteAsAsync(
            CodiRequestKind.Command, requestName, new { id, baseRowVersion = 1L }, RegiaContext());

    /// <summary>Un ingresso su una catena archiviata è indistinguibile da una catena inesistente.</summary>
    private async Task ExpectResourceNotFoundAsync(Fixture fixture)
    {
        var failure = await _host.ExpectFailureAsAsync(
            JoinSessionCommand.RequestName,
            Body(fixture, "Luca"),
            CodiUseCaseHost.JoinContext("scope-di-prova", "chiave-di-prova"));

        Assert.Equal(CodiErrorCodes.ResourceNotFound, failure.Code);
        Assert.Equal(0L, await ScalarAsync<long>("SELECT COUNT(*) FROM session_participant;"));
        Assert.Equal(0L, await ScalarAsync<long>("SELECT COUNT(*) FROM station;"));
    }

    /// <summary>
    /// Inserisce una riga in <c>admin_user</c>: la composizione dei casi d'uso non registra
    /// l'impronta delle password, ma la chiave esterna della sessione richiede l'amministratore.
    /// </summary>
    private async Task<Guid> InsertAdminUserAsync()
    {
        var id = Guid.NewGuid();

        await using var connection = await _host.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO admin_user (
                id, username, normalized_username, password_hash, security_stamp,
                is_active, failed_login_count, created_at_utc_ms, updated_at_utc_ms)
            VALUES ($id, 'Maestra', 'MAESTRA-' || $id, 'hash-di-prova', 'stamp-di-prova', 1, 0, 1000, 1000);
            """;
        AddParameter(command, "$id", id.ToString("D"));

        await command.ExecuteNonQueryAsync();

        return id;
    }

    private Task<JoinSessionOutcome> JoinAsync(
        Fixture fixture,
        string displayName,
        string? browserKey = null,
        string admissionScope = "scope-di-prova",
        string idempotencyKey = "chiave-di-prova") =>
        _host.CommandAsAsync<JoinSessionOutcome>(
            JoinSessionCommand.RequestName,
            Body(fixture, displayName, browserKey),
            CodiUseCaseHost.JoinContext(admissionScope, idempotencyKey));

    private static object Body(Fixture fixture, string displayName, string? browserKey = null) => new
    {
        schoolId = fixture.SchoolId,
        classroomId = fixture.ClassroomId,
        sessionId = fixture.SessionId,
        displayName,
        browserKey,
    };

    /// <summary>Codice di ingresso della sessione, come sta nella riga: è quello che il docente detta.</summary>
    private async Task<string> JoinCodeAsync(Guid sessionId)
    {
        await using var connection = await _host.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT join_code FROM lesson_session WHERE id = $id;";
        AddParameter(command, "$id", sessionId.ToString("D"));

        return (string)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// Un codice ammissibile ma diverso da quello dato: serve a provare il rifiuto di un codice che
    /// non esiste, non di un codice scritto male — quello ha un caso proprio.
    /// </summary>
    private static string DifferentCode(string code) =>
        code[..^1] + (code[^1] == '9' ? '8' : (char)(code[^1] + 1));

    private Task<JoinSessionOutcome> JoinByCodeAsync(
        string joinCode,
        string displayName,
        string admissionScope = "scope-di-prova",
        string idempotencyKey = "chiave-di-prova") =>
        _host.CommandAsAsync<JoinSessionOutcome>(
            JoinSessionCommand.RequestName,
            new { joinCode, displayName },
            CodiUseCaseHost.JoinContext(admissionScope, idempotencyKey));

    private Task<CodiRequestException> ExpectCodeFailureAsync(
        string joinCode,
        string displayName,
        string admissionScope = "scope-di-prova",
        string idempotencyKey = "chiave-di-prova") =>
        _host.ExpectFailureAsAsync(
            JoinSessionCommand.RequestName,
            new { joinCode, displayName },
            CodiUseCaseHost.JoinContext(admissionScope, idempotencyKey));

    /// <summary>Valori della prima riga, nell'ordine delle colonne selezionate.</summary>
    private async Task<object[]> ColumnsAsync(string sql, Guid id)
    {
        await using var connection = await _host.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        AddParameter(command, "$id", id.ToString("D"));

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "La riga attesa non esiste.");

        var values = new object[reader.FieldCount];
        reader.GetValues(values);

        return values;
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = await _host.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();

        return (T)Convert.ChangeType(value!, typeof(T), CultureInfo.InvariantCulture);
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = await _host.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    /// <summary>Scuola, classe e sessione scelte dal client: la catena che l'ingresso deve verificare.</summary>
    private sealed record Fixture(Guid SchoolId, Guid ClassroomId, Guid SessionId);
}
