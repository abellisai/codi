using Mediator;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Domain.Organization;

namespace Qwerty.Codi.Application.Organization;

/// <summary>
/// Crea una scuola (§10.4). La verifica di utilizzabilità del nome precede qualsiasi accesso al
/// database; l'unicità ha l'ultima parola sulla tabella, perché solo l'indice unico sa quale
/// scrittura concorrente sia arrivata prima.
/// </summary>
public sealed class CreateSchoolHandler(IUnitOfWorkContext unitOfWorkContext, TimeProvider timeProvider)
    : IRequestHandler<CreateSchoolCommand, SchoolDto>
{
    public async ValueTask<SchoolDto> Handle(CreateSchoolCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!SchoolName.IsValid(request.Name))
        {
            throw new CodiRequestException(
                CodiErrorCodes.InvalidName,
                "Il nome della scuola è vuoto, troppo lungo o contiene caratteri non ammessi.");
        }

        var school = School.Create(request.Name, timeProvider.GetUtcNow());

        // Una sola scrittura: la transazione non aggiungerebbe garanzie, mentre l'unicità del
        // nome resta affidata all'indice della tabella (§7.2).
        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!await work.Repo<ISchoolRepository>().TryInsertAsync(school, cancellationToken).ConfigureAwait(false))
        {
            throw new CodiRequestException(
                CodiErrorCodes.SchoolAlreadyExists,
                $"Esiste già una scuola con il nome «{school.Name}».");
        }

        return SchoolDto.FromDomain(school);
    }
}

/// <summary>
/// Modifica il nome di una scuola (§10.4). Lettura, verifica del nome già in uso e scrittura
/// condizionata avvengono nella stessa transazione: fra la lettura e l'aggiornamento non si
/// inserisce nessun altro scrittore, quindi il controllo di versione e quello di unicità non
/// possono essere scavalcati da una richiesta concorrente (§8.3).
/// </summary>
public sealed class UpdateSchoolHandler(IUnitOfWorkContext unitOfWorkContext, TimeProvider timeProvider)
    : IRequestHandler<UpdateSchoolCommand, SchoolDto>
{
    public async ValueTask<SchoolDto> Handle(UpdateSchoolCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!SchoolName.IsValid(request.Name))
        {
            throw new CodiRequestException(
                CodiErrorCodes.InvalidName,
                "Il nome della scuola è vuoto, troppo lungo o contiene caratteri non ammessi.");
        }

        if (request.BaseRowVersion is null)
        {
            throw CodiRequestException.VersionRequired(UpdateSchoolCommand.RequestName);
        }

        await using var work = await unitOfWorkContext
            .CreateAsync(useTransaction: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var schools = work.Repo<ISchoolRepository>();
        var stored = await schools.GetAsync(request.Id.ToString("D"), cancellationToken).ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var school = stored.ToDomain();
        if (school.RowVersion != request.BaseRowVersion)
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        if (!school.Rename(request.Name, timeProvider.GetUtcNow()))
        {
            // Il nome visualizzato è lo stesso: non è una modifica, e riscrivere la riga
            // alzerebbe la versione a ogni salvataggio della stessa schermata.
            return SchoolDto.FromDomain(school);
        }

        var duplicated = await schools.ExistsAsync(
            entity => entity.NormalizedName == school.NormalizedName && entity.Id != stored.Id,
            cancellationToken).ConfigureAwait(false);
        if (duplicated)
        {
            throw new CodiRequestException(
                CodiErrorCodes.SchoolAlreadyExists,
                $"Esiste già una scuola con il nome «{school.Name}».");
        }

        var updated = await schools.TryUpdateAsync(school, cancellationToken).ConfigureAwait(false)
            ?? throw CodiRequestException.RowVersionMismatch();

        await work.CommitAsync(cancellationToken).ConfigureAwait(false);

        return SchoolDto.FromDomain(updated);
    }
}

/// <summary>
/// Archivia una scuola (§10.4, §6.1). Archiviare due volte è un esito previsto e non cambia
/// nulla: la richiesta ripetuta restituisce lo stato corrente senza alzare la versione di riga.
/// </summary>
public sealed class ArchiveSchoolHandler(IUnitOfWorkContext unitOfWorkContext, TimeProvider timeProvider)
    : IRequestHandler<ArchiveSchoolCommand, SchoolDto>
{
    public async ValueTask<SchoolDto> Handle(ArchiveSchoolCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.BaseRowVersion is null)
        {
            throw CodiRequestException.VersionRequired(ArchiveSchoolCommand.RequestName);
        }

        await using var work = await unitOfWorkContext
            .CreateAsync(useTransaction: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var schools = work.Repo<ISchoolRepository>();
        var stored = await schools.GetAsync(request.Id.ToString("D"), cancellationToken).ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var school = stored.ToDomain();
        if (school.RowVersion != request.BaseRowVersion)
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        if (!school.Archive(timeProvider.GetUtcNow()))
        {
            return SchoolDto.FromDomain(school);
        }

        var updated = await schools.TryUpdateAsync(school, cancellationToken).ConfigureAwait(false)
            ?? throw CodiRequestException.RowVersionMismatch();

        await work.CommitAsync(cancellationToken).ConfigureAwait(false);

        return SchoolDto.FromDomain(updated);
    }
}
