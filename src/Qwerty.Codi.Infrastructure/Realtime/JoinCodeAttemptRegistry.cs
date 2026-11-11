using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Sessions;

namespace Qwerty.Codi.Infrastructure.Realtime;

/// <summary>
/// Conteggio in memoria dei tentativi di codice di ingresso andati a vuoto (§9.4, §17.3).
///
/// Come il registro delle connessioni, è unico per processo e protetto da un lucchetto: i client di
/// un'aula sono dodici e una regola semplice si verifica più facilmente di una ottimizzata. Il tempo
/// è sempre quello ricevuto dal server.
///
/// Ogni tentativo fallito alimenta **due** conteggi: quello del browser (impronta dello scope di
/// ammissione) e quello dell'indirizzo di rete. Il blocco scatta quando uno dei due esaurisce i
/// tentativi, così un browser che rinnova il ticket a ogni richiesta non elude il limite cambiando
/// scope. Un ingresso riuscito azzera entrambi.
///
/// Il registro non conserva il codice digitato né l'indirizzo in chiaro: le chiavi sono impronte
/// calcolate dal caso d'uso. Le voci scadute si eliminano quando il registro cresce, così un'aula
/// che sbaglia molte volte non lascia memoria occupata a tempo indeterminato.
/// </summary>
internal sealed class JoinCodeAttemptRegistry : IJoinCodeAttemptRegistry
{
    /// <summary>Soglia oltre la quale si eliminano le voci scadute prima di registrarne una nuova.</summary>
    private const int PruneThreshold = 256;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, AttemptState> _attempts = [];

    public bool IsBlocked(JoinCodeAttemptScope scope, DateTimeOffset nowUtc) =>
        Locked(() =>
            IsBlockedCore(Key(scope), nowUtc) ||
            (scope.AddressFingerprint is { } address && IsBlockedCore(AddressKey(address), nowUtc)));

    public void RegisterFailure(JoinCodeAttemptScope scope, DateTimeOffset nowUtc) =>
        Locked(() =>
        {
            if (_attempts.Count >= PruneThreshold)
            {
                Prune(nowUtc);
            }

            Increment(Key(scope), nowUtc);
            if (scope.AddressFingerprint is { } address)
            {
                Increment(AddressKey(address), nowUtc);
            }
        });

    public void RegisterSuccess(JoinCodeAttemptScope scope, DateTimeOffset nowUtc) =>
        Locked(() =>
        {
            // L'ingresso riuscito è la prova che il browser non sta cercando a tentoni: cadono
            // entrambi i conteggi, quello dello scope e quello dell'indirizzo.
            _attempts.Remove(Key(scope));
            if (scope.AddressFingerprint is { } address)
            {
                _attempts.Remove(AddressKey(address));
            }
        });

    private bool IsBlockedCore(string key, DateTimeOffset nowUtc) =>
        _attempts.TryGetValue(key, out var state) &&
        JoinCodeAttemptPolicy.IsBlocked(state.Failures, state.FirstFailureUtc, nowUtc);

    /// <summary>Conta un tentativo nella finestra corrente, oppure apre una finestra nuova.</summary>
    private void Increment(string key, DateTimeOffset nowUtc)
    {
        if (_attempts.TryGetValue(key, out var state) &&
            nowUtc - state.FirstFailureUtc < JoinCodeAttemptPolicy.Window)
        {
            _attempts[key] = state with { Failures = state.Failures + 1 };
            return;
        }

        _attempts[key] = new AttemptState(Failures: 1, FirstFailureUtc: nowUtc);
    }

    /// <summary>Chiave del conteggio del browser: le due impronte, separate perché non si confondano.</summary>
    private static string Key(JoinCodeAttemptScope scope) =>
        $"{scope.AdmissionFingerprint}\u001f{scope.AddressFingerprint ?? string.Empty}";

    /// <summary>Chiave del conteggio dell'indirizzo: una forma distinta da quella del browser.</summary>
    private static string AddressKey(string addressFingerprint) => $"\u001f{addressFingerprint}";

    /// <summary>Elimina le voci la cui finestra è scaduta: non servono più a decidere nulla.</summary>
    private void Prune(DateTimeOffset nowUtc)
    {
        var expired = _attempts
            .Where(entry => nowUtc - entry.Value.FirstFailureUtc >= JoinCodeAttemptPolicy.Window)
            .Select(entry => entry.Key)
            .ToArray();

        foreach (var key in expired)
        {
            _attempts.Remove(key);
        }
    }

    private T Locked<T>(Func<T> action)
    {
        lock (_gate)
        {
            return action();
        }
    }

    private void Locked(Action action)
    {
        lock (_gate)
        {
            action();
        }
    }

    /// <summary>Tentativi falliti dentro la finestra corrente e il loro primo istante.</summary>
    private readonly record struct AttemptState(int Failures, DateTimeOffset FirstFailureUtc);
}
