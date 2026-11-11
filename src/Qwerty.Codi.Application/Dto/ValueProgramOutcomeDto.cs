namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Esito di un programma che conserva valori, letto dall'alunno dopo aver salvato il progetto.
///
/// Dice **se** il programma produce quello che doveva e ripete i messaggi che il docente ha scritto
/// per i due casi. Porta anche il primo caso fallito, perché su un esercizio di programmazione «non
/// funziona» non basta a correggere: sapere che con i valori 3, 8 e 6 il programma mostra 1 invece
/// di 2 è quello che serve per capire dove guardare.
///
/// Non porta i casi di prova attesi per intero né un punteggio: la correzione è dell'esercizio, non
/// una pagella dell'alunno.
/// </summary>
public sealed record ValueProgramOutcomeDto(
    bool Passed,
    string SuccessMessage,
    string WrongMessage,
    /// <summary>Che cosa non va, quando c'è qualcosa da correggere; vuoto quando il programma è giusto.</summary>
    string Problem);
