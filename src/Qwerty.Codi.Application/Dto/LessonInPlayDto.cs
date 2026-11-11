namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Presenza di una lezione in play: l'unica cosa che la home dell'alunno deve sapere prima di
/// chiedere il codice. Non contiene identificativi, titoli né conteggi di partecipanti.
/// </summary>
public sealed record LessonInPlayDto(bool InPlay);
