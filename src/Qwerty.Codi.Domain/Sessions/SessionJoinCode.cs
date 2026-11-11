using System.Globalization;
using System.Security.Cryptography;

namespace Qwerty.Codi.Domain.Sessions;

/// <summary>
/// Codice di ingresso della sessione (§6.1, §9.4): otto cifre decimali che il docente detta a voce e
/// l'alunno digita per entrare, al posto della catena scuola → classe → sessione.
///
/// Non è una password e non identifica nessuno: vale solo mentre la sessione è in play, si genera
/// all'avvio e accompagna la sessione fino al termine. Non è nemmeno un identificativo: due sessioni
/// non possono avere lo stesso codice finché entrambe esistono, perché il valore è unico nel
/// database, ma il codice non dice chi entra.
///
/// Il valore è una **stringa** di otto cifre e non un numero: uno zero iniziale sarebbe una cifra
/// del codice che un intero perderebbe. La prima cifra non è zero perché un codice dettato a voce
/// non deve dipendere da uno zero che l'alunno può non sentire o non digitare.
/// </summary>
public readonly record struct SessionJoinCode
{
    /// <summary>Lunghezza fissa del codice: otto cifre, come si detta e come si digita.</summary>
    public const int Length = 8;

    /// <summary>Estremo inferiore dell'intervallo generato: la prima cifra non è zero.</summary>
    private const int InclusiveMinimum = 10_000_000;

    /// <summary>Estremo superiore escluso: nove cifre non sono un codice.</summary>
    private const int ExclusiveMaximum = 100_000_000;

    private SessionJoinCode(string value) => Value = value;

    /// <summary>Le otto cifre del codice, senza separatori.</summary>
    public string Value { get; }

    /// <summary>
    /// Genera un codice casuale con il generatore crittografico del sistema: il codice non deve
    /// essere indovinabile da chi non lo ha sentito, e la generazione non dipende dall'orologio.
    /// </summary>
    public static SessionJoinCode Create() =>
        new(RandomNumberGenerator.GetInt32(InclusiveMinimum, ExclusiveMaximum)
            .ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Ricostruisce un codice letto dal database o arrivato dal payload, verificandone la forma. Un
    /// valore di lunghezza diversa, con un carattere non decimale o con lo zero iniziale non è un
    /// codice: rifiutarlo è preferibile a normalizzarlo, perché un codice normalizzato farebbe
    /// entrare in una sessione con un valore che nessuno ha detto.
    /// </summary>
    public static SessionJoinCode Restore(string value)
    {
        if (!IsWellFormed(value))
        {
            throw new ArgumentException(
                $"Il codice di ingresso deve essere di {Length} cifre e non iniziare con zero.", nameof(value));
        }

        return new SessionJoinCode(value);
    }

    /// <summary>Vero se il testo è un codice ammissibile, senza costruirne il valore.</summary>
    public static bool IsWellFormed(string? value)
    {
        if (value is null || value.Length != Length || value[0] == '0')
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Le otto cifre, forma canonica del codice nei payload e nei log.</summary>
    public override string ToString() => Value;
}
