namespace Qwerty.Codi.Api.Hubs;

/// <summary>
/// Percorsi e gruppi degli hub di §11.1. I percorsi sono la seconda superficie HTTP dell'API:
/// restano fuori dal dispatcher `/execute`, hanno schemi di autenticazione propri e non aggiungono
/// una rotta per singolo caso d'uso.
/// </summary>
internal static class CodiHubs
{
    /// <summary>Hub del partecipante: handshake, stato corrente, ACK, heartbeat e telemetria.</summary>
    public const string ParticipantPath = "/hubs/v1/participant";

    /// <summary>Hub del docente: osservazione della sessione, ricevute e telemetria.</summary>
    public const string TeacherPath = "/hubs/v1/teacher";
}

/// <summary>
/// Nomi dei gruppi interni di §11.1. L'appartenenza è decisa dal server: il gruppo è un meccanismo
/// di instradamento, non un controllo di accesso, e da solo non autorizza nulla.
/// </summary>
internal static class CodiHubGroups
{
    /// <summary>Gruppo del singolo partecipante: riceve solo il proprio stato.</summary>
    public static string Participant(Guid participantId) => $"participant:{participantId:D}";

    /// <summary>Gruppo dei docenti che osservano una sessione.</summary>
    public static string Teachers(Guid sessionId) => $"session:{sessionId:D}:teachers";
}
