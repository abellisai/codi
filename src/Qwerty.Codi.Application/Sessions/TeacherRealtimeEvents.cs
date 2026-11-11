namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Nomi degli eventi insegnante di §11.3 e di §15.5. Stanno in un solo posto perché sono il
/// contratto fra il server e la dashboard, che non condividono il codice. I nomi non cambiano
/// quando cambia il metodo che li produce o la query che li alimenta.
/// </summary>
public static class TeacherRealtimeEvents
{
    public const string TeacherSnapshot = "TeacherSnapshot";
    public const string ParticipantJoined = "ParticipantJoined";
    public const string ParticipantPresenceChanged = "ParticipantPresenceChanged";
    public const string ParticipantStateUpdated = "ParticipantStateUpdated";
    public const string CommandStatusChanged = "CommandStatusChanged";

    /// <summary>Nuova anteprima disponibile per un alunno (§15.5): revisione, impronta e istante.</summary>
    public const string WorkspacePreviewAvailable = "WorkspacePreviewAvailable";

    /// <summary>
    /// Messaggio scritto dall'alunno nella chat del singolo partecipante (§8, §15.1). Il thread non si
    /// replaya all'apertura: chi arriva dopo legge il thread con la query, quindi l'evento serve solo a
    /// mostrare il messaggio nuovo a chi sta già guardando.
    /// </summary>
    public const string ParticipantMessagePosted = "ParticipantMessagePosted";

    public const string ObservationRevoked = "ObservationRevoked";
}
