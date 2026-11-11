using Qwerty.Codi.Application.Dispatch;
namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Esito di un comando di regia registrato (§10.1, §10.6): l'identificativo del comando, la
/// revisione risultante, il riepilogo delle consegne e il riferimento alla query di stato. Il
/// trasporto lo traduce in <c>202 Accepted</c>.
/// </summary>
public sealed record CommandAcceptedDto(
    Guid CommandId,
    Guid SessionId,
    long SessionRevision,
    DeliverySummaryDto DeliverySummary,
    CommandStatusReferenceDto StatusQuery) : ICodiAcceptedOutcome;
