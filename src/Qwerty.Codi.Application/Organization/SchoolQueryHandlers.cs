using Mediator;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;

namespace Qwerty.Codi.Application.Organization;

/// <summary>
/// Elenco delle scuole (§10.5). È una lettura: nessuna transazione e nessuna scrittura. L'ordine
/// è per nome senza distinzione di maiuscole, stabile e indipendente dalla lingua della macchina.
/// </summary>
public sealed class ListSchoolsHandler(IUnitOfWorkContext unitOfWorkContext)
    : IRequestHandler<ListSchoolsQuery, IReadOnlyList<SchoolDto>>
{
    public async ValueTask<IReadOnlyList<SchoolDto>> Handle(
        ListSchoolsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var schools = work.Repo<ISchoolRepository>();
        var rows = request.IncludeArchived
            ? await schools.GetAllAsync(cancellationToken).ConfigureAwait(false)
            : await schools.QueryAsync(entity => entity.IsArchivedFlag == 0, cancellationToken).ConfigureAwait(false);

        return
        [
            .. rows
                .Select(row => SchoolDto.FromDomain(row.ToDomain()))
                .OrderBy(school => school.Name, StringComparer.OrdinalIgnoreCase),
        ];
    }
}

/// <summary>
/// Dettaglio di una scuola (§10.5), comprese quelle archiviate: lo storico resta consultabile.
/// Una scuola inesistente produce <c>404 ResourceNotFound</c>.
/// </summary>
public sealed class GetSchoolHandler(IUnitOfWorkContext unitOfWorkContext)
    : IRequestHandler<GetSchoolQuery, SchoolDto>
{
    public async ValueTask<SchoolDto> Handle(GetSchoolQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var stored = await work.Repo<ISchoolRepository>()
            .GetAsync(request.Id.ToString("D"), cancellationToken)
            .ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        return SchoolDto.FromDomain(stored.ToDomain());
    }
}
