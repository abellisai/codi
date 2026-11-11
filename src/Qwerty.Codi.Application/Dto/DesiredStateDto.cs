namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Stato desiderato che un comando ha applicato (§6.2), nella forma che l'envelope porta al client
/// (§11.4). È la parte del comando che il destinatario deve conoscere; la vista è il valore del
/// dominio e non un codice numerico. I riferimenti ad assegnazione e tentativo restano assenti
/// finché non esistono i comandi che li creano, e il testo del documento non ne inventa.
///
/// Un campo assente significa «il comando non cambia questo valore»: un comando collettivo non
/// conosce i riferimenti individuali, che ogni client ha dal proprio snapshot (§11.4), e una
/// generazione assente lascia quella che il tentativo ha già. Solo i comandi che li stabiliscono
/// scrivono assegnazione, tentativo e generazione.
/// </summary>
public sealed record DesiredStateDto(
    string View,
    bool InteractionEnabled,
    Guid? AssignmentId = null,
    Guid? AttemptId = null,
    bool TeacherPaused = false,
    bool RecoveryBlocked = false,
    long? ExecutionGeneration = null,
    ExecutionDirectiveDto? ExecutionDirective = null);
