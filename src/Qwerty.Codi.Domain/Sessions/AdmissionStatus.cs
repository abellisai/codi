namespace Qwerty.Codi.Domain.Sessions;

/// <summary>
/// Ammissione di un partecipante alla sessione (§7.4). L'esclusione non cancella il lavoro già
/// salvato: toglie l'operatività, chiude la connessione e revoca la credenziale (§10.4,
/// <c>ExcludeParticipant</c>). I valori sono gli stessi usati dal database nella colonna
/// <c>admission_status</c>.
/// </summary>
public enum AdmissionStatus
{
    /// <summary>Ammesso: può ricevere stato desiderato e lavorare.</summary>
    Admitted,

    /// <summary>Escluso dal docente: nessun permesso operativo, storica comunque consultabile.</summary>
    Excluded,
}
