namespace Qwerty.Codi.Domain.Sessions;

/// <summary>
/// Vista mostrata all'alunno (§6.2). È lo stato desiderato che il server vuole per la sua pagina,
/// non ciò che il browser riferisce di mostrare: la vista osservata può essere in ritardo.
/// </summary>
public enum ParticipantView
{
    /// <summary>Attesa: la sessione è in corso ma nessuna attività è assegnata.</summary>
    Waiting,

    /// <summary>Attività assegnata, con l'esercizio e i controlli previsti dallo stato desiderato.</summary>
    Exercise,

    /// <summary>Saluto finale: la sessione è terminata o il partecipante è uscito.</summary>
    Goodbye,
}
