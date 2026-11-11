using System.Text.Json;

namespace Qwerty.Codi.Application.Dispatch;

/// <summary>
/// Serializzazione dei payload del dispatcher. Sta in un solo posto perché il formato è un
/// contratto (§10.2): corpo JSON in camelCase, deserializzato senza distinguere maiuscole.
/// </summary>
public static class CodiJson
{
    /// <summary>
    /// Opzioni dei corpi delle richieste. I contatori a 64 bit viaggiano come stringhe decimali
    /// (§10.2): il numero JSON è un doppio e perderebbe cifre su una versione di riga grande.
    /// </summary>
    public static JsonSerializerOptions Payload { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,

        // Un campo del corpo è obbligatorio **a meno che il record della richiesta non gli dia un
        // valore predefinito**: la nullabilità da sola non lo rende facoltativo, quindi
        // l'opzionalità si dichiara nel record e non si deduce dal tipo. Regola misurata, non
        // dedotta: senza di essa il dispatcher accetterebbe `{}` consegnando `null` all'handler,
        // e sarebbe il caso d'uso a dover difendersi dal trasporto (§10.1).
        RespectRequiredConstructorParameters = true,

        Converters = { new LongAsStringJsonConverter() },
    };

    /// <summary>
    /// Applica le stesse convenzioni a un serializzatore che non è quello del dispatcher, come
    /// quello degli endpoint dedicati di §10.3: gli stessi campi non devono avere due forme a
    /// seconda della porta da cui escono, e un contatore a 64 bit resta una stringa decimale (§10.2).
    /// </summary>
    public static void ApplyTo(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.PropertyNameCaseInsensitive = true;
        options.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString;

        if (!options.Converters.Any(converter => converter is LongAsStringJsonConverter))
        {
            options.Converters.Add(new LongAsStringJsonConverter());
        }
    }
}
