namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Limiti del conteggio dei tentativi di codice di ingresso (§9.4, §17.3). Stanno qui, accanto al
/// caso d'uso che li applica, perché sono una regola e non un dettaglio del registro in memoria.
///
/// Il limite è **generoso di proposito**: un bambino di otto anni sbaglia a digitare, e un blocco
/// severo trasformerebbe un errore di battitura in una lezione ferma. Dieci codici errati in un
/// minuto si fermano per il resto della finestra; un ingresso riuscito azzera tutto. Lo spazio dei
/// codici è di novanta milioni, quindi il limite serve a rendere impraticabile la ricerca a tentoni,
/// non a punire chi sbaglia.
/// </summary>
public static class JoinCodeAttemptPolicy
{
    /// <summary>Tentativi errati ammessi dentro la finestra prima del blocco.</summary>
    public const int MaximumFailures = 10;

    /// <summary>Durata della finestra dei tentativi: oltre, il conteggio riparte da zero.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Vero se il conteggio ha esaurito i tentativi e la finestra non è ancora scaduta. Il tempo è
    /// quello ricevuto dal server: l'orologio del PC dell'alunno non decide se può riprovare.
    /// </summary>
    public static bool IsBlocked(int failures, DateTimeOffset firstFailureUtc, DateTimeOffset nowUtc) =>
        failures >= MaximumFailures && nowUtc - firstFailureUtc < Window;
}
