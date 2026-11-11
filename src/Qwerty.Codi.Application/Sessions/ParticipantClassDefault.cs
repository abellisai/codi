using System.Text.Json;
using Qwerty.Codi.Domain.Sessions;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Legge il default di classe di <c>lesson_session.class_default_json</c> (§6.1, §6.2): è ciò che
/// un nuovo ingresso riceve come punto di partenza prima che il docente assegni un'attività.
///
/// Il documento lo scrive il server — alla creazione della sessione — quindi non è un ingresso
/// dell'utente e non si corregge: un documento che questa versione non sa leggere è un difetto
/// interno, e dirlo è meglio che far entrare un alunno in una vista inventata.
/// </summary>
public static class ParticipantClassDefault
{
    /// <summary>Vista che il default dichiara per i nuovi ingressi.</summary>
    public static ParticipantView ReadView(string classDefaultJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(classDefaultJson);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(classDefaultJson);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "Il default di classe della sessione non è un documento JSON leggibile.", exception);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("view", out var view) ||
                view.ValueKind != JsonValueKind.String ||
                !Enum.TryParse<ParticipantView>(view.GetString(), ignoreCase: false, out var parsed))
            {
                throw new InvalidOperationException(
                    "Il default di classe della sessione non dichiara una vista riconosciuta.");
            }

            return parsed;
        }
    }
}
