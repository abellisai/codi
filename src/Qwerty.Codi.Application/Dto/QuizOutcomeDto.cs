namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Esito di un esercizio dichiarativo, letto dall'alunno dopo aver salvato la risposta.
///
/// Dice **se** la risposta è corretta e ripete i messaggi che il docente ha scritto per i due casi.
/// Non porta la risposta attesa: il bambino può riprovare finché vuole, e una soluzione scritta nel
/// DTO renderebbe la correzione una formalità. Per la stessa ragione non porta un punteggio parziale
/// né il numero di tentativi: l'esito è dell'esercizio, non una pagella dell'alunno.
/// </summary>
public sealed record QuizOutcomeDto(
    bool Correct,
    string SuccessMessage,
    string WrongMessage);
