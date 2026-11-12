using Qwerty.Codi.Application.Dto;
using System.Globalization;
using Mediator;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Common;
using Qwerty.Codi.Domain.Exercises;

namespace Qwerty.Codi.Application.Exercises;

/// <summary>
/// Crea un esercizio (§10.4). Una sola scrittura, quindi nessuna transazione: il titolo non è una
/// chiave e non esiste un conflitto da comporre con altre scritture.
/// </summary>
public sealed class CreateExerciseHandler(IUnitOfWorkContext unitOfWorkContext, TimeProvider timeProvider)
    : IRequestHandler<CreateExerciseCommand, ExerciseDto>
{
    public async ValueTask<ExerciseDto> Handle(CreateExerciseCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!NameNormalization.IsUsable(request.Title, Exercise.MaxTitleLength))
        {
            throw new CodiRequestException(
                CodiErrorCodes.InvalidName,
                "Il titolo dell'esercizio è vuoto, troppo lungo o contiene caratteri non ammessi.");
        }

        if ((request.Description?.Trim().Length ?? 0) > Exercise.MaxDescriptionLength)
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed,
                $"La descrizione dell'esercizio supera i {Exercise.MaxDescriptionLength} caratteri.");
        }

        // Il tipo si verifica prima di scrivere: un tipo che nessun motore interpreta non deve
        // arrivare all'anagrafica, dove resterebbe come un esercizio che nessuno può svolgere.
        if (ExerciseEngineRegistry.FindType(request.ExerciseType) is null)
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed,
                $"Il tipo di esercizio «{request.ExerciseType}» non è previsto: questa build conosce {string.Join(", ", ExerciseEngineRegistry.KnownExerciseTypes)}.");
        }

        var exercise = Exercise.Create(
            request.ExerciseType, request.Title, request.Description, timeProvider.GetUtcNow());

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await work.Repo<IExerciseRepository>()
            .InsertAsync(ExerciseEntity.FromDomain(exercise), cancellationToken)
            .ConfigureAwait(false);

        return ExerciseDto.FromDomain(exercise);
    }
}

/// <summary>Modifica titolo e descrizione di un esercizio (§10.4), con controllo di versione della riga.</summary>
public sealed class UpdateExerciseHandler(IUnitOfWorkContext unitOfWorkContext, TimeProvider timeProvider)
    : IRequestHandler<UpdateExerciseCommand, ExerciseDto>
{
    public async ValueTask<ExerciseDto> Handle(UpdateExerciseCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!NameNormalization.IsUsable(request.Title, Exercise.MaxTitleLength))
        {
            throw new CodiRequestException(
                CodiErrorCodes.InvalidName,
                "Il titolo dell'esercizio è vuoto, troppo lungo o contiene caratteri non ammessi.");
        }

        if ((request.Description?.Trim().Length ?? 0) > Exercise.MaxDescriptionLength)
        {
            throw new CodiRequestException(
                CodiErrorCodes.ValidationFailed,
                $"La descrizione dell'esercizio supera i {Exercise.MaxDescriptionLength} caratteri.");
        }

        if (request.BaseRowVersion is null)
        {
            throw CodiRequestException.VersionRequired(UpdateExerciseCommand.RequestName);
        }

        await using var work = await unitOfWorkContext
            .CreateAsync(useTransaction: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var exercises = work.Repo<IExerciseRepository>();
        var stored = await exercises.GetAsync(request.Id.ToString("D"), cancellationToken).ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var exercise = stored.ToDomain();
        if (exercise.RowVersion != request.BaseRowVersion)
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        if (!exercise.Update(request.Title, request.Description, timeProvider.GetUtcNow()))
        {
            return ExerciseDto.FromDomain(exercise);
        }

        var row = ExerciseEntity.FromDomain(exercise);
        if (!await exercises.UpdateAsync(row, cancellationToken).ConfigureAwait(false))
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        await work.CommitAsync(cancellationToken).ConfigureAwait(false);

        return ExerciseDto.FromDomain(exercise.WithRowVersion(row.RowVersion));
    }
}

/// <summary>
/// Archivia un esercizio (§10.4, §6.1). Ripetere l'archiviazione restituisce lo stato corrente
/// senza alzare la versione di riga.
/// </summary>
public sealed class ArchiveExerciseHandler(IUnitOfWorkContext unitOfWorkContext, TimeProvider timeProvider)
    : IRequestHandler<ArchiveExerciseCommand, ExerciseDto>
{
    public async ValueTask<ExerciseDto> Handle(ArchiveExerciseCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.BaseRowVersion is null)
        {
            throw CodiRequestException.VersionRequired(ArchiveExerciseCommand.RequestName);
        }

        await using var work = await unitOfWorkContext
            .CreateAsync(useTransaction: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var exercises = work.Repo<IExerciseRepository>();
        var stored = await exercises.GetAsync(request.Id.ToString("D"), cancellationToken).ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        var exercise = stored.ToDomain();
        if (exercise.RowVersion != request.BaseRowVersion)
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        if (!exercise.Archive(timeProvider.GetUtcNow()))
        {
            return ExerciseDto.FromDomain(exercise);
        }

        var row = ExerciseEntity.FromDomain(exercise);
        if (!await exercises.UpdateAsync(row, cancellationToken).ConfigureAwait(false))
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        await work.CommitAsync(cancellationToken).ConfigureAwait(false);

        return ExerciseDto.FromDomain(exercise.WithRowVersion(row.RowVersion));
    }
}

/// <summary>
/// Salva la bozza di un esercizio (§10.4): la crea se non esiste, altrimenti la sostituisce.
/// La definizione è verificata **prima** di scrivere, e il documento memorizzato è quello canonico
/// prodotto dalla verifica e non il testo ricevuto: ciò che è salvato è ciò che è stato validato.
///
/// Un esercizio inesistente è un <c>404</c> e non un errore di chiave esterna: la bozza appartiene
/// a un esercizio, e chiederne una per un identificativo sbagliato non è una scrittura da tentare.
/// </summary>
public sealed class SaveExerciseDraftHandler(IUnitOfWorkContext unitOfWorkContext, TimeProvider timeProvider)
    : IRequestHandler<SaveExerciseDraftCommand, ExerciseDraftDto>
{
    public async ValueTask<ExerciseDraftDto> Handle(
        SaveExerciseDraftCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var definition = ExerciseDefinitionJson.Parse(
            request.DefinitionJson, SaveExerciseDraftCommand.RequestName);
        var definitionJson = ExerciseDefinitionJson.ToJson(definition);
        var workspaceJson = ExerciseWorkspaceValidator.Validate(
            request.WorkspaceJson,
            definition,
            SaveExerciseDraftCommand.RequestName,
            "il progetto a blocchi");
        // La soluzione di riferimento è un lavoro come gli altri e passa dalle stesse regole: una
        // soluzione che il motore non saprebbe eseguire — o una risposta attesa che il motore non
        // saprebbe confrontare — non aiuterebbe il docente a verificare. Il punto di ingresso resta
        // facoltativo qui, perché il docente può mettere via una soluzione ancora incompleta: la
        // pubblicazione lo richiede.
        var referenceSolution = string.IsNullOrWhiteSpace(request.ReferenceSolutionJson)
            ? null
            : ExerciseWorkspaceValidator.Validate(
                request.ReferenceSolutionJson,
                definition,
                SaveExerciseDraftCommand.RequestName,
                "la soluzione di riferimento",
                request.ReferenceSolutionJson);

        await using var work = await unitOfWorkContext
            .CreateAsync(useTransaction: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var exerciseId = request.ExerciseId.ToString("D");
        var exercises = work.Repo<IExerciseRepository>();

        if (await exercises.GetAsync(exerciseId, cancellationToken).ConfigureAwait(false) is null)
        {
            throw CodiRequestException.ResourceNotFound();
        }

        var now = timeProvider.GetUtcNow();
        var drafts = work.Repo<IExerciseDraftRepository>();
        var stored = await drafts.GetAsync(exerciseId, cancellationToken).ConfigureAwait(false);

        if (stored is null)
        {
            if (request.BaseRowVersion is not null)
            {
                // Chi dichiara una versione crede che la bozza esista: non esiste, quindi la sua
                // lettura è superata e ripetere la scrittura su quella versione non ha senso.
                throw CodiRequestException.RowVersionMismatch();
            }

            var created = new ExerciseDraftEntity
            {
                Id = exerciseId,
                DefinitionJson = definitionJson,
                WorkspaceJson = workspaceJson,
                ReferenceSolutionJson = referenceSolution,
                CreatedAtUtcMs = now.ToUnixTimeMilliseconds(),
                UpdatedAtUtcMs = now.ToUnixTimeMilliseconds(),
                RowVersion = 1,
            };

            await drafts.InsertAsync(created, cancellationToken).ConfigureAwait(false);
            await work.CommitAsync(cancellationToken).ConfigureAwait(false);

            return ToDto(created);
        }

        if (request.BaseRowVersion is null)
        {
            throw CodiRequestException.VersionRequired(SaveExerciseDraftCommand.RequestName);
        }

        if (stored.RowVersion != request.BaseRowVersion)
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        stored.DefinitionJson = definitionJson;
        stored.WorkspaceJson = workspaceJson;
        stored.ReferenceSolutionJson = referenceSolution;
        stored.UpdatedAtUtcMs = now.ToUnixTimeMilliseconds();

        // La scrittura riporta la versione nuova sull'entità: il DTO restituito è quello che il
        // chiamante userà come versione attesa al salvataggio successivo.
        if (!await drafts.UpdateAsync(stored, cancellationToken).ConfigureAwait(false))
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        await work.CommitAsync(cancellationToken).ConfigureAwait(false);

        return ToDto(stored);
    }

    private static ExerciseDraftDto ToDto(ExerciseDraftEntity draft) =>
        new(
            Guid.Parse(draft.Id),
            draft.DefinitionJson,
            draft.WorkspaceJson,
            draft.RowVersion,
            DateTimeOffset.FromUnixTimeMilliseconds(draft.UpdatedAtUtcMs));
}

/// <summary>
/// Verifica un esercizio senza pubblicarlo (§10.4, §13.8). Non scrive nulla: riporta l'elenco dei
/// problemi che impedirebbero la pubblicazione, così il docente li legge invece di riceverli uno
/// alla volta come rifiuti. Un documento che non è nemmeno JSON non è un guasto della richiesta ma
/// un problema della bozza, quindi diventa una voce dell'elenco e non un <c>400</c>.
///
/// La verifica richiede il punto di ingresso nella soluzione di riferimento: una soluzione che non
/// parte non permette di verificare l'esercizio, ed è la stessa condizione che la pubblicazione
/// applica.
/// </summary>
public sealed class ValidateExerciseDraftHandler(IUnitOfWorkContext unitOfWorkContext)
    : IRequestHandler<ValidateExerciseDraftCommand, ExerciseDraftValidationDto>
{
    public async ValueTask<ExerciseDraftValidationDto> Handle(
        ValidateExerciseDraftCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var work = await unitOfWorkContext
            .CreateAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (await work.Repo<IExerciseRepository>()
                .GetAsync(request.ExerciseId.ToString("D"), cancellationToken)
                .ConfigureAwait(false) is null)
        {
            throw CodiRequestException.ResourceNotFound();
        }

        var problems = new List<string>();

        ExerciseDefinition definition;
        try
        {
            definition = ExerciseDefinitionJson.Parse(
                request.DefinitionJson, ValidateExerciseDraftCommand.RequestName);
        }
        catch (CodiRequestException failure)
        {
            // Senza una definizione valida non si può verificare nemmeno il progetto: il problema
            // della definizione è l'unico che si possa dire, e dirlo è più utile di un elenco
            // costruito su regole che non si conoscono.
            return new ExerciseDraftValidationDto(false, [failure.Message]);
        }

        Collect(
            () => ExerciseWorkspaceValidator.Validate(
                request.WorkspaceJson,
                definition,
                ValidateExerciseDraftCommand.RequestName,
                "il progetto a blocchi"),
            problems);

        if (!string.IsNullOrWhiteSpace(request.ReferenceSolutionJson))
        {
            Collect(
                () => ExerciseWorkspaceValidator.Validate(
                    request.ReferenceSolutionJson,
                    definition,
                    ValidateExerciseDraftCommand.RequestName,
                    "la soluzione di riferimento",
                    request.ReferenceSolutionJson,
                    requireEntryPoint: true),
                problems);
        }

        return new ExerciseDraftValidationDto(problems.Count == 0, problems);
    }

    /// <summary>Esegue una verifica e raccoglie il messaggio del rifiuto, se c'è.</summary>
    private static void Collect(Action check, List<string> problems)
    {
        try
        {
            check();
        }
        catch (CodiRequestException failure)
        {
            problems.Add(failure.Message);
        }
    }
}

/// <summary>
/// Pubblica una versione immutabile dalla revisione di bozza dichiarata (§10.4, §13.8).
///
/// La bozza è già sul server e viene riletta: la richiesta dichiara la revisione, non porta i
/// documenti, così ciò che si pubblica è ciò che era stato salvato e verificato. La versione
/// porta l'impronta del contenuto e il numero progressivo dell'esercizio; la soluzione privata
/// finisce nella tabella separata e non esce nel DTO.
///
/// Ripetere l'ultima pubblicazione restituisce la stessa versione quando contenuto, soluzione e
/// note coincidono. Ripubblicare un contenuto precedente crea una nuova versione più recente,
/// che le prossime assegnazioni possono selezionare.
/// </summary>
public sealed class PublishExerciseVersionHandler(
    IUnitOfWorkContext unitOfWorkContext,
    TimeProvider timeProvider)
    : IRequestHandler<PublishExerciseVersionCommand, ExerciseVersionDto>
{
    public async ValueTask<ExerciseVersionDto> Handle(
        PublishExerciseVersionCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Context.AdminUserId is not { } publishedBy)
        {
            throw CodiRequestException.AuthenticationRequired(PublishExerciseVersionCommand.RequestName);
        }

        if (request.BaseRowVersion is null)
        {
            throw CodiRequestException.VersionRequired(PublishExerciseVersionCommand.RequestName);
        }

        var authorNotes = string.IsNullOrWhiteSpace(request.AuthorNotes) ? null : request.AuthorNotes.Trim();

        await using var work = await unitOfWorkContext
            .CreateAsync(useTransaction: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var exerciseId = request.ExerciseId.ToString("D");
        if (await work.Repo<IExerciseRepository>()
                .GetAsync(exerciseId, cancellationToken)
                .ConfigureAwait(false) is null)
        {
            throw CodiRequestException.ResourceNotFound();
        }

        var drafts = work.Repo<IExerciseDraftRepository>();
        var draft = await drafts.GetAsync(exerciseId, cancellationToken).ConfigureAwait(false)
            ?? throw CodiRequestException.ResourceNotFound();

        if (draft.RowVersion != request.BaseRowVersion)
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        // La bozza è stata verificata al salvataggio, ma una versione pubblicata non si corregge:
        // ripetere la verifica qui costa poco e impedisce di fissare per sempre un documento che la
        // versione del motore non sa più interpretare.
        var definition = ExerciseDefinitionJson.Parse(
            draft.DefinitionJson, PublishExerciseVersionCommand.RequestName);
        ExerciseWorkspaceValidator.Validate(
            draft.WorkspaceJson,
            definition,
            PublishExerciseVersionCommand.RequestName,
            "il progetto a blocchi");

        if (!string.IsNullOrWhiteSpace(draft.ReferenceSolutionJson))
        {
            ExerciseWorkspaceValidator.Validate(
                draft.ReferenceSolutionJson,
                definition,
                PublishExerciseVersionCommand.RequestName,
                "la soluzione di riferimento",
                draft.ReferenceSolutionJson,
                requireEntryPoint: true);
        }

        var versions = work.Repo<IExerciseVersionRepository>();
        var published = await versions
            .QueryAsync(row => row.ExerciseId == exerciseId, cancellationToken)
            .ConfigureAwait(false);

        var contentHash = ExerciseContentHash.Compute(
            draft.DefinitionJson,
            draft.WorkspaceJson,
            definition.EngineVersion,
            definition.SchemaVersion.ToString(CultureInfo.InvariantCulture));
        // L'impronta della soluzione è separata da quella del contenuto (0013): una modifica alla
        // sola soluzione deve produrre un'impronta diversa, senza toccare l'impronta studente che
        // i tentativi già pubblicati portano con sé.
        var referenceHash = ExerciseContentHash.ComputeReference(draft.ReferenceSolutionJson);

        var privateRows = work.Repo<IExerciseVersionPrivateRepository>();
        // Solo l'ultima versione rappresenta il contenuto disponibile per le prossime assegnazioni.
        foreach (var candidate in published.OrderByDescending(row => row.VersionNumber).Take(1))
        {
            if (!string.Equals(candidate.ContentHash, contentHash, StringComparison.Ordinal))
            {
                continue;
            }

            // Le versioni storiche anteriori alla migrazione 0013 non hanno impronta di riferimento:
            // per loro il confronto resta sul documento privato, come prima della migrazione.
            if (candidate.ReferenceHash is not null
                && !string.Equals(candidate.ReferenceHash, referenceHash, StringComparison.Ordinal))
            {
                continue;
            }

            var stored = await privateRows.GetAsync(candidate.Id, cancellationToken).ConfigureAwait(false);
            if (string.Equals(stored?.ReferenceSolutionJson, draft.ReferenceSolutionJson, StringComparison.Ordinal)
                && string.Equals(stored?.AuthorNotes, authorNotes, StringComparison.Ordinal))
            {
                // Stessa pubblicazione: la versione esiste già e non se ne crea una seconda.
                return ExerciseVersionDto.FromEntity(candidate, stored?.AuthorNotes);
            }
        }

        var version = new ExerciseVersionEntity
        {
            Id = Guid.NewGuid().ToString("D"),
            ExerciseId = exerciseId,
            VersionNumber = published.Count == 0 ? 1 : published.Max(row => row.VersionNumber) + 1,
            DefinitionJson = draft.DefinitionJson,
            InitialWorkspaceJson = draft.WorkspaceJson,
            EngineVersion = definition.EngineVersion,
            SchemaVersion = definition.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            ContentHash = contentHash,
            ReferenceHash = referenceHash,
            PublishedByAdminId = publishedBy.ToString("D"),
            CreatedAtUtcMs = timeProvider.GetUtcNow().ToUnixTimeMilliseconds(),
        };

        await versions.InsertAsync(version, cancellationToken).ConfigureAwait(false);
        await privateRows
            .InsertAsync(
                new ExerciseVersionPrivateEntity
                {
                    Id = version.Id,
                    ReferenceSolutionJson = draft.ReferenceSolutionJson,
                    AuthorNotes = authorNotes,
                },
                cancellationToken)
            .ConfigureAwait(false);

        await work.CommitAsync(cancellationToken).ConfigureAwait(false);

        // Le note escono insieme alla versione appena creata: chi ha appena pubblicato vede
        // confermato ciò che ha scritto senza doverlo rileggere.
        return ExerciseVersionDto.FromEntity(version, authorNotes);
    }
}

/// <summary>
/// Copia una versione pubblicata nella bozza senza modificarla (§10.4). La versione resta quella
/// che era; la bozza riparte dai suoi documenti e la soluzione privata torna con loro, perché
/// senza di essa il docente non potrebbe più provare l'esercizio.
///
/// La scrittura segue le stesse regole del salvataggio: crea la bozza se non esiste, altrimenti
/// richiede la versione letta, e un conflitto non sovrascrive il lavoro di nessuno.
/// </summary>
public sealed class CopyExerciseVersionToDraftHandler(
    IUnitOfWorkContext unitOfWorkContext,
    TimeProvider timeProvider)
    : IRequestHandler<CopyExerciseVersionToDraftCommand, ExerciseDraftDto>
{
    public async ValueTask<ExerciseDraftDto> Handle(
        CopyExerciseVersionToDraftCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var work = await unitOfWorkContext
            .CreateAsync(useTransaction: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var exerciseId = request.ExerciseId.ToString("D");
        if (await work.Repo<IExerciseRepository>()
                .GetAsync(exerciseId, cancellationToken)
                .ConfigureAwait(false) is null)
        {
            throw CodiRequestException.ResourceNotFound();
        }

        var version = (await work.Repo<IExerciseVersionRepository>()
                .QueryAsync(row => row.ExerciseId == exerciseId && row.VersionNumber == request.VersionNumber, cancellationToken)
                .ConfigureAwait(false))
            .SingleOrDefault()
            ?? throw CodiRequestException.ResourceNotFound();

        var privateRow = await work.Repo<IExerciseVersionPrivateRepository>()
            .GetAsync(version.Id, cancellationToken)
            .ConfigureAwait(false);

        var now = timeProvider.GetUtcNow();
        var drafts = work.Repo<IExerciseDraftRepository>();
        var stored = await drafts.GetAsync(exerciseId, cancellationToken).ConfigureAwait(false);

        if (stored is null)
        {
            if (request.BaseRowVersion is not null)
            {
                // Chi dichiara una versione crede che la bozza esista: non esiste, quindi la sua
                // lettura è superata.
                throw CodiRequestException.RowVersionMismatch();
            }

            var created = new ExerciseDraftEntity
            {
                Id = exerciseId,
                DefinitionJson = version.DefinitionJson,
                WorkspaceJson = version.InitialWorkspaceJson,
                ReferenceSolutionJson = privateRow?.ReferenceSolutionJson,
                CreatedAtUtcMs = now.ToUnixTimeMilliseconds(),
                UpdatedAtUtcMs = now.ToUnixTimeMilliseconds(),
                RowVersion = 1,
            };

            await drafts.InsertAsync(created, cancellationToken).ConfigureAwait(false);
            await work.CommitAsync(cancellationToken).ConfigureAwait(false);

            return ToDto(created);
        }

        if (request.BaseRowVersion is null)
        {
            throw CodiRequestException.VersionRequired(CopyExerciseVersionToDraftCommand.RequestName);
        }

        if (stored.RowVersion != request.BaseRowVersion)
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        stored.DefinitionJson = version.DefinitionJson;
        stored.WorkspaceJson = version.InitialWorkspaceJson;
        stored.ReferenceSolutionJson = privateRow?.ReferenceSolutionJson;
        stored.UpdatedAtUtcMs = now.ToUnixTimeMilliseconds();

        if (!await drafts.UpdateAsync(stored, cancellationToken).ConfigureAwait(false))
        {
            throw CodiRequestException.RowVersionMismatch();
        }

        await work.CommitAsync(cancellationToken).ConfigureAwait(false);

        return ToDto(stored);
    }

    private static ExerciseDraftDto ToDto(ExerciseDraftEntity draft) =>
        new(
            Guid.Parse(draft.Id),
            draft.DefinitionJson,
            draft.WorkspaceJson,
            draft.RowVersion,
            DateTimeOffset.FromUnixTimeMilliseconds(draft.UpdatedAtUtcMs));
}

/// <summary>
/// Elimina una versione pubblicata che non è la più recente (§10.4, §13.8). La regola non è
/// formale: l'ultima versione è quella che le prossime assegnazioni usano, quindi toglierla
/// cambierebbe il materiale disponibile invece di ripulire la cronologia; per sostituirla se ne
/// pubblica una nuova, che è il percorso previsto da §13.8.
///
/// Una versione già assegnata non si elimina nemmeno: <c>activity_assignment</c> la riferisce e il
/// lavoro degli alunni è stato costruito su di lei. Il controllo è esplicito perché il vincolo del
/// database produrrebbe un errore di integrità, non un rifiuto comprensibile.
///
/// La riga compagna privata si elimina nella stessa transazione: soluzione di riferimento e note
/// dell'autore non hanno senso senza la versione che completano.
/// </summary>
public sealed class DeleteExerciseVersionHandler(IUnitOfWorkContext unitOfWorkContext)
    : IRequestHandler<DeleteExerciseVersionCommand, ExerciseVersionDeletedDto>
{
    public async ValueTask<ExerciseVersionDeletedDto> Handle(
        DeleteExerciseVersionCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var work = await unitOfWorkContext
            .CreateAsync(useTransaction: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var exerciseId = request.ExerciseId.ToString("D");
        if (await work.Repo<IExerciseRepository>()
                .GetAsync(exerciseId, cancellationToken)
                .ConfigureAwait(false) is null)
        {
            throw CodiRequestException.ResourceNotFound();
        }

        var versions = work.Repo<IExerciseVersionRepository>();
        var published = await versions
            .QueryAsync(row => row.ExerciseId == exerciseId, cancellationToken)
            .ConfigureAwait(false);

        var version = published
            .SingleOrDefault(row => row.VersionNumber == request.VersionNumber)
            ?? throw CodiRequestException.ResourceNotFound();

        if (published.All(row => row.VersionNumber <= version.VersionNumber))
        {
            throw new CodiRequestException(
                CodiErrorCodes.LatestExerciseVersion,
                "L'ultima versione dell'esercizio non si elimina: per sostituirla se ne pubblica una nuova.");
        }

        if (await work.Repo<IActivityAssignmentRepository>()
                .ExistsAsync(row => row.ExerciseVersionId == version.Id, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new CodiRequestException(
                CodiErrorCodes.ExerciseVersionInUse,
                "La versione è già stata assegnata a una lezione: il lavoro degli alunni non cambia sotto i loro occhi.");
        }

        await work.Repo<IExerciseVersionPrivateRepository>()
            .DeleteByVersionAsync(version.Id, cancellationToken)
            .ConfigureAwait(false);

        if (!await versions.DeletePublishedAsync(version, cancellationToken).ConfigureAwait(false))
        {
            // Difensivo, non un caso osservato: la transazione è `BEGIN IMMEDIATE`, quindi il lock
            // di scrittura è già preso quando si leggono le versioni e nessun altro writer si
            // inserisce fra la lettura e la cancellazione. Se la riga sparisse comunque, la
            // risposta onesta è 404 e non un successo senza effetto.
            throw CodiRequestException.ResourceNotFound();
        }

        await work.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new ExerciseVersionDeletedDto(request.ExerciseId, version.VersionNumber);
    }
}
