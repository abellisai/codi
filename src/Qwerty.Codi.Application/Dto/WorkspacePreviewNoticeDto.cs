namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Avviso al docente che osserva (§11.3, §15.5): dice che un'anteprima è arrivata, per chi e con
/// quale revisione, senza portare il progetto. Il contenuto si legge a parte con
/// <c>GetParticipantPreview</c>, così il canale resta a bassa frequenza (§15.6).
///
/// È una **proiezione** di <see cref="WorkspacePreviewDto"/>: il tentativo non viaggia perché il
/// docente lo riceve insieme al contenuto quando lo chiede. Prima era un oggetto anonimo costruito
/// nell'adattatore, quindi nessun tipo dichiarava quali campi uscissero davvero.
/// </summary>
public sealed record WorkspacePreviewNoticeDto(Guid ParticipantId, Guid SessionId,
    long ProjectRevision, string WorkspaceHash, DateTimeOffset ReceivedAtUtc);
