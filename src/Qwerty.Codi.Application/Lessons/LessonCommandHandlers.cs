using Mediator;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Common;
using Qwerty.Codi.Domain.Lessons;

namespace Qwerty.Codi.Application.Lessons;

/// <summary>
/// Crea una lezione nel corso indicato (§10.4). Il corso deve esistere: la verifica sta nella
/// stessa unità di lavoro dell'inserimento, perché una chiave esterna violata diventerebbe un
/// errore interno invece della risposta prevista.
/// </summary>
public sealed class CreateLessonHandler(IUnitOfWorkContext unitOfWorkContext, TimeProvider timeProvider)
    : IRequestHandler<CreateLessonCommand, LessonDto>
{
    public async ValueTask<LessonDto> Handle(CreateLessonCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.CourseId == Guid.Empty)
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed, "Una lezione appartiene a un corso.");
        }

        if (!NameNormalization.IsUsable(request.Title, Lesson.MaxTitleLength))
        {
            throw new CodiRequestException(
                CodiErrorCodes.InvalidName,
                "Il titolo della lezione è vuoto, troppo lungo o contiene caratteri non ammessi.");
        }

        if ((request.Description?.Trim().Length ?? 0) > Lesson.MaxDescriptionLength)
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed,
                $"La descrizione della lezione supera i {Lesson.MaxDescriptionLength} caratteri.");
        }

        var lesson = Lesson.Create(request.CourseId, request.Title, request.Description, timeProvider.GetUtcNow());

        // La verifica del corso e l'inserimento stanno nella stessa transazione: la chiave esterna
        // non deve essere l'unico controllo, perché una violazione diventerebbe un errore interno.
        await using var work = await unitOfWorkContext
            .CreateAsync(useTransaction: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (await work.Repo<ICourseRepository>()
                .GetAsync(request.CourseId.ToString("D"), cancellationToken)
                .ConfigureAwait(false) is null)
        {
            throw CodiRequestException.ResourceNotFound();
        }

        await work.Repo<ILessonRepository>()
            .InsertAsync(LessonEntity.FromDomain(lesson), cancellationToken)
            .ConfigureAwait(false);

        await work.CommitAsync(cancellationToken).ConfigureAwait(false);

        return LessonDto.FromDomain(lesson);
    }
}

/// <summary>Modifica titolo e descrizione di una lezione (§10.4), con controllo di versione della riga.</summary>
public sealed class UpdateLessonHandler(IUnitOfWorkContext unitOfWorkContext, TimeProvider timeProvider)
    : IRequestHandler<UpdateLessonCommand, LessonDto>
{
    public async ValueTask<LessonDto> Handle(UpdateLessonCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!NameNormalization.IsUsable(request.Title, Lesson.MaxTitleLength))
        {
            throw new CodiRequestException(
                CodiErrorCodes.InvalidName,
                "Il titolo della lezione è vuoto, troppo lungo o contiene caratteri non ammessi.");
        }

        if ((request.Description?.Trim().Length ?? 0) > Lesson.MaxDescriptionLength)
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed,
                $"La descrizione della lezione supera i {Lesson.MaxDescriptionLength} caratteri.");
        }

        if (request.BaseRowVersion is null)
        {
            throw CodiRequestException.VersionRequired(UpdateLessonCommand.RequestName);
        }

        await using var work = await unitOfWorkContext
            .CreateAsync(useTransaction: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var lessons = work.Repo<ILessonRepository>();
        var stored = await lessons.GetAsync(request.Id.ToString("D"), cancellationToken).ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var lesson = stored.ToDomain();
        if (lesson.RowVersion != request.BaseRowVersion)
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        if (!lesson.Update(request.Title, request.Description, timeProvider.GetUtcNow()))
        {
            return LessonDto.FromDomain(lesson);
        }

        var row = LessonEntity.FromDomain(lesson);
        if (!await lessons.UpdateAsync(row, cancellationToken).ConfigureAwait(false))
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        await work.CommitAsync(cancellationToken).ConfigureAwait(false);

        return LessonDto.FromDomain(lesson.WithRowVersion(row.RowVersion));
    }
}

/// <summary>
/// Sostituisce la sequenza ordinata degli esercizi di una lezione (§10.4, §7.3).
///
/// La sostituzione avviene in due fasi dentro una sola transazione: prima tutte le righe della
/// lezione passano a una posizione temporanea, poi ricevono i valori definitivi riusando le righe
/// esistenti in ordine di posizione. Serve perché l'indice unico <c>(lesson_id, position)</c> non
/// ammette due righe sullo stesso valore, nemmeno per l'istante in cui una posizione definitiva
/// cambia proprietario, e perché riusare la riga conserva il riferimento che le sessioni di T4
/// terranno verso la riga.
///
/// Le righe in eccesso vengono cancellate: sono figlie tecniche della lezione e non portano
/// storico proprio. Gli esercizi riferiti, invece, non si toccano: restano nella libreria.
/// </summary>
public sealed class ReplaceLessonExercisesHandler(
    IUnitOfWorkContext unitOfWorkContext,
    TimeProvider timeProvider) : IRequestHandler<ReplaceLessonExercisesCommand, IReadOnlyList<LessonExerciseDto>>
{
    public async ValueTask<IReadOnlyList<LessonExerciseDto>> Handle(
        ReplaceLessonExercisesCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.BaseRowVersion is null)
        {
            throw CodiRequestException.VersionRequired(ReplaceLessonExercisesCommand.RequestName);
        }

        if (request.Exercises is null)
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed, "La sequenza degli esercizi è obbligatoria.");
        }

        // Interpretazione prima della transazione: una richiesta incoerente non deve nemmeno
        // aprirla, e il messaggio dice quale casella non va bene.
        var desired = new List<LessonExerciseInput>(request.Exercises.Count);
        foreach (var input in request.Exercises)
        {
            EnsureUsable(input);
            desired.Add(input);
        }

        await using var work = await unitOfWorkContext
            .CreateAsync(useTransaction: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var lessons = work.Repo<ILessonRepository>();
        var stored = await lessons.GetAsync(request.LessonId.ToString("D"), cancellationToken).ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        if (stored.ToDomain().RowVersion != request.BaseRowVersion)
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        // Gli esercizi devono esistere e non essere archiviati: la verifica precede le scritture,
        // così una sequenza con un identificativo sbagliato non lascia la lezione a metà.
        var exercises = work.Repo<IExerciseRepository>();
        foreach (var exerciseId in desired.Select(input => input.ExerciseId).Distinct())
        {
            var storedExercise = await exercises
                .GetAsync(exerciseId.ToString("D"), cancellationToken)
                .ConfigureAwait(false);

            if (storedExercise is null)
            {
                throw CodiRequestException.ResourceNotFound();
            }

            if (storedExercise.ToDomain().IsArchived)
            {
                throw new CodiRequestException(
                    CodiErrorCodes.ValidationFailed,
                    "Un esercizio archiviato non può far parte della sequenza di una lezione.");
            }
        }

        var now = timeProvider.GetUtcNow();
        var rows = work.Repo<ILessonExerciseRepository>();
        var existing = (await rows
                .QueryAsync(entity => entity.LessonId == request.LessonId.ToString("D"), cancellationToken)
                .ConfigureAwait(false))
            .OrderBy(row => row.Position)
            .ToList();

        // Fase uno: le righe esistenti lasciano libere le posizioni definitive. L'entità porta con
        // sé la versione di riga, quindi è la copia che la fase successiva riusa: ricostruirla dal
        // dominio riporterebbe la versione di prima della scrittura.
        var temporary = -1;
        var pool = new List<LessonExerciseEntity>(existing);
        foreach (var row in pool)
        {
            var lessonExercise = row.ToDomain();
            lessonExercise.PlaceAt(temporary--, lessonExercise.ExerciseId, now);
            await UpdateAsync(rows, row, lessonExercise, cancellationToken).ConfigureAwait(false);
        }

        // Fase due: i valori definitivi. Ogni casella cerca **la riga** che mostra già quell'esercizio
        // e la sposta: le posizioni cambiano, il riferimento non cambia. Riscrivere il riferimento
        // sulla riga della posizione lascerebbe la sequenza giusta, ma cambierebbe sotto le mani di
        // un'assegnazione di T4 che punta a quella riga.
        var replaced = new List<LessonExerciseDto>(desired.Count);
        for (var index = 0; index < desired.Count; index++)
        {
            var exerciseId = desired[index].ExerciseId;
            var position = index + 1;
            var match = pool.FindIndex(row => row.ExerciseId == exerciseId.ToString("D"));

            if (match >= 0)
            {
                var row = pool[match];
                var lessonExercise = row.ToDomain();
                lessonExercise.PlaceAt(position, exerciseId, now);
                await UpdateAsync(rows, row, lessonExercise, cancellationToken).ConfigureAwait(false);
                replaced.Add(LessonExerciseDto.FromDomain(lessonExercise.WithRowVersion(row.RowVersion)));
                pool.RemoveAt(match);

                continue;
            }

            var created = LessonExercise.Create(request.LessonId, exerciseId, position, now);

            await rows.InsertAsync(LessonExerciseEntity.FromDomain(created), cancellationToken)
                .ConfigureAwait(false);
            replaced.Add(LessonExerciseDto.FromDomain(created));
        }

        // Le righe che nessuna casella ha reclamato non fanno più parte della sequenza.
        foreach (var leftover in pool)
        {
            if (!await rows.DeleteAsync(leftover, cancellationToken).ConfigureAwait(false))
            {
                throw CodiRequestException.RowVersionMismatch();
            }
        }

        await work.CommitAsync(cancellationToken).ConfigureAwait(false);

        return replaced;
    }

    /// <summary>
    /// Scrive la riga e allinea la copia in memoria alla versione che la persistenza ha assegnato:
    /// è la copia che la fase successiva riusa.
    /// </summary>
    private static async Task UpdateAsync(
        ILessonExerciseRepository rows,
        LessonExerciseEntity row,
        LessonExercise lessonExercise,
        CancellationToken cancellationToken)
    {
        var updated = LessonExerciseEntity.FromDomain(lessonExercise);

        if (!await rows.UpdateAsync(updated, cancellationToken).ConfigureAwait(false))
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        row.ExerciseId = updated.ExerciseId;
        row.Position = updated.Position;
        row.RowVersion = updated.RowVersion;
    }

    private static void EnsureUsable(LessonExerciseInput input)
    {
        if (input is null)
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed, "Una casella della sequenza non è leggibile.");
        }

        if (input.ExerciseId == Guid.Empty)
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed, "Una casella della sequenza deve riferire un esercizio.");
        }
    }
}

/// <summary>
/// Archivia una lezione (§10.4, §6.1). Ripetere l'archiviazione restituisce lo stato corrente
/// senza alzare la versione di riga.
/// </summary>
public sealed class ArchiveLessonHandler(IUnitOfWorkContext unitOfWorkContext, TimeProvider timeProvider)
    : IRequestHandler<ArchiveLessonCommand, LessonDto>
{
    public async ValueTask<LessonDto> Handle(ArchiveLessonCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.BaseRowVersion is null)
        {
            throw CodiRequestException.VersionRequired(ArchiveLessonCommand.RequestName);
        }

        await using var work = await unitOfWorkContext
            .CreateAsync(useTransaction: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var lessons = work.Repo<ILessonRepository>();
        var stored = await lessons.GetAsync(request.Id.ToString("D"), cancellationToken).ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var lesson = stored.ToDomain();
        if (lesson.RowVersion != request.BaseRowVersion)
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        if (!lesson.Archive(timeProvider.GetUtcNow()))
        {
            return LessonDto.FromDomain(lesson);
        }

        var row = LessonEntity.FromDomain(lesson);
        if (!await lessons.UpdateAsync(row, cancellationToken).ConfigureAwait(false))
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        await work.CommitAsync(cancellationToken).ConfigureAwait(false);

        return LessonDto.FromDomain(lesson.WithRowVersion(row.RowVersion));
    }
}
