namespace Qwerty.Codi.Domain.Exercises;

/// <summary>
/// Valore di un campo di un blocco, già tradotto dal formato di trasporto. Il dominio descrive
/// **che cosa** un campo deve contenere, non **come** è scritto nel documento Blockly: chi legge il
/// JSON riempie questa forma, e le regole della versione del motore la verificano.
///
/// Il valore porta il testo originale — che serve a chi legge per mostrare l'errore con il valore
/// sbagliato — e i tipi che i cataloghi usano: un numero intero, una scelta fra valori previsti e un
/// nome di variabile.
/// </summary>
public readonly record struct BlockFieldValue(string Text, long? Number, string? Choice, string? Name)
{
    /// <summary>Valore numerico intero, già letto dal documento.</summary>
    public static BlockFieldValue NumberValue(long number) =>
        new(number.ToString(System.Globalization.CultureInfo.InvariantCulture), number, null, null);

    /// <summary>Valore a scelta, confrontato senza distinzione di maiuscole come nel catalogo.</summary>
    public static BlockFieldValue ChoiceValue(string choice) => new(choice, null, choice, null);

    /// <summary>
    /// Nome di variabile. Non è una scelta fra valori previsti dal catalogo, perché i nomi ammessi
    /// dipendono dall'esercizio: la regola li riceve da chi ha in mano la definizione.
    /// </summary>
    public static BlockFieldValue NameValue(string name) => new(name, null, null, name);

    /// <summary>
    /// Testo letto dal documento, che può essere **sia** una scelta prevista dal catalogo **sia** il
    /// nome di una variabile. Chi legge il documento non sa quale regola incontrerà, e non deve
    /// indovinare: riempie entrambe le letture, e la regola del blocco usa quella che le compete.
    /// </summary>
    public static BlockFieldValue TextValue(string text) => new(text, null, text, text);
}
