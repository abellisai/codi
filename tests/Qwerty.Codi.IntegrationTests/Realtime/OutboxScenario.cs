using Qwerty.Codi.Application.Dto;
using System.Globalization;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Lessons;
using Qwerty.Codi.Application.Organization;
using Qwerty.Codi.Application.Sessions;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Realtime;

/// <summary>
/// Scena condivisa dei test dell'outbox: una sessione in play con un partecipante ammesso e un
/// comando di regia già registrato con le sue consegne. La usano il dispatcher e il servizio in
/// background; ricopiarla nei due file significherebbe verificare due scene diverse chiamandole
/// con lo stesso nome.
/// </summary>
internal sealed class OutboxScenario : IAsyncDisposable
{
    private readonly CodiUseCaseHost _host;

    private OutboxScenario(CodiUseCaseHost host, Guid adminId)
    {
        _host = host;
        AdminId = adminId;
    }

    /// <summary>Composizione dei casi d'uso, reale: registro, mediator, repository e segnale outbox.</summary>
    public CodiUseCaseHost Host => _host;

    /// <summary>Amministratore proprietario della sessione: la chiave esterna lo richiede.</summary>
    public Guid AdminId { get; }

    public static async Task<OutboxScenario> CreateAsync()
    {
        var host = await CodiUseCaseHost.CreateAsync();

        return new OutboxScenario(host, await InsertAdminUserAsync(host));
    }

    /// <summary>Contesto di regia: la credenziale amministrativa concede anche SessionController (D3).</summary>
    public CodiRequestContext RegiaContext() =>
        new(isAuthenticated: true, [CodiPolicy.Admin, CodiPolicy.SessionController], adminUserId: AdminId);

    /// <summary>Scuola, classe, corso, lezione, sessione istanziata e avviata in play.</summary>
    public async Task<Seed> StartedSessionAsync()
    {
        var school = await _host.CommandAsAsync<SchoolDto>(
            CreateSchoolCommand.RequestName, new { name = "Scuola Manzoni" }, RegiaContext());
        var classroom = await _host.CommandAsAsync<ClassroomDto>(
            CreateClassroomCommand.RequestName,
            new { schoolId = school.Id, name = "Terza A", schoolYear = "2026/2027" },
            RegiaContext());
        var course = await _host.CommandAsAsync<CourseDto>(
            CreateCourseCommand.RequestName, new { title = "Informatica di base" }, RegiaContext());
        var lesson = await _host.CommandAsAsync<LessonDto>(
            CreateLessonCommand.RequestName, new { courseId = course.Id, title = "Prima lezione" }, RegiaContext());
        var session = await _host.CommandAsAsync<SessionDto>(
            CreateSessionCommand.RequestName,
            new { lessonId = lesson.Id, classroomId = classroom.Id },
            RegiaContext());
        await _host.CommandAsAsync<SessionDto>(
            StartSessionCommand.RequestName,
            new { sessionId = session.Id, expectedSessionRevision = 1L },
            RegiaContext());

        return new Seed(school.Id, classroom.Id, session.Id);
    }

    /// <summary>Ingresso di un alunno dal percorso anonimo di §9.4.</summary>
    public Task<JoinSessionOutcome> JoinAsync(
        Seed seed,
        string displayName,
        string idempotencyKey = "chiave-di-prova") =>
        _host.CommandAsAsync<JoinSessionOutcome>(
            JoinSessionCommand.RequestName,
            new
            {
                schoolId = seed.SchoolId,
                classroomId = seed.ClassroomId,
                sessionId = seed.SessionId,
                displayName,
            },
            CodiUseCaseHost.JoinContext("scope-di-prova", idempotencyKey));

    /// <summary>Comando di regia che scrive lo stato desiderato e mette in coda il messaggio.</summary>
    public Task<CommandAcceptedDto> ShowWaitingAsync(Seed seed, long expectedSessionRevision) =>
        _host.CommandAsAsync<CommandAcceptedDto>(
            ShowWaitingCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision },
            RegiaContext());

    /// <summary>Valore scalare di una query di verifica sul database temporaneo.</summary>
    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = await _host.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();

        return (T)Convert.ChangeType(value!, typeof(T), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Prima riga di una query, letta per nome di colonna: i test di consegna verificano poche
    /// colonne alla volta e mappare un'entità intera dichiarerebbe campi che il caso non guarda.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, object?>> RowAsync(string sql)
    {
        await using var connection = await _host.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "La riga attesa non esiste.");

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var index = 0; index < reader.FieldCount; index++)
        {
            var value = await reader.IsDBNullAsync(index) ? null : reader.GetValue(index);
            values[reader.GetName(index)] = value;
        }

        return values;
    }

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    /// <summary>
    /// Inserisce una riga in <c>admin_user</c>: la composizione dei casi d'uso non registra
    /// l'impronta delle password, ma la chiave esterna della sessione richiede l'amministratore.
    /// </summary>
    private static async Task<Guid> InsertAdminUserAsync(CodiUseCaseHost host)
    {
        var id = Guid.NewGuid();

        await using var connection = await host.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO admin_user (
                id, username, normalized_username, password_hash, security_stamp,
                is_active, failed_login_count, created_at_utc_ms, updated_at_utc_ms)
            VALUES ($id, 'Maestra', 'MAESTRA-' || $id, 'hash-di-prova', 'stamp-di-prova', 1, 0, 1000, 1000);
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$id";
        parameter.Value = id.ToString("D");
        command.Parameters.Add(parameter);

        await command.ExecuteNonQueryAsync();

        return id;
    }
}

/// <summary>Catena scelta dal client, creata dal percorso reale.</summary>
internal sealed record Seed(Guid SchoolId, Guid ClassroomId, Guid SessionId);
