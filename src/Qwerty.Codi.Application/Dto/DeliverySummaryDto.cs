namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Riepilogo delle consegne di un comando (§10.6): quante sono ancora da inviare, quante sono state
/// applicate e confermate, quante sono fallite. Non è «il comando è arrivato»: è «a che punto è la
/// consegna», che il docente osserva separatamente dal proprio invio.
/// </summary>
public sealed record DeliverySummaryDto(int Pending, int Applied, int Failed);
