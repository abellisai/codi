namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Soluzione di riferimento della bozza (§10.5): il progetto che risolve l'esercizio, riservato al
/// docente. È separata da <see cref="ExerciseDraftDto"/> perché il DTO della bozza non la porta con
/// sé (§13.1); <c>WorkspaceJson</c> è nullo quando il docente non ne ha ancora composta una.
/// </summary>
public sealed record ExerciseReferenceSolutionDto(Guid ExerciseId, string? WorkspaceJson);
