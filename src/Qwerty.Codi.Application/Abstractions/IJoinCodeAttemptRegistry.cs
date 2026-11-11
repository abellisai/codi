namespace Qwerty.Codi.Application.Abstractions;

/// <summary>
/// Conteggio dei tentativi di codice di ingresso andati a vuoto (§9.4, §17.3). Vive in memoria come
/// il registro delle connessioni: un riavvio del server azzera i conteggi, ed è accettato — il
/// blocco protegge da una ricerca a tentoni in aula, non da un attacco distribuito.
///
/// Non conserva il codice digitato: registra **che** un tentativo è fallito, con il suo istante, e
/// nient'altro. La chiave ha due componenti — l'impronta dello scope di ammissione e quella
/// dell'indirizzo di rete — perché un browser che rinnova il ticket a ogni richiesta non deve poter
/// eludere il limite cambiando scope.
/// </summary>
public interface IJoinCodeAttemptRegistry
{
    /// <summary>Vero se i tentativi falliti recenti hanno esaurito il limite ammesso.</summary>
    bool IsBlocked(JoinCodeAttemptScope scope, DateTimeOffset nowUtc);

    /// <summary>Registra un tentativo andato a vuoto: un codice errato o un ingresso rifiutato.</summary>
    void RegisterFailure(JoinCodeAttemptScope scope, DateTimeOffset nowUtc);

    /// <summary>Registra un ingresso riuscito: il conteggio del browser e quello dell'indirizzo cadono.</summary>
    void RegisterSuccess(JoinCodeAttemptScope scope, DateTimeOffset nowUtc);
}

/// <summary>
/// Chiave di un conteggio di tentativi: l'impronta dello scope di ammissione — che distingue il
/// browser senza conservarne il ticket — e quella dell'indirizzo di rete, quando il trasporto lo
/// conosce. Entrambe sono impronte: il registro non conserva né il ticket né l'indirizzo in chiaro.
/// </summary>
public readonly record struct JoinCodeAttemptScope(string AdmissionFingerprint, string? AddressFingerprint);
