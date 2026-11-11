using Mediator;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Common;
using Qwerty.Codi.Domain.Organization;

namespace Qwerty.Codi.Application.Organization;

/// <summary>
/// Crea una classe in una scuola (§10.4). Nome e anno si verificano prima di ogni accesso al
/// database; l'unicità della coppia nella scuola la impone l'indice, mentre l'esistenza della
/// scuola si verifica nella stessa transazione dell'inserimento, così una chiave esterna violata
/// non diventa un errore interno.
/// </summary>
public sealed class CreateClassroomHandler(IUnitOfWorkContext unitOfWorkContext, TimeProvider timeProvider)
    : IRequestHandler<CreateClassroomCommand, ClassroomDto>
{
    public async ValueTask<ClassroomDto> Handle(CreateClassroomCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!NameNormalization.IsUsable(request.Name, Classroom.MaxNameLength))
        {
            throw new CodiRequestException(
                CodiErrorCodes.InvalidName,
                "Il nome della classe è vuoto, troppo lungo o contiene caratteri non ammessi.");
        }

        if (!SchoolYearNormalization.IsValid(request.SchoolYear))
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed,
                "L'anno scolastico deve indicare due anni consecutivi, per esempio 2026/2027.");
        }

        var classroom = Classroom.Create(
            request.SchoolId, request.Name, request.SchoolYear, timeProvider.GetUtcNow());

        await using var work = await unitOfWorkContext
            .CreateAsync(useTransaction: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var school = await work.Repo<ISchoolRepository>()
            .GetAsync(request.SchoolId.ToString("D"), cancellationToken)
            .ConfigureAwait(false);
        if (school is null)
        {
            throw CodiRequestException.ResourceNotFound();
        }

        if (!await work.Repo<IClassroomRepository>().TryInsertAsync(classroom, cancellationToken).ConfigureAwait(false))
        {
            throw new CodiRequestException(
                CodiErrorCodes.ClassroomAlreadyExists,
                $"Nella scuola esiste già una classe «{classroom.Name}» per l'anno {classroom.SchoolYear}.");
        }

        await work.CommitAsync(cancellationToken).ConfigureAwait(false);

        return ClassroomDto.FromDomain(classroom);
    }
}

/// <summary>
/// Modifica nome e anno di una classe (§10.4). Lettura, verifica della coppia già in uso e
/// scrittura condizionata stanno nella stessa transazione: fra la lettura e l'aggiornamento non
/// si inserisce nessun altro scrittore, quindi né il controllo di versione né quello di unicità
/// possono essere scavalcati.
/// </summary>
public sealed class UpdateClassroomHandler(IUnitOfWorkContext unitOfWorkContext, TimeProvider timeProvider)
    : IRequestHandler<UpdateClassroomCommand, ClassroomDto>
{
    public async ValueTask<ClassroomDto> Handle(UpdateClassroomCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!NameNormalization.IsUsable(request.Name, Classroom.MaxNameLength))
        {
            throw new CodiRequestException(
                CodiErrorCodes.InvalidName,
                "Il nome della classe è vuoto, troppo lungo o contiene caratteri non ammessi.");
        }

        if (!SchoolYearNormalization.IsValid(request.SchoolYear))
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed,
                "L'anno scolastico deve indicare due anni consecutivi, per esempio 2026/2027.");
        }

        if (request.BaseRowVersion is null)
        {
            throw CodiRequestException.VersionRequired(UpdateClassroomCommand.RequestName);
        }

        await using var work = await unitOfWorkContext
            .CreateAsync(useTransaction: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var classrooms = work.Repo<IClassroomRepository>();
        var stored = await classrooms.GetAsync(request.Id.ToString("D"), cancellationToken).ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var classroom = stored.ToDomain();
        if (classroom.RowVersion != request.BaseRowVersion)
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        if (!classroom.Update(request.Name, request.SchoolYear, timeProvider.GetUtcNow()))
        {
            return ClassroomDto.FromDomain(classroom);
        }

        var duplicated = await classrooms.ExistsAsync(
            entity => entity.SchoolId == stored.SchoolId
                && entity.NormalizedName == classroom.NormalizedName
                && entity.SchoolYear == classroom.SchoolYear
                && entity.Id != stored.Id,
            cancellationToken).ConfigureAwait(false);
        if (duplicated)
        {
            throw new CodiRequestException(
                CodiErrorCodes.ClassroomAlreadyExists,
                $"Nella scuola esiste già una classe «{classroom.Name}» per l'anno {classroom.SchoolYear}.");
        }

        var row = ClassroomEntity.FromDomain(classroom);
        if (!await classrooms.UpdateAsync(row, cancellationToken).ConfigureAwait(false))
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        await work.CommitAsync(cancellationToken).ConfigureAwait(false);

        return ClassroomDto.FromDomain(classroom.WithRowVersion(row.RowVersion));
    }
}

/// <summary>
/// Archivia una classe (§10.4, §6.1). Ripetere l'archiviazione restituisce lo stato corrente
/// senza alzare la versione di riga.
/// </summary>
public sealed class ArchiveClassroomHandler(IUnitOfWorkContext unitOfWorkContext, TimeProvider timeProvider)
    : IRequestHandler<ArchiveClassroomCommand, ClassroomDto>
{
    public async ValueTask<ClassroomDto> Handle(ArchiveClassroomCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.BaseRowVersion is null)
        {
            throw CodiRequestException.VersionRequired(ArchiveClassroomCommand.RequestName);
        }

        await using var work = await unitOfWorkContext
            .CreateAsync(useTransaction: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var classrooms = work.Repo<IClassroomRepository>();
        var stored = await classrooms.GetAsync(request.Id.ToString("D"), cancellationToken).ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var classroom = stored.ToDomain();
        if (classroom.RowVersion != request.BaseRowVersion)
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        if (!classroom.Archive(timeProvider.GetUtcNow()))
        {
            return ClassroomDto.FromDomain(classroom);
        }

        var row = ClassroomEntity.FromDomain(classroom);
        if (!await classrooms.UpdateAsync(row, cancellationToken).ConfigureAwait(false))
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        await work.CommitAsync(cancellationToken).ConfigureAwait(false);

        return ClassroomDto.FromDomain(classroom.WithRowVersion(row.RowVersion));
    }
}
