namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Esito della verifica di una bozza (§10.4, §13.8): vero quando l'esercizio è pubblicabile, con
/// l'elenco dei problemi leggibili che lo impediscono. Un elenco vuoto non è un'assenza di
/// risposta: è la risposta «non c'è nulla da correggere».
/// </summary>
public sealed record ExerciseDraftValidationDto(bool IsValid, IReadOnlyList<string> Problems);
