using Mediator;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Elenco delle sessioni (§10.5), dalla più recente: il criterio è l'istante di creazione, che non
/// compare nel DTO ma ordina l'elenco. A parità di istante l'ordine è stabile per identificativo,
/// così due letture non presentano l'elenco in ordine diverso.
/// </summary>
public sealed class ListSessionsHandler(IUnitOfWorkContext unitOfWorkContext)
    : IRequestHandler<ListSessionsQuery, IReadOnlyList<SessionSummaryDto>>
{
    public async ValueTask<IReadOnlyList<SessionSummaryDto>> Handle(
        ListSessionsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var rows = await work.Repo<ILessonSessionRepository>()
            .GetAllAsync(cancellationToken)
            .ConfigureAwait(false);

        var recentFirst = rows
            .OrderByDescending(row => row.CreatedAtUtcMs)
            .ThenByDescending(row => row.Id, StringComparer.Ordinal)
            .ToList();

        return await SessionSummaryProjector
            .ProjectAsync(work, recentFirst, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>
/// Dettaglio di una sessione (§10.5). Lettura senza transazione. Una sessione inesistente o di un
/// altro amministratore produce lo stesso esito — <c>404 ResourceNotFound</c> — senza rivelare
/// quali identificativi esistono: la proprietà della risorsa è un controllo del caso d'uso, non
/// della policy.
/// </summary>
public sealed class GetSessionHandler(IUnitOfWorkContext unitOfWorkContext)
    : IRequestHandler<GetSessionQuery, SessionSummaryDto>
{
    public async ValueTask<SessionSummaryDto> Handle(GetSessionQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.AdminUserId is not { } controllerAdminId)
        {
            throw CodiRequestException.AuthenticationRequired(GetSessionQuery.RequestName);
        }

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var stored = await work.Repo<ILessonSessionRepository>()
            .GetAsync(request.SessionId.ToString("D"), cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        if (!string.Equals(stored.ControllerAdminId, controllerAdminId.ToString("D"), StringComparison.Ordinal))
        {
            throw CodiRequestException.ResourceNotFound();
        }

        var projected = await SessionSummaryProjector
            .ProjectAsync(work, [stored], cancellationToken)
            .ConfigureAwait(false);

        return projected[0];
    }
}

/// <summary>
/// Scuole con ingressi disponibili (§10.3). La selezione «in play» e i filtri di archiviazione
/// stanno qui, non nel trasporto: si leggono le sessioni in play, se ne ricavano le classi non
/// archiviate e da queste le scuole non archiviate. L'ordine è per nome senza distinzione di
/// maiuscole, stabile e indipendente dalla lingua della macchina.
///
/// È una lettura: nessuna transazione e nessuna scrittura. Le giunzioni avvengono in memoria perché
/// i dati di un laboratorio sono minimi e i repository CRUD non esprimono una giunzione in modo più
/// leggibile di così.
/// </summary>
public sealed class ListAvailableSchoolsHandler(IUnitOfWorkContext unitOfWorkContext)
    : IQueryHandler<ListAvailableSchoolsQuery, IReadOnlyList<AvailableSchoolDto>>
{
    public async ValueTask<IReadOnlyList<AvailableSchoolDto>> Handle(
        ListAvailableSchoolsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var inPlay = await work.Repo<ILessonSessionRepository>()
            .QueryAsync(InPlayLessonSession.Predicate(), cancellationToken)
            .ConfigureAwait(false);

        var classroomIds = inPlay
            .Select(row => row.ClassroomId)
            .ToHashSet(StringComparer.Ordinal);

        if (classroomIds.Count == 0)
        {
            return [];
        }

        var classrooms = await work.Repo<IClassroomRepository>()
            .QueryAsync(row => row.IsArchivedFlag == 0, cancellationToken)
            .ConfigureAwait(false);

        var schoolIds = classrooms
            .Where(row => classroomIds.Contains(row.Id))
            .Select(row => row.SchoolId)
            .ToHashSet(StringComparer.Ordinal);

        if (schoolIds.Count == 0)
        {
            return [];
        }

        var schools = await work.Repo<ISchoolRepository>()
            .QueryAsync(row => row.IsArchivedFlag == 0, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. schools
                .Where(row => schoolIds.Contains(row.Id))
                .Select(row => AvailableSchoolDto.FromDomain(row.ToDomain()))
                .OrderBy(school => school.Name, StringComparer.OrdinalIgnoreCase),
        ];
    }
}

/// <summary>
/// Classi accessibili di una scuola (§10.3). Si leggono le sessioni in play, se ne ricavano gli
/// identificativi delle classi e si tengono le classi non archiviate di quella scuola fra quelle
/// con un turno in play. L'ordine è per anno scolastico decrescente e poi per nome, come l'elenco
/// amministrativo di <c>ListClassrooms</c>: le classi dell'anno in corso vengono prima.
///
/// Lettura senza transazione. Una scuola inesistente non è un errore: la query non la verifica, e
/// l'insieme vuoto che ne risulta è indistinguibile da quello di una scuola senza classi in play.
/// </summary>
public sealed class ListAvailableClassroomsHandler(IUnitOfWorkContext unitOfWorkContext)
    : IQueryHandler<ListAvailableClassroomsQuery, IReadOnlyList<AvailableClassroomDto>>
{
    public async ValueTask<IReadOnlyList<AvailableClassroomDto>> Handle(
        ListAvailableClassroomsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var inPlay = await work.Repo<ILessonSessionRepository>()
            .QueryAsync(InPlayLessonSession.Predicate(), cancellationToken)
            .ConfigureAwait(false);

        var classroomIds = inPlay
            .Select(row => row.ClassroomId)
            .ToHashSet(StringComparer.Ordinal);

        if (classroomIds.Count == 0)
        {
            return [];
        }

        var schoolId = request.SchoolId.ToString("D");
        var classrooms = await work.Repo<IClassroomRepository>()
            .QueryAsync(row => row.SchoolId == schoolId && row.IsArchivedFlag == 0, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. classrooms
                .Where(row => classroomIds.Contains(row.Id))
                .Select(row => AvailableClassroomDto.FromDomain(row.ToDomain()))
                .OrderByDescending(classroom => classroom.SchoolYear, StringComparer.Ordinal)
                .ThenBy(classroom => classroom.Name, StringComparer.OrdinalIgnoreCase),
        ];
    }
}

/// <summary>
/// La sessione in play di una classe, con la lezione e il corso (§10.3). La lezione determina il
/// corso: dalla sessione si risale alla lezione e da questa al corso, così la risposta porta titolo
/// del corso, titolo della lezione e gli identificativi utili alla scelta e al successivo join.
///
/// Lettura senza transazione. Una classe inesistente o archiviata e una classe senza sessione in
/// play danno lo stesso esito — <c>404 ResourceNotFound</c> — senza rivelare quali identificativi
/// esistono. In coerenza con l'indice di §7.6 c'è al più una sessione non terminata per classe,
/// quindi la ricerca della sessione in play è unica.
/// </summary>
public sealed class GetClassroomPlaySessionHandler(IUnitOfWorkContext unitOfWorkContext)
    : IQueryHandler<GetClassroomPlaySessionQuery, PlaySessionDto>
{
    public async ValueTask<PlaySessionDto> Handle(
        GetClassroomPlaySessionQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var classroomId = request.ClassroomId.ToString("D");
        var classroomRow = await work.Repo<IClassroomRepository>()
            .GetAsync(classroomId, cancellationToken)
            .ConfigureAwait(false);

        if (classroomRow is null || classroomRow.IsArchivedFlag != 0)
        {
            throw CodiRequestException.ResourceNotFound();
        }

        var classroom = classroomRow.ToDomain();

        var sessionRow = (await work.Repo<ILessonSessionRepository>()
                .QueryAsync(row => row.ClassroomId == classroomId, cancellationToken)
                .ConfigureAwait(false))
            .FirstOrDefault(InPlayLessonSession.Matches);

        if (sessionRow is null)
        {
            throw CodiRequestException.ResourceNotFound();
        }

        var lessonRow = await work.Repo<ILessonRepository>()
            .GetAsync(sessionRow.LessonId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var lesson = lessonRow.ToDomain();

        var courseRow = await work.Repo<ICourseRepository>()
            .GetAsync(lesson.CourseId.ToString("D"), cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var course = courseRow.ToDomain();

        return new PlaySessionDto(
            classroom.SchoolId,
            classroom.Id,
            Guid.Parse(sessionRow.Id),
            lesson.Id,
            course.Id,
            course.Title,
            lesson.Title);
    }
}

/// <summary>
/// Presenza di una lezione in play (§10.3): la home dell'alunno la interroga per sapere se deve
/// chiedere il codice, e quando la risposta è negativa la pagina lo dichiara invece di far digitare
/// un codice che non esiste.
///
/// Si legge con la stessa regola delle altre letture pubbliche — <see cref="InPlayLessonSession"/> —
/// e si risponde con un booleano: nessun identificativo, nessun titolo, nessuna classe. La lettura è
/// senza transazione e non muta nulla.
/// </summary>
public sealed class IsLessonInPlayHandler(IUnitOfWorkContext unitOfWorkContext)
    : IQueryHandler<IsLessonInPlayQuery, LessonInPlayDto>
{
    public async ValueTask<LessonInPlayDto> Handle(
        IsLessonInPlayQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var inPlay = await work.Repo<ILessonSessionRepository>()
            .ExistsAsync(InPlayLessonSession.Predicate(), cancellationToken)
            .ConfigureAwait(false);

        return new LessonInPlayDto(inPlay);
    }
}
