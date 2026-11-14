namespace Qwerty.Codi.Application.Dto;

/// <summary>Nome autorevole del partecipante e versione della riga che lo contiene.</summary>
public sealed record ParticipantProfileDto(Guid ParticipantId, string DisplayName, long RowVersion);
