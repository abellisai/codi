namespace Qwerty.Codi.Application.Dto;

/// <summary>Dettaglio di un alunno: la riga della griglia, il materiale e il lavoro corrente.</summary>
public sealed record SessionParticipantDetailDto(
    SessionParticipantOverviewDto Participant,
    ParticipantAssignmentDto? Assignment,
    ParticipantAttemptDto? Attempt,
    int AttemptCount);
