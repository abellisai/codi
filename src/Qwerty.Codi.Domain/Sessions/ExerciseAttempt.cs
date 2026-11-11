namespace Qwerty.Codi.Domain.Sessions;

/// <summary>
/// Stato del tentativo di un alunno. La specifica chiede la colonna ma non ne enumera i valori: questi
/// sono una scelta locale registrata nella migrazione `0008` e descrivono il ciclo di vita
/// osservabile — pronto e bloccato all'assegnazione, in esecuzione, sospeso, concluso, archiviato da
/// un azzeramento.
/// </summary>
public enum AttemptStatus
{
    /// <summary>Pronto ma non modificabile: è lo stato con cui nasce un tentativo (§6.5, `EnableActivity`).</summary>
    Ready,

    /// <summary>L'alunno può lavorare: l'attività è abilitata e non sospesa.</summary>
    Running,

    /// <summary>Sospeso dalla regia o dal client: conserva progetto e posizione.</summary>
    Paused,

    /// <summary>Concluso: il traguardo è stato raggiunto.</summary>
    Completed,

    /// <summary>Archiviato da un azzeramento: resta nello storico, non si riprende (§10.4, `ResetWork`).</summary>
    Archived,
}

/// <summary>
/// Tentativo di un alunno su un'assegnazione (§7.3): il lavoro vero, con il progetto, la sua impronta
/// e la revisione con cui il client dichiara su quale stato si basa un salvataggio (§15.2). Un
/// azzeramento **archivia** il tentativo e ne crea uno nuovo dal modello iniziale, così il lavoro
/// precedente non sparisce.
///
/// Il numero è progressivo per coppia partecipante/assegnazione e non si riusa: è ciò che rende
/// riconoscibile il tentativo corrente anche dopo un rizzero.
/// </summary>
public sealed class ExerciseAttempt
{
    private ExerciseAttempt(
        Guid id,
        Guid sessionId,
        Guid participantId,
        Guid assignmentId,
        Guid exerciseVersionId,
        long attemptNumber,
        AttemptStatus status,
        string workspaceJson,
        string workspaceHash,
        long projectRevision,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        long rowVersion)
    {
        Id = id;
        SessionId = sessionId;
        ParticipantId = participantId;
        AssignmentId = assignmentId;
        ExerciseVersionId = exerciseVersionId;
        AttemptNumber = attemptNumber;
        Status = status;
        WorkspaceJson = workspaceJson;
        WorkspaceHash = workspaceHash;
        ProjectRevision = projectRevision;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        RowVersion = rowVersion;
    }

    public Guid Id { get; }

    public Guid SessionId { get; }

    public Guid ParticipantId { get; }

    public Guid AssignmentId { get; }

    /// <summary>Versione dell'esercizio su cui il tentativo lavora: quella fissata dall'assegnazione.</summary>
    public Guid ExerciseVersionId { get; }

    /// <summary>Progressivo per coppia partecipante/assegnazione.</summary>
    public long AttemptNumber { get; }

    public AttemptStatus Status { get; private set; }

    /// <summary>Progetto a blocchi, nella forma serializzata del motore.</summary>
    public string WorkspaceJson { get; private set; }

    /// <summary>Impronta del progetto: permette di riconoscere un salvataggio ripetuto o incoerente.</summary>
    public string WorkspaceHash { get; private set; }

    /// <summary>Revisione del progetto dichiarata dal client sull'ultimo salvataggio accettato.</summary>
    public long ProjectRevision { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Versione della riga per la concorrenza ottimistica (§7.1).</summary>
    public long RowVersion { get; }

    /// <summary>
    /// Crea il tentativo di un alunno su un'assegnazione: nasce **pronto e bloccato**, con il modello
    /// iniziale della versione e senza revisioni di progetto, perché nessuno ha ancora salvato nulla.
    /// Abilitarlo è una decisione del docente (`EnableActivity`), non una conseguenza della creazione.
    /// </summary>
    public static ExerciseAttempt Create(
        Guid sessionId,
        Guid participantId,
        Guid assignmentId,
        Guid exerciseVersionId,
        long attemptNumber,
        string initialWorkspaceJson,
        string workspaceHash,
        DateTimeOffset nowUtc)
    {
        EnsureIdentifier(sessionId, nameof(sessionId), "Un tentativo appartiene a una sessione.");
        EnsureIdentifier(participantId, nameof(participantId), "Un tentativo appartiene a un partecipante.");
        EnsureIdentifier(assignmentId, nameof(assignmentId), "Un tentativo nasce da un'assegnazione.");
        EnsureIdentifier(exerciseVersionId, nameof(exerciseVersionId), "Un tentativo lavora su una versione.");
        EnsureAttemptNumber(attemptNumber);
        EnsureWorkspace(initialWorkspaceJson, workspaceHash);
        EnsureUtc(nowUtc, nameof(nowUtc));

        return new ExerciseAttempt(
            Guid.NewGuid(),
            sessionId,
            participantId,
            assignmentId,
            exerciseVersionId,
            attemptNumber,
            AttemptStatus.Ready,
            initialWorkspaceJson,
            workspaceHash,
            projectRevision: 0,
            createdAtUtc: nowUtc,
            updatedAtUtc: nowUtc,
            rowVersion: 1);
    }

    /// <summary>
    /// Archivia il tentativo per un azzeramento (§10.4, `ResetWork`): il lavoro resta nello storico e
    /// non si riprende, mentre il tentativo nuovo nasce dal modello iniziale della versione. Archiviare
    /// due volte lo stesso tentativo è un errore del caso d'uso, non un'operazione ripetibile: la
    /// ripetizione di `ResetWork` la governa la revisione attesa del comando, non l'archiviazione.
    /// </summary>
    public void SaveWorkspace(string workspaceJson, string workspaceHash, long projectRevision,
        DateTimeOffset nowUtc, bool finalizing = false)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));
        EnsureWorkspace(workspaceJson, workspaceHash);
        if (projectRevision <= ProjectRevision || projectRevision < 1)
            throw new ArgumentException("La revisione del progetto deve avanzare.", nameof(projectRevision));
        if (Status == AttemptStatus.Archived && !finalizing)
            throw new InvalidOperationException("Un tentativo archiviato richiede un permesso conclusivo.");
        WorkspaceJson = workspaceJson;
        WorkspaceHash = workspaceHash;
        ProjectRevision = projectRevision;
        UpdatedAtUtc = nowUtc;
    }

    public void Archive(DateTimeOffset nowUtc)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));

        if (Status == AttemptStatus.Archived)
        {
            throw new InvalidOperationException(
                "Il tentativo è già archiviato: il caso d'uso deve riconoscere il lavoro corrente.");
        }

        Status = AttemptStatus.Archived;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Ricostruisce il tentativo da quanto è stato persistito, validando i dati letti.</summary>
    public static ExerciseAttempt Restore(
        Guid id,
        Guid sessionId,
        Guid participantId,
        Guid assignmentId,
        Guid exerciseVersionId,
        long attemptNumber,
        AttemptStatus status,
        string workspaceJson,
        string workspaceHash,
        long projectRevision,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        long rowVersion)
    {
        EnsureIdentifier(id, nameof(id), "L'identificativo del tentativo è obbligatorio.");
        EnsureIdentifier(sessionId, nameof(sessionId), "Un tentativo appartiene a una sessione.");
        EnsureIdentifier(participantId, nameof(participantId), "Un tentativo appartiene a un partecipante.");
        EnsureIdentifier(assignmentId, nameof(assignmentId), "Un tentativo nasce da un'assegnazione.");
        EnsureIdentifier(exerciseVersionId, nameof(exerciseVersionId), "Un tentativo lavora su una versione.");
        EnsureAttemptNumber(attemptNumber);
        EnsureWorkspace(workspaceJson, workspaceHash);

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Stato del tentativo non riconosciuto.");
        }

        if (projectRevision < 0)
        {
            throw new ArgumentException("La revisione del progetto non può essere negativa.", nameof(projectRevision));
        }

        if (rowVersion < 1)
        {
            throw new ArgumentException("La versione di riga deve essere positiva.", nameof(rowVersion));
        }

        EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        EnsureUtc(updatedAtUtc, nameof(updatedAtUtc));

        if (updatedAtUtc < createdAtUtc)
        {
            throw new ArgumentException("L'ultima modifica non può precedere la creazione.", nameof(updatedAtUtc));
        }

        return new ExerciseAttempt(
            id,
            sessionId,
            participantId,
            assignmentId,
            exerciseVersionId,
            attemptNumber,
            status,
            workspaceJson,
            workspaceHash,
            projectRevision,
            createdAtUtc,
            updatedAtUtc,
            rowVersion);
    }

    /// <summary>Copia con la versione di riga assegnata dalla persistenza.</summary>
    public ExerciseAttempt WithRowVersion(long rowVersion)
    {
        if (rowVersion < 1)
        {
            throw new ArgumentException("La versione di riga deve essere positiva.", nameof(rowVersion));
        }

        return new ExerciseAttempt(
            Id,
            SessionId,
            ParticipantId,
            AssignmentId,
            ExerciseVersionId,
            AttemptNumber,
            Status,
            WorkspaceJson,
            WorkspaceHash,
            ProjectRevision,
            CreatedAtUtc,
            UpdatedAtUtc,
            rowVersion);
    }

    private static void EnsureAttemptNumber(long attemptNumber)
    {
        if (attemptNumber < 1)
        {
            throw new ArgumentException("Il numero del tentativo parte da uno.", nameof(attemptNumber));
        }
    }

    private static void EnsureWorkspace(string workspaceJson, string workspaceHash)
    {
        if (string.IsNullOrWhiteSpace(workspaceJson))
        {
            throw new ArgumentException("Il tentativo porta il progetto con cui nasce.", nameof(workspaceJson));
        }

        if (string.IsNullOrWhiteSpace(workspaceHash))
        {
            throw new ArgumentException("Il progetto ha un'impronta calcolabile.", nameof(workspaceHash));
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
