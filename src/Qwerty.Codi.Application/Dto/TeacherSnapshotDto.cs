namespace Qwerty.Codi.Application.Dto;

public sealed record TeacherSnapshotDto(int ProtocolVersion, Guid ServerInstanceId,
    SessionDto Session, IReadOnlyList<SessionParticipantOverviewDto> Participants)
{
    public IReadOnlyList<CommandStatusDto> Commands { get; init; } = [];
}
