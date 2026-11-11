using System.Text.Json;
using Qwerty.Codi.Application.Dispatch;

namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Direttiva di esecuzione dello stato desiderato (§6.2): l'ultimo comando esecutivo, con un
/// identificativo proprio che la rende applicabile **una volta sola**. Il client riconosce
/// l'identificativo già applicato, quindi la stessa direttiva consegnata due volte non riavvia un
/// programma concluso; ripartire è una decisione del docente (`RestartExecution`), non una
/// conseguenza di una riconnessione (§5.2, invariante 14).
///
/// `ExecutionGeneration` è la generazione a cui la direttiva appartiene. Un valore assente significa
/// «la generazione che il destinatario ha»: è il caso di un comando di classe, perché dopo un
/// riavvio individuale le generazioni possono divergere e una sola generazione nell'involucro
/// uguale per tutti sarebbe sbagliata per qualcuno. Un comando al singolo destinatario la dichiara.
/// </summary>
public sealed record ExecutionDirectiveDto(
    Guid DirectiveId,
    string Kind,
    long? ExecutionGeneration,
    DateTimeOffset IssuedAtUtc)
{
    /// <summary>Avvio del progetto corrente: non azzera la scena e non tocca il progetto (§10.4).</summary>
    public const string RunKind = "Run";

    /// <summary>Riavvio imposto dal docente: la generazione avanza e la scena riparte (§10.4).</summary>
    public const string RestartKind = "Restart";

    /// <summary>Direttiva di avvio per un destinatario, alla generazione dichiarata o alla propria.</summary>
    public static ExecutionDirectiveDto Run(long? executionGeneration, DateTimeOffset nowUtc) =>
        new(Guid.NewGuid(), RunKind, executionGeneration, nowUtc);

    /// <summary>Direttiva di riavvio per un destinatario, alla nuova generazione o alla propria.</summary>
    public static ExecutionDirectiveDto Restart(long? executionGeneration, DateTimeOffset nowUtc) =>
        new(Guid.NewGuid(), RestartKind, executionGeneration, nowUtc);

    /// <summary>Documento da scrivere in <c>participant_state.directive_json</c>.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, CodiJson.Payload);

    /// <summary>
    /// Legge la direttiva persistita. Il documento lo scrive il server, quindi un documento
    /// illeggibile o con un tipo non riconosciuto è un difetto interno: dirlo è meglio che far
    /// ripartire un client con una direttiva inventata.
    /// </summary>
    public static ExecutionDirectiveDto? Read(string? directiveJson)
    {
        if (string.IsNullOrWhiteSpace(directiveJson))
        {
            return null;
        }

        ExecutionDirectiveDto? directive;
        try
        {
            directive = JsonSerializer.Deserialize<ExecutionDirectiveDto>(directiveJson, CodiJson.Payload);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "La direttiva di esecuzione persistita non è un documento JSON leggibile.", exception);
        }

        if (directive is null || !IsKnownKind(directive.Kind))
        {
            throw new InvalidOperationException(
                "La direttiva di esecuzione persistita non dichiara un tipo riconosciuto.");
        }

        return directive;
    }

    private static bool IsKnownKind(string kind) =>
        string.Equals(kind, RunKind, StringComparison.Ordinal) ||
        string.Equals(kind, RestartKind, StringComparison.Ordinal);
}
