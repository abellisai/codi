using Qwerty.Codi.Domain.Exercises;

namespace Qwerty.Codi.Application.Exercises;

/// <summary>
/// Verifica il lavoro di un tentativo secondo la versione del motore che lo governa: il progetto a
/// blocchi di un esercizio di percorso, la risposta di un esercizio dichiarativo.
///
/// Il lavoro di un alunno è **un** campo nel database e **due** forme possibili: quale delle due lo
/// riguardi lo dice il tipo dichiarato dalla definizione, non il contenuto ricevuto. Il contenuto
/// viene verificato, non interpretato: un lavoro della forma sbagliata è un rifiuto spiegato, non un
/// tentativo di leggerlo come se fosse l'altra forma.
/// </summary>
public static class ExerciseWorkspaceValidator
{
    /// <summary>
    /// Verifica il lavoro e lo restituisce com'è stato ricevuto.
    /// </summary>
    /// <param name="json">Il lavoro: progetto a blocchi oppure verbale di risposta.</param>
    /// <param name="definition">La definizione della versione pubblicata.</param>
    /// <param name="referenceSolutionJson">
    /// La soluzione privata della versione, quando esiste. La usa il tipo dichiarativo per
    /// riconoscere una risposta corretta; il tipo a blocchi la ignora, perché la sua verifica non ha
    /// bisogno della soluzione.
    /// </param>
    /// <param name="requestName">Nome della richiesta, per il messaggio di un formato rifiutato.</param>
    /// <param name="label">Etichetta del documento con il suo articolo.</param>
    /// <param name="requireEntryPoint">Vero quando il documento deve poter partire da solo.</param>
    public static string Validate(
        string? json,
        ExerciseDefinition definition,
        string requestName,
        string label,
        string? referenceSolutionJson = null,
        bool requireEntryPoint = false)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return string.Equals(definition.ExerciseType, QuizEngine.ExerciseType, StringComparison.Ordinal)
            ? QuizAnswerJson.Validate(
                json, (QuizData)definition.Data, referenceSolutionJson, requestName, label)
            : ExerciseWorkspaceJson.Validate(json!, definition, requestName, label, requireEntryPoint);
    }
}
