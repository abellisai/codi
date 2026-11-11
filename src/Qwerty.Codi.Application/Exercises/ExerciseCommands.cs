using Qwerty.Codi.Application.Dto;
using System.Text.Json.Serialization;
using Qwerty.Codi.Application.Dispatch;

namespace Qwerty.Codi.Application.Exercises;

/// <summary>
/// Crea un esercizio (§10.4). Il titolo non è una chiave: due esercizi possono avere lo stesso titolo.
///
/// Il **tipo di attività** è obbligatorio e appartiene all'anagrafica, non alla definizione: quando
/// il docente crea un esercizio la bozza non esiste ancora, e senza il tipo l'editor non saprebbe
/// quali campi mostrare. Il tipo non si cambia dopo la creazione.
/// </summary>
public sealed record CreateExerciseCommand(string Title, string ExerciseType, string? Description = null)
    : ICodiCommand<ExerciseDto>
{
    public static string RequestName => "CreateExercise";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}

/// <summary>Modifica titolo e descrizione di un esercizio (§10.4).</summary>
public sealed record UpdateExerciseCommand(
    Guid Id,
    string Title,
    string? Description = null,
    long? BaseRowVersion = null) : ICodiCommand<ExerciseDto>
{
    public static string RequestName => "UpdateExercise";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}

/// <summary>Archivia un esercizio (§10.4, §6.1): lo storico resta, esce dalla configurazione attiva.</summary>
public sealed record ArchiveExerciseCommand(Guid Id, long? BaseRowVersion = null) : ICodiCommand<ExerciseDto>
{
    public static string RequestName => "ArchiveExercise";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}

/// <summary>
/// Salva la bozza di un esercizio (§10.4): definizione, progetto iniziale e soluzione di
/// riferimento facoltativa. I due documenti viaggiano come stringhe JSON perché sono documenti con
/// una loro versione di schema (§13.2).
///
/// <c>BaseRowVersion</c> è facoltativa nel record perché la sua assenza ha due esiti propri e
/// distinti — la creazione di una bozza che non esiste ancora, oppure <c>428</c> su una bozza che
/// esiste — e non un corpo non deserializzabile.
/// </summary>
public sealed record SaveExerciseDraftCommand(
    Guid ExerciseId,
    string DefinitionJson,
    string WorkspaceJson,
    string? ReferenceSolutionJson = null,
    long? BaseRowVersion = null) : ICodiCommand<ExerciseDraftDto>
{
    public static string RequestName => "SaveExerciseDraft";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}

/// <summary>
/// Verifica un esercizio senza pubblicarlo (§10.4, §13.8). È la prova generale della pubblicazione:
/// riporta in un elenco leggibile ciò che la impedirebbe — definizione, progetto, soluzione di
/// riferimento — e non solleva un errore, perché una bozza incompleta è una condizione di lavoro e
/// non un guasto.
///
/// I documenti viaggiano nel corpo come in <see cref="SaveExerciseDraftCommand"/>: la verifica
/// riguarda ciò che il docente ha appena composto, anche se non è ancora stato salvato.
/// </summary>
public sealed record ValidateExerciseDraftCommand(
    Guid ExerciseId,
    string DefinitionJson,
    string WorkspaceJson,
    string? ReferenceSolutionJson = null) : ICodiCommand<ExerciseDraftValidationDto>
{
    public static string RequestName => "ValidateExerciseDraft";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}

/// <summary>
/// Pubblica una versione immutabile dalla revisione di bozza dichiarata (§10.4, §13.8). La bozza è
/// già sul server: la richiesta ne indica la versione, non ne porta i documenti, così ciò che si
/// pubblica è esattamente ciò che era stato salvato e verificato.
///
/// Ripetere la stessa pubblicazione non crea una seconda versione: l'identità di una pubblicazione
/// è il suo contenuto (definizione, progetto iniziale e soluzione privata), e una versione con lo
/// stesso contenuto viene restituita invece di essere duplicata.
///
/// L'autore è quello della credenziale verificata dal trasporto, mai un identificativo del payload
/// (§9.3): il contesto è associato dall'esecutore prima dell'handler.
/// </summary>
public sealed record PublishExerciseVersionCommand(
    Guid ExerciseId,
    long? BaseRowVersion = null,
    string? AuthorNotes = null) : ICodiCommand<ExerciseVersionDto>, ICodiContextualRequest
{
    public static string RequestName => "PublishExerciseVersion";

    public static CodiPolicy Policy => CodiPolicy.Admin;

    /// <summary>Contesto verificato dal trasporto: non è un campo del contratto JSON (§4.4).</summary>
    [JsonIgnore]
    public CodiRequestContext Context { get; init; } = CodiRequestContext.Anonymous;

    public ICodiRequest WithContext(CodiRequestContext context) => this with { Context = context };
}

/// <summary>
/// Copia una versione pubblicata nella bozza senza modificarla (§10.4): la versione resta quella
/// che era, la bozza riparte da lì. La scrittura segue le stesse regole del salvataggio — crea la
/// bozza se non esiste, altrimenti richiede la versione letta.
/// </summary>
public sealed record CopyExerciseVersionToDraftCommand(
    Guid ExerciseId,
    long VersionNumber,
    long? BaseRowVersion = null) : ICodiCommand<ExerciseDraftDto>
{
    public static string RequestName => "CopyExerciseVersionToDraft";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}

/// <summary>
/// Elimina una versione pubblicata che **non è la più recente** (§10.4, §13.8), con la sua riga
/// compagna privata: soluzione di riferimento e note dell'autore spariscono con lei.
///
/// L'ultima versione resta: è quella che le prossime assegnazioni usano, e per sostituirla se ne
/// pubblica una nuova. Una versione già assegnata a una sessione non si elimina nemmeno, perché
/// il materiale dato agli alunni non cambia sotto i loro occhi: il rifiuto è un conflitto di stato,
/// non un errore di trasporto.
/// </summary>
public sealed record DeleteExerciseVersionCommand(Guid ExerciseId, long VersionNumber)
    : ICodiCommand<ExerciseVersionDeletedDto>
{
    public static string RequestName => "DeleteExerciseVersion";

    public static CodiPolicy Policy => CodiPolicy.Admin;
}
