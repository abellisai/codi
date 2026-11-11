using Mediator;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Common;
using Qwerty.Codi.Domain.Organization;

namespace Qwerty.Codi.Application.Organization;

/// <summary>
/// Crea un corso (§10.4). Una sola scrittura, quindi nessuna transazione: il titolo non è una
/// chiave e non esiste un conflitto da comporre con altre scritture.
/// </summary>
public sealed class CreateCourseHandler(IUnitOfWorkContext unitOfWorkContext, TimeProvider timeProvider)
    : IRequestHandler<CreateCourseCommand, CourseDto>
{
    public async ValueTask<CourseDto> Handle(CreateCourseCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!NameNormalization.IsUsable(request.Title, Course.MaxTitleLength))
        {
            throw new CodiRequestException(
                CodiErrorCodes.InvalidName,
                "Il titolo del corso è vuoto, troppo lungo o contiene caratteri non ammessi.");
        }

        if ((request.Description?.Trim().Length ?? 0) > Course.MaxDescriptionLength)
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed,
                $"La descrizione del corso supera i {Course.MaxDescriptionLength} caratteri.");
        }

        var course = Course.Create(request.Title, request.Description, timeProvider.GetUtcNow());

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await work.Repo<ICourseRepository>()
            .InsertAsync(CourseEntity.FromDomain(course), cancellationToken)
            .ConfigureAwait(false);

        return CourseDto.FromDomain(course);
    }
}

/// <summary>Modifica titolo e descrizione di un corso (§10.4), con controllo di versione della riga.</summary>
public sealed class UpdateCourseHandler(IUnitOfWorkContext unitOfWorkContext, TimeProvider timeProvider)
    : IRequestHandler<UpdateCourseCommand, CourseDto>
{
    public async ValueTask<CourseDto> Handle(UpdateCourseCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!NameNormalization.IsUsable(request.Title, Course.MaxTitleLength))
        {
            throw new CodiRequestException(
                CodiErrorCodes.InvalidName,
                "Il titolo del corso è vuoto, troppo lungo o contiene caratteri non ammessi.");
        }

        if ((request.Description?.Trim().Length ?? 0) > Course.MaxDescriptionLength)
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed,
                $"La descrizione del corso supera i {Course.MaxDescriptionLength} caratteri.");
        }

        if (request.BaseRowVersion is null)
        {
            throw CodiRequestException.VersionRequired(UpdateCourseCommand.RequestName);
        }

        await using var work = await unitOfWorkContext
            .CreateAsync(useTransaction: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var courses = work.Repo<ICourseRepository>();
        var stored = await courses.GetAsync(request.Id.ToString("D"), cancellationToken).ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var course = stored.ToDomain();
        if (course.RowVersion != request.BaseRowVersion)
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        if (!course.Update(request.Title, request.Description, timeProvider.GetUtcNow()))
        {
            return CourseDto.FromDomain(course);
        }

        var row = CourseEntity.FromDomain(course);
        if (!await courses.UpdateAsync(row, cancellationToken).ConfigureAwait(false))
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        await work.CommitAsync(cancellationToken).ConfigureAwait(false);

        return CourseDto.FromDomain(course.WithRowVersion(row.RowVersion));
    }
}

/// <summary>
/// Archivia un corso (§10.4, §6.1). Ripetere l'archiviazione restituisce lo stato corrente senza
/// alzare la versione di riga.
/// </summary>
public sealed class ArchiveCourseHandler(IUnitOfWorkContext unitOfWorkContext, TimeProvider timeProvider)
    : IRequestHandler<ArchiveCourseCommand, CourseDto>
{
    public async ValueTask<CourseDto> Handle(ArchiveCourseCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.BaseRowVersion is null)
        {
            throw CodiRequestException.VersionRequired(ArchiveCourseCommand.RequestName);
        }

        await using var work = await unitOfWorkContext
            .CreateAsync(useTransaction: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var courses = work.Repo<ICourseRepository>();
        var stored = await courses.GetAsync(request.Id.ToString("D"), cancellationToken).ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var course = stored.ToDomain();
        if (course.RowVersion != request.BaseRowVersion)
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        if (!course.Archive(timeProvider.GetUtcNow()))
        {
            return CourseDto.FromDomain(course);
        }

        var row = CourseEntity.FromDomain(course);
        if (!await courses.UpdateAsync(row, cancellationToken).ConfigureAwait(false))
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        await work.CommitAsync(cancellationToken).ConfigureAwait(false);

        return CourseDto.FromDomain(course.WithRowVersion(row.RowVersion));
    }
}
