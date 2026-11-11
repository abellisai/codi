namespace Qwerty.Codi.Domain.Sessions;

/// <summary>
/// Stato di una sessione di lezione (§6.1). Ogni transizione è prodotta da un comando del catalogo
/// §10.4: una sessione terminata non viene riaperta e, per una nuova lezione operativa, se ne crea
/// e avvia una nuova.
/// </summary>
public enum SessionStatus
{
    /// <summary>Sessione istanziata e non ancora avviata: ingressi inizialmente chiusi e contenuto di attesa.</summary>
    Lobby,

    /// <summary>Sessione avviata: il docente conduce le attività.</summary>
    Active,

    /// <summary>Impostazione generale di pausa della classe.</summary>
    Paused,

    /// <summary>Sessione conclusa definitivamente e non riaperta.</summary>
    Ended,
}
