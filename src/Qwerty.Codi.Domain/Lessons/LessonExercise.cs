namespace Qwerty.Codi.Domain.Lessons;

/// <summary>
/// Esercizio di una lezione (§7.3): una riga ordinata che punta a un esercizio della libreria.
/// La riga non conserva il proprio contenuto: il riferimento è il modo in cui il docente continua
/// a modificare l'esercizio senza riscrivere la lezione.
///
/// Le posizioni reali sono <c>1..N</c>. Una posizione <b>negativa</b> è ammessa e significa
/// «temporanea»: il riordino di §7.3 ne ha bisogno perché l'indice unico <c>(lesson_id, position)</c>
/// non ammette due righe sullo stesso valore, quindi la sostituzione passa da valori che nessuna
/// posizione definitiva può occupare. Lo zero resta vietato perché non è né una posizione reale né
/// una temporanea.
/// </summary>
public sealed class LessonExercise
{
    private LessonExercise(
        Guid id,
        Guid lessonId,
        Guid exerciseId,
        int position,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        long rowVersion)
    {
        Id = id;
        LessonId = lessonId;
        ExerciseId = exerciseId;
        Position = position;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        RowVersion = rowVersion;
    }

    public Guid Id { get; }

    /// <summary>Lezione di appartenenza: immutabile, l'esercizio si sostituisce dentro la sua lezione.</summary>
    public Guid LessonId { get; }

    /// <summary>Esercizio mostrato dalla riga: è il riferimento che il docente continua a modificare.</summary>
    public Guid ExerciseId { get; private set; }

    /// <summary>Posizione nell'ordine della lezione: positiva e definitiva, negativa se temporanea.</summary>
    public int Position { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Versione della riga per la concorrenza ottimistica (§7.1).</summary>
    public long RowVersion { get; }

    /// <summary>Vero se la posizione è temporanea, cioè se la riga è a metà di un riordino.</summary>
    public bool HasTemporaryPosition => Position < 0;

    /// <summary>Crea una riga nella posizione indicata.</summary>
    public static LessonExercise Create(Guid lessonId, Guid exerciseId, int position, DateTimeOffset nowUtc)
    {
        EnsureLesson(lessonId);
        EnsureExercise(exerciseId);
        EnsurePosition(position);

        if (nowUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("L'istante di creazione deve essere in UTC.", nameof(nowUtc));
        }

        return new LessonExercise(
            Guid.NewGuid(), lessonId, exerciseId, position,
            createdAtUtc: nowUtc, updatedAtUtc: nowUtc, rowVersion: 1);
    }

    /// <summary>Ricostruisce la riga da quanto è stato persistito, validando i dati letti.</summary>
    public static LessonExercise Restore(
        Guid id,
        Guid lessonId,
        Guid exerciseId,
        int position,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        long rowVersion)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("L'identificativo dell'esercizio di lezione è obbligatorio.", nameof(id));
        }

        EnsureLesson(lessonId);
        EnsureExercise(exerciseId);
        EnsurePosition(position);

        if (updatedAtUtc < createdAtUtc)
        {
            throw new ArgumentException("L'ultima modifica non può precedere la creazione.", nameof(updatedAtUtc));
        }

        if (rowVersion < 1)
        {
            throw new ArgumentException("La versione di riga deve essere positiva.", nameof(rowVersion));
        }

        return new LessonExercise(
            id, lessonId, exerciseId, position, createdAtUtc, updatedAtUtc, rowVersion);
    }

    /// <summary>
    /// Porta la riga a una posizione e a un esercizio nuovi. È l'unica operazione che cambia una
    /// riga: posizione e riferimento si scrivono insieme perché il vincolo di unicità della
    /// tabella li guarda insieme, e modificarli in due passaggi lascerebbe una riga momentaneamente
    /// incoerente.
    /// </summary>
    public void PlaceAt(int position, Guid exerciseId, DateTimeOffset nowUtc)
    {
        EnsureExercise(exerciseId);
        EnsurePosition(position);

        ExerciseId = exerciseId;
        Position = position;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Copia con la versione di riga assegnata dalla persistenza.</summary>
    public LessonExercise WithRowVersion(long rowVersion)
    {
        if (rowVersion < 1)
        {
            throw new ArgumentException("La versione di riga deve essere positiva.", nameof(rowVersion));
        }

        return new LessonExercise(
            Id, LessonId, ExerciseId, Position, CreatedAtUtc, UpdatedAtUtc, rowVersion);
    }

    private static void EnsureLesson(Guid lessonId)
    {
        if (lessonId == Guid.Empty)
        {
            throw new ArgumentException("Un esercizio di lezione appartiene a una lezione.", nameof(lessonId));
        }
    }

    private static void EnsureExercise(Guid exerciseId)
    {
        if (exerciseId == Guid.Empty)
        {
            throw new ArgumentException("Un esercizio di lezione riferisce un esercizio.", nameof(exerciseId));
        }
    }

    private static void EnsurePosition(int position)
    {
        if (position == 0)
        {
            throw new ArgumentException(
                "La posizione dell'esercizio è un numero diverso da zero.", nameof(position));
        }
    }
}
