using Mediator;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;

namespace Qwerty.Codi.Application.Organization;

/// <summary>
/// Classi di una scuola (§10.5). L'ordine è per anno scolastico decrescente e poi per nome: le
/// classi dell'anno in corso vengono prima, che è l'ordine in cui il docente le cerca.
/// </summary>
public sealed class ListClassroomsHandler(IUnitOfWorkContext unitOfWorkContext)
    : IRequestHandler<ListClassroomsQuery, IReadOnlyList<ClassroomDto>>
{
    public async ValueTask<IReadOnlyList<ClassroomDto>> Handle(
        ListClassroomsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var classrooms = work.Repo<IClassroomRepository>();
        var schoolId = request.SchoolId.ToString("D");
        var rows = request.IncludeArchived
            ? await classrooms.QueryAsync(entity => entity.SchoolId == schoolId, cancellationToken).ConfigureAwait(false)
            : await classrooms.QueryAsync(
                entity => entity.SchoolId == schoolId && entity.IsArchivedFlag == 0, cancellationToken).ConfigureAwait(false);

        return
        [
            .. rows
                .Select(row => ClassroomDto.FromDomain(row.ToDomain()))
                .OrderByDescending(classroom => classroom.SchoolYear, StringComparer.Ordinal)
                .ThenBy(classroom => classroom.Name, StringComparer.OrdinalIgnoreCase),
        ];
    }
}

/// <summary>Dettaglio di una classe (§10.5), archiviate comprese: lo storico resta consultabile.</summary>
public sealed class GetClassroomHandler(IUnitOfWorkContext unitOfWorkContext)
    : IRequestHandler<GetClassroomQuery, ClassroomDto>
{
    public async ValueTask<ClassroomDto> Handle(GetClassroomQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var stored = await work.Repo<IClassroomRepository>()
            .GetAsync(request.Id.ToString("D"), cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        return ClassroomDto.FromDomain(stored.ToDomain());
    }
}
