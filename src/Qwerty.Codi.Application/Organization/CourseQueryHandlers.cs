using Mediator;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;

namespace Qwerty.Codi.Application.Organization;

/// <summary>Elenco dei corsi (§10.5), in ordine di titolo senza distinzione di maiuscole.</summary>
public sealed class ListCoursesHandler(IUnitOfWorkContext unitOfWorkContext)
    : IRequestHandler<ListCoursesQuery, IReadOnlyList<CourseDto>>
{
    public async ValueTask<IReadOnlyList<CourseDto>> Handle(
        ListCoursesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var courses = work.Repo<ICourseRepository>();
        var rows = request.IncludeArchived
            ? await courses.GetAllAsync(cancellationToken).ConfigureAwait(false)
            : await courses.QueryAsync(entity => entity.IsArchivedFlag == 0, cancellationToken).ConfigureAwait(false);

        return
        [
            .. rows
                .Select(row => CourseDto.FromDomain(row.ToDomain()))
                .OrderBy(course => course.Title, StringComparer.OrdinalIgnoreCase),
        ];
    }
}

/// <summary>Dettaglio di un corso (§10.5), archiviati compresi.</summary>
public sealed class GetCourseHandler(IUnitOfWorkContext unitOfWorkContext) : IRequestHandler<GetCourseQuery, CourseDto>
{
    public async ValueTask<CourseDto> Handle(GetCourseQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var stored = await work.Repo<ICourseRepository>()
            .GetAsync(request.Id.ToString("D"), cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        return CourseDto.FromDomain(stored.ToDomain());
    }
}
