namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Un messaggio della chat fra docente e singolo alunno (§15.1). Lo stesso tipo vale per il thread
/// letto con le query e per il messaggio che arriva sugli hub: il client legge sempre un tipo con un
/// nome, mai un oggetto costruito a mano nell'adattatore (§11.3, §11.4).
///
/// Il thread vive in memoria e non nel database: un messaggio perso al riavvio del server non è un
/// dato mancante, è l'effetto dichiarato di §15.1.
/// </summary>
public sealed record ParticipantMessageDto(
    Guid MessageId,
    Guid SessionId,
    Guid ParticipantId,
    /// <summary>Forma testuale di <see cref="Domain.Sessions.MessageAuthor"/>.</summary>
    string Author,
    string Text,
    DateTimeOffset WrittenAtUtc);
