namespace Qwerty.Codi.Domain.Sessions;

/// <summary>
/// Ambito di un'assegnazione (§6.5): vale per tutta la classe oppure per un solo destinatario. È lo
/// stesso vocabolario del comando che la crea e della colonna che la conserva: un solo nome per lo
/// stesso concetto.
/// </summary>
public enum AssignmentScope
{
    /// <summary>L'assegnazione raggiunge tutti gli alunni ammessi della sessione.</summary>
    Class,

    /// <summary>L'assegnazione vale per il solo partecipante indicato.</summary>
    Participant,
}

/// <summary>
/// Assegnazione di una versione pubblicata di esercizio a una sessione (§7.3, §6.5). Fissa **la
/// versione**: il materiale consegnato agli alunni non cambia se poi la sequenza della lezione viene
/// modificata, perché l'assegnazione non segue la lezione ma la fotografa.
///
/// È immutabile: una nuova assegnazione è una riga nuova, che lascia intatto il lavoro già svolto sul
/// materiale precedente. Per questo non ha né transizioni né versione di riga.
/// </summary>
public sealed class ActivityAssignment
{
    private ActivityAssignment(
        Guid id,
        Guid sessionId,
        Guid? lessonExerciseId,
        AssignmentScope scope,
        Guid? targetParticipantId,
        Guid exerciseVersionId,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        SessionId = sessionId;
        LessonExerciseId = lessonExerciseId;
        Scope = scope;
        TargetParticipantId = targetParticipantId;
        ExerciseVersionId = exerciseVersionId;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    /// <summary>Sessione a cui l'assegnazione appartiene: non cambia.</summary>
    public Guid SessionId { get; }

    /// <summary>Casella della sequenza da cui è nata; assente per un'assegnazione diretta.</summary>
    public Guid? LessonExerciseId { get; }

    public AssignmentScope Scope { get; }

    /// <summary>Destinatario dell'assegnazione individuale; assente in un'assegnazione di classe.</summary>
    public Guid? TargetParticipantId { get; }

    /// <summary>Versione pubblicata dell'esercizio, fissata qui e mai sostituita.</summary>
    public Guid ExerciseVersionId { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>
    /// Crea l'assegnazione di una versione pubblicata. L'ambito decide se il destinatario esiste: una
    /// assegnazione di classe con un destinatario sarebbe ambigua — non si saprebbe chi raggiunge — e
    /// una individuale senza destinatario non raggiungerebbe nessuno.
    /// </summary>
    public static ActivityAssignment Create(
        Guid sessionId,
        Guid? lessonExerciseId,
        AssignmentScope scope,
        Guid? targetParticipantId,
        Guid exerciseVersionId,
        DateTimeOffset nowUtc)
    {
        EnsureIdentifier(sessionId, nameof(sessionId), "Un'assegnazione appartiene a una sessione.");
        EnsureIdentifier(
            exerciseVersionId,
            nameof(exerciseVersionId),
            "Un'assegnazione fissa la versione pubblicata dell'esercizio.");
        EnsureScopeCoherence(scope, targetParticipantId);
        EnsureUtc(nowUtc, nameof(nowUtc));

        return new ActivityAssignment(
            Guid.NewGuid(),
            sessionId,
            lessonExerciseId,
            scope,
            targetParticipantId,
            exerciseVersionId,
            nowUtc);
    }

    /// <summary>Ricostruisce l'assegnazione da quanto è stato persistito, validando i dati letti.</summary>
    public static ActivityAssignment Restore(
        Guid id,
        Guid sessionId,
        Guid? lessonExerciseId,
        AssignmentScope scope,
        Guid? targetParticipantId,
        Guid exerciseVersionId,
        DateTimeOffset createdAtUtc)
    {
        EnsureIdentifier(id, nameof(id), "L'identificativo dell'assegnazione è obbligatorio.");
        EnsureIdentifier(sessionId, nameof(sessionId), "Un'assegnazione appartiene a una sessione.");
        EnsureIdentifier(
            exerciseVersionId,
            nameof(exerciseVersionId),
            "Un'assegnazione fissa la versione pubblicata dell'esercizio.");

        if (!Enum.IsDefined(scope))
        {
            throw new ArgumentOutOfRangeException(nameof(scope), scope, "Ambito di assegnazione non riconosciuto.");
        }

        EnsureScopeCoherence(scope, targetParticipantId);
        EnsureUtc(createdAtUtc, nameof(createdAtUtc));

        return new ActivityAssignment(
            id,
            sessionId,
            lessonExerciseId,
            scope,
            targetParticipantId,
            exerciseVersionId,
            createdAtUtc);
    }

    /// <summary>Vero se l'assegnazione raggiunge il partecipante indicato (§6.5).</summary>
    public bool Reaches(Guid participantId) =>
        Scope == AssignmentScope.Class || TargetParticipantId == participantId;

    private static void EnsureScopeCoherence(AssignmentScope scope, Guid? targetParticipantId)
    {
        if (scope == AssignmentScope.Participant && targetParticipantId is null)
        {
            throw new ArgumentException(
                "Un'assegnazione individuale deve indicare il proprio destinatario.",
                nameof(targetParticipantId));
        }

        if (scope == AssignmentScope.Class && targetParticipantId is not null)
        {
            throw new ArgumentException(
                "Un'assegnazione di classe non ha un destinatario: raggiunge tutti gli ammessi.",
                nameof(targetParticipantId));
        }
    }

    private static void EnsureIdentifier(Guid value, string parameterName, string message)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(message, parameterName);
        }
    }

    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Gli istanti sono in UTC.", parameterName);
        }
    }
}
