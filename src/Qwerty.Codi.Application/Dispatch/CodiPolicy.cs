namespace Qwerty.Codi.Application.Dispatch;

/// <summary>
/// Policy di §9.3. Una richiesta ne dichiara una sola: il dispatcher la verifica prima di
/// eseguire l'handler, e la verifica non si deduce dal percorso HTTP.
/// </summary>
public enum CodiPolicy
{
    /// <summary>Gestione dell'organizzazione, dei contenuti e degli esercizi.</summary>
    Admin,

    /// <summary>Amministratore proprietario della sessione: comandi di regia e ingressi.</summary>
    SessionController,

    /// <summary>Partecipazione ammessa a una sessione non terminata.</summary>
    AdmittedParticipant,

    /// <summary>Come sopra, con epoch della connessione corrente e handshake completato.</summary>
    ActiveParticipantConnection,

    /// <summary>Accesso ristretto a un grant conclusivo valido.</summary>
    FinalizingParticipant,

    /// <summary>Solo ingresso e autenticazione, prima che esista un contesto applicativo.</summary>
    AnonymousBootstrap,
}
