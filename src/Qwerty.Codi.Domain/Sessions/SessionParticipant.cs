namespace Qwerty.Codi.Domain.Sessions;

/// <summary>
/// Partecipazione di un alunno alla sessione (§7.4): l'identità del momento, non un'anagrafica. Il
/// nome appartiene alla partecipazione e può essere corretto dal docente; nomi uguali sono ammessi,
/// perché due «Luca» nella stessa classe sono una situazione normale e il nome non identifica
/// nessuno (§5.2).
///
/// La partecipazione porta l'epoca della connessione operativa (§5.3, §12.5): l'incremento avviene
/// quando il client acquisisce l'operatività (<see cref="AcquireConnection"/>), quindi una
/// connessione superata non ha più l'epoca corrente e i suoi messaggi tardivi si riconoscono.
/// L'esclusione è una transizione di stato ripetibile senza effetti: escludere chi è già escluso
/// non cambia la riga, come l'archiviazione ripetuta di una scuola.
/// </summary>
public sealed class SessionParticipant
{
    private SessionParticipant(
        Guid id,
        Guid sessionId,
        Guid? stationId,
        string displayName,
        AdmissionStatus admissionStatus,
        long connectionEpoch,
        DateTimeOffset joinedAtUtc,
        DateTimeOffset? excludedAtUtc,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        long rowVersion,
        DateTimeOffset? leftAtUtc = null)
    {
        Id = id;
        SessionId = sessionId;
        StationId = stationId;
        DisplayName = displayName;
        AdmissionStatus = admissionStatus;
        ConnectionEpoch = connectionEpoch;
        JoinedAtUtc = joinedAtUtc;
        ExcludedAtUtc = excludedAtUtc;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        RowVersion = rowVersion;
        LeftAtUtc = leftAtUtc;
    }

    public Guid Id { get; }

    /// <summary>Sessione a cui la partecipazione appartiene: non cambia.</summary>
    public Guid SessionId { get; }

    /// <summary>Postazione da cui l'alunno è entrato; assente quando il PC non è noto (§7.2).</summary>
    public Guid? StationId { get; private set; }

    /// <summary>Nome visualizzato, 1–30 grafemi, senza spazi ai bordi.</summary>
    public string DisplayName { get; private set; }

    public AdmissionStatus AdmissionStatus { get; private set; }

    /// <summary>Epoca della connessione operativa: esclude i messaggi delle connessioni superate.</summary>
    public long ConnectionEpoch { get; private set; }

    /// <summary>Istante dell'ingresso: l'inizio della partecipazione, non della lezione.</summary>
    public DateTimeOffset JoinedAtUtc { get; }

    /// <summary>Istante dell'esclusione; presente soltanto se lo stato è <see cref="AdmissionStatus.Excluded"/>.</summary>
    public DateTimeOffset? ExcludedAtUtc { get; private set; }

    /// <summary>Uscita volontaria: nasconde la partecipazione dalla regia senza cancellare i lavori.</summary>
    public DateTimeOffset? LeftAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Versione della riga per la concorrenza ottimistica (§7.1).</summary>
    public long RowVersion { get; }

    /// <summary>
    /// Crea la partecipazione di chi entra adesso: ammessa, senza epoca acquisita e senza
    /// postazione se il PC non è noto. La postazione si può associare in un secondo momento
    /// (§9.5), quindi non è obbligatoria all'ingresso.
    /// </summary>
    public static SessionParticipant Create(
        Guid sessionId,
        string displayName,
        Guid? stationId,
        DateTimeOffset nowUtc)
    {
        EnsureIdentifier(sessionId, nameof(sessionId), "Una partecipazione appartiene a una sessione.");
        EnsureDisplayName(displayName);
        EnsureUtc(nowUtc, nameof(nowUtc));

        return new SessionParticipant(
            Guid.NewGuid(),
            sessionId,
            stationId,
            ParticipantName.Trim(displayName),
            AdmissionStatus.Admitted,
            connectionEpoch: 0,
            joinedAtUtc: nowUtc,
            excludedAtUtc: null,
            createdAtUtc: nowUtc,
            updatedAtUtc: nowUtc,
            rowVersion: 1);
    }

    /// <summary>Ricostruisce la partecipazione da quanto è stato persistito, validando i dati letti.</summary>
    public static SessionParticipant Restore(
        Guid id,
        Guid sessionId,
        Guid? stationId,
        string displayName,
        AdmissionStatus admissionStatus,
        long connectionEpoch,
        DateTimeOffset joinedAtUtc,
        DateTimeOffset? excludedAtUtc,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        long rowVersion,
        DateTimeOffset? leftAtUtc = null)
    {
        EnsureIdentifier(id, nameof(id), "L'identificativo della partecipazione è obbligatorio.");
        EnsureIdentifier(sessionId, nameof(sessionId), "Una partecipazione appartiene a una sessione.");
        EnsureDisplayName(displayName);

        if (!Enum.IsDefined(admissionStatus))
        {
            throw new ArgumentOutOfRangeException(
                nameof(admissionStatus), admissionStatus, "Stato di ammissione non riconosciuto.");
        }

        if (connectionEpoch < 0)
        {
            throw new ArgumentException("L'epoca della connessione non può essere negativa.", nameof(connectionEpoch));
        }

        if (rowVersion < 1)
        {
            throw new ArgumentException("La versione di riga deve essere positiva.", nameof(rowVersion));
        }

        EnsureUtc(joinedAtUtc, nameof(joinedAtUtc));
        EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        EnsureUtc(updatedAtUtc, nameof(updatedAtUtc));

        if (excludedAtUtc is { } excluded)
        {
            EnsureUtc(excluded, nameof(excludedAtUtc));
        }

        EnsureExclusionCoherence(admissionStatus, excludedAtUtc);
        if (leftAtUtc is { } left)
        {
            EnsureUtc(left, nameof(leftAtUtc));
            if (left < joinedAtUtc) throw new ArgumentException("L'uscita non può precedere l'ingresso.", nameof(leftAtUtc));
        }

        if (joinedAtUtc < createdAtUtc)
        {
            throw new ArgumentException(
                "L'ingresso non può precedere la creazione della riga.", nameof(joinedAtUtc));
        }

        if (updatedAtUtc < createdAtUtc)
        {
            throw new ArgumentException("L'ultima modifica non può precedere la creazione.", nameof(updatedAtUtc));
        }

        return new SessionParticipant(
            id, sessionId, stationId, ParticipantName.Trim(displayName),
            admissionStatus, connectionEpoch, joinedAtUtc, excludedAtUtc,
            createdAtUtc, updatedAtUtc, rowVersion, leftAtUtc);
    }

    /// <summary>Vero se la partecipazione è ammessa e può quindi ricevere stato desiderato.</summary>
    public bool IsAdmitted => AdmissionStatus == AdmissionStatus.Admitted && LeftAtUtc is null;

    /// <summary>Registra una sola volta l'uscita volontaria.</summary>
    public void Leave(DateTimeOffset nowUtc)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));
        if (nowUtc < JoinedAtUtc) throw new ArgumentException("L'uscita non può precedere l'ingresso.", nameof(nowUtc));
        if (LeftAtUtc is not null) return;
        LeftAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Esclude la partecipazione dalla sessione (§10.4). Ripetere l'esclusione non cambia nulla:
    /// l'istante è quello della prima, così come l'archiviazione ripetuta non alza la versione.
    /// </summary>
    public void Exclude(DateTimeOffset nowUtc)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));

        if (AdmissionStatus == AdmissionStatus.Excluded)
        {
            return;
        }

        AdmissionStatus = AdmissionStatus.Excluded;
        ExcludedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Corregge il nome visualizzato e la postazione (§10.4, §9.5): sono i due dati che il docente
    /// può aggiustare — un nome scritto male, un PC riconosciuto o spostato — mentre l'identità
    /// della partecipazione e il suo stato di ammissione non cambiano. La postazione può anche
    /// essere tolta, se il PC non è più noto.
    /// </summary>
    public void Correct(string displayName, Guid? stationId, DateTimeOffset nowUtc)
    {
        EnsureDisplayName(displayName);
        EnsureUtc(nowUtc, nameof(nowUtc));

        DisplayName = ParticipantName.Trim(displayName);
        StationId = stationId;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Acquisisce l'operatività della connessione corrente (§11.2 passo 4, §12.5): incrementa
    /// l'epoca, che ordina i messaggi delle connessioni successive — un upload o un evento con
    /// un'epoca superata viene rifiutato. Non cambia l'ammissione: chi è escluso non acquisisce
    /// nulla, e il caso d'uso lo verifica prima di arrivare qui.
    /// </summary>
    public long AcquireConnection(DateTimeOffset nowUtc)
    {
        EnsureUtc(nowUtc, nameof(nowUtc));

        ConnectionEpoch++;
        UpdatedAtUtc = nowUtc;

        return ConnectionEpoch;
    }

    /// <summary>Copia con la versione di riga assegnata dalla persistenza.</summary>
    public SessionParticipant WithRowVersion(long rowVersion)
    {
        if (rowVersion < 1)
        {
            throw new ArgumentException("La versione di riga deve essere positiva.", nameof(rowVersion));
        }

        return new SessionParticipant(
            Id, SessionId, StationId, DisplayName, AdmissionStatus, ConnectionEpoch,
            JoinedAtUtc, ExcludedAtUtc, CreatedAtUtc, UpdatedAtUtc, rowVersion, LeftAtUtc);
    }

    private static void EnsureDisplayName(string displayName)
    {
        ArgumentNullException.ThrowIfNull(displayName);

        if (!ParticipantName.IsUsable(displayName))
        {
            throw new ArgumentException(
                $"Il nome del partecipante deve avere da 1 a {ParticipantName.MaxGraphemes} caratteri.",
                nameof(displayName));
        }
    }

    /// <summary>
    /// Coerenza fra ammissione e istante di esclusione: solo una partecipazione esclusa ha l'istante,
    /// e una esclusa ce l'ha sempre. È lo stesso vincolo che il database impone con un CHECK (§7.4).
    /// </summary>
    private static void EnsureExclusionCoherence(
        AdmissionStatus admissionStatus,
        DateTimeOffset? excludedAtUtc)
    {
        var isExcluded = admissionStatus == AdmissionStatus.Excluded;

        if (isExcluded != (excludedAtUtc is not null))
        {
            throw new ArgumentException(
                "Solo una partecipazione esclusa porta l'istante di esclusione.", nameof(excludedAtUtc));
        }
    }

    private static void EnsureIdentifier(Guid value, string parameterName, string message)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(message, parameterName);
        }
    }

    private static void EnsureUtc(DateTimeOffset instant, string parameterName)
    {
        if (instant.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("L'istante deve essere in UTC.", parameterName);
        }
    }
}
