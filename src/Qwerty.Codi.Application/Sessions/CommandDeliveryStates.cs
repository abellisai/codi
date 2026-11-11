namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Ambiti di applicazione di un comando (§6.5, §10.8). Il valore è una stringa perché è anche il
/// contenuto di <c>session_command.scope</c>: un solo nome per lo stesso concetto, nel contratto e
/// in database.
/// </summary>
public static class CommandScopes
{
    /// <summary>Il comando vale per tutta la classe: default e stati dei partecipanti.</summary>
    public const string Class = "Class";

    /// <summary>Il comando vale per un solo destinatario, dichiarato nel comando.</summary>
    public const string Participant = "Participant";
}

/// <summary>
/// Stati di una consegna (§12.2). Sono il valore di <c>command_delivery.status</c> e del DTO di
/// stato: una consegna superata conserva la ricevuta storica ma non riporta indietro lo stato
/// corrente.
/// </summary>
public static class CommandDeliveryStates
{
    /// <summary>Ancora da inviare: è lo stato con cui nasce una consegna.</summary>
    public const string Pending = "Pending";

    /// <summary>Inviata al trasporto del destinatario, non ancora confermata.</summary>
    public const string Sent = "Sent";

    /// <summary>Applicata e confermata dal client.</summary>
    public const string Applied = "Applied";

    /// <summary>Rifiutata o fallita: il client non l'ha applicata.</summary>
    public const string Failed = "Failed";

    /// <summary>Nessuna conferma entro il tempo previsto; un ACK tardivo può portarla ad <c>Applied</c>.</summary>
    public const string Unconfirmed = "Unconfirmed";

    /// <summary>Superata da una revisione più recente: non si riapplica sul nuovo stato.</summary>
    public const string Superseded = "Superseded";
}
