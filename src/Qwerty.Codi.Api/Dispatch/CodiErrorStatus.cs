using Qwerty.Codi.Application.Dispatch;

namespace Qwerty.Codi.Api.Dispatch;

/// <summary>
/// Traduzione dei codici applicativi di §10.7 nello stato HTTP corrispondente. Sta in un solo
/// posto perché il codice è il contratto e lo stato è la sua rappresentazione sul trasporto.
/// </summary>
internal static class CodiErrorStatus
{
    /// <summary>
    /// Stato HTTP del codice applicativo. Un codice non mappato è un errore di programmazione:
    /// la risposta resta un errore, non un successo con corpo inatteso.
    /// </summary>
    public static int Resolve(string code) => code switch
    {
        CodiErrorCodes.RowVersionMismatch => StatusCodes.Status412PreconditionFailed,
        CodiErrorCodes.InvalidPayload => StatusCodes.Status400BadRequest,
        CodiErrorCodes.InvalidName => StatusCodes.Status400BadRequest,
        CodiErrorCodes.ValidationFailed => StatusCodes.Status400BadRequest,
        CodiErrorCodes.EntityVersionRequired => StatusCodes.Status428PreconditionRequired,
        CodiErrorCodes.ResourceNotFound => StatusCodes.Status404NotFound,
        CodiErrorCodes.SchoolAlreadyExists => StatusCodes.Status409Conflict,
        CodiErrorCodes.ClassroomAlreadyExists => StatusCodes.Status409Conflict,
        CodiErrorCodes.SessionAlreadyOpen => StatusCodes.Status409Conflict,
        CodiErrorCodes.SessionRevisionConflict => StatusCodes.Status409Conflict,
        CodiErrorCodes.SessionStateConflict => StatusCodes.Status409Conflict,
        CodiErrorCodes.AdmissionsClosed => StatusCodes.Status409Conflict,
        CodiErrorCodes.IdempotencyKeyConflict => StatusCodes.Status409Conflict,
        CodiErrorCodes.AntiforgeryRejected => StatusCodes.Status400BadRequest,
        CodiErrorCodes.PayloadTooLarge => StatusCodes.Status413PayloadTooLarge,
        CodiErrorCodes.StorageUnavailable => StatusCodes.Status503ServiceUnavailable,
        CodiErrorCodes.AuthenticationRequired => StatusCodes.Status401Unauthorized,
        CodiErrorCodes.CredentialsRejected => StatusCodes.Status401Unauthorized,
        CodiErrorCodes.PermissionDenied => StatusCodes.Status403Forbidden,
        CodiErrorCodes.CommandNotFound => StatusCodes.Status404NotFound,
        CodiErrorCodes.QueryNotFound => StatusCodes.Status404NotFound,
        CodiErrorCodes.BootstrapNotAllowed => StatusCodes.Status409Conflict,
        CodiErrorCodes.AdminAccountAlreadyExists => StatusCodes.Status409Conflict,
        CodiErrorCodes.AccountLocked => StatusCodes.Status429TooManyRequests,
        CodiErrorCodes.InvalidJoinCode => StatusCodes.Status404NotFound,
        CodiErrorCodes.TooManyJoinAttempts => StatusCodes.Status429TooManyRequests,
        CodiErrorCodes.MessageThreadFull => StatusCodes.Status409Conflict,
        CodiErrorCodes.LatestExerciseVersion => StatusCodes.Status409Conflict,
        CodiErrorCodes.ExerciseVersionInUse => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError,
    };

    /// <summary>Titolo breve del Problem Details, adatto a chi legge la risposta.</summary>
    public static string Title(string code) => code switch
    {
        CodiErrorCodes.RowVersionMismatch => "Modifica concorrente",
        CodiErrorCodes.InvalidPayload => "Corpo della richiesta non valido",
        CodiErrorCodes.InvalidName => "Nome non utilizzabile",
        CodiErrorCodes.ValidationFailed => "Dati non validi",
        CodiErrorCodes.EntityVersionRequired => "Versione della riga obbligatoria",
        CodiErrorCodes.ResourceNotFound => "Risorsa non trovata",
        CodiErrorCodes.SchoolAlreadyExists => "Scuola già esistente",
        CodiErrorCodes.ClassroomAlreadyExists => "Classe già esistente",
        CodiErrorCodes.SessionAlreadyOpen => "Sessione già aperta per la classe",
        CodiErrorCodes.SessionRevisionConflict => "Revisione della sessione superata",
        CodiErrorCodes.SessionStateConflict => "Stato della sessione non compatibile",
        CodiErrorCodes.AdmissionsClosed => "Ingressi chiusi",
        CodiErrorCodes.IdempotencyKeyConflict => "Richiesta ripetuta con contenuto diverso",
        CodiErrorCodes.AntiforgeryRejected => "Token antiforgery mancante o non valido",
        CodiErrorCodes.PayloadTooLarge => "Contenuto troppo grande",
        CodiErrorCodes.StorageUnavailable => "Archivio dei file non disponibile",
        CodiErrorCodes.AuthenticationRequired => "Accesso richiesto",
        CodiErrorCodes.CredentialsRejected => "Credenziali non valide",
        CodiErrorCodes.PermissionDenied => "Permesso mancante",
        CodiErrorCodes.CommandNotFound => "Comando sconosciuto",
        CodiErrorCodes.QueryNotFound => "Query sconosciuta",
        CodiErrorCodes.BootstrapNotAllowed => "Bootstrap non ammesso",
        CodiErrorCodes.AdminAccountAlreadyExists => "Account già esistente",
        CodiErrorCodes.AccountLocked => "Accesso temporaneamente bloccato",
        CodiErrorCodes.InvalidJoinCode => "Codice di ingresso non valido",
        CodiErrorCodes.TooManyJoinAttempts => "Troppi tentativi di codice",
        CodiErrorCodes.MessageThreadFull => "Thread della chat pieno",
        CodiErrorCodes.LatestExerciseVersion => "Ultima versione dell'esercizio",
        CodiErrorCodes.ExerciseVersionInUse => "Versione già usata da una lezione",
        _ => "Errore non classificato",
    };
}
