using System.Linq.Expressions;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Sessions;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Definizione vincolante di «sessione in play» (§6.1, §10.3): la sessione è <b>avviata</b>
/// (<c>Active</c> oppure <c>Paused</c>) **e** gli ingressi sono aperti. Una sessione in
/// <c>Lobby</c> o <c>Ended</c>, o con ingressi chiusi, non è in play e non compare nella scoperta
/// pubblica. La regola sta qui, accanto ai casi d'uso che la usano, così i tre percorsi di
/// scoperta non possono divergere.
///
/// La pausa sospende l'attività, non l'ingresso: un alunno che arriva durante una spiegazione — o
/// che riavvia il PC — deve poter ancora vedere la lezione e entrare, e troverà la schermata di
/// attesa. Gli ingressi restano governati dal loro flag, indipendente dallo stato (§6.1): chiuderli
/// è l'unico modo per sospendere i nuovi ingressi.
/// </summary>
internal static class InPlayLessonSession
{
    /// <summary>
    /// Predicato traducibile in SQL per la lettura delle sole sessioni in play. Il valore è
    /// ricostruito a ogni chiamata: un'espressione condivisa non è un contratto, e la query resta
    /// indipendente da come i repository la interpretano.
    /// </summary>
    public static Expression<Func<LessonSessionEntity, bool>> Predicate() =>
        row => row.AdmissionsOpenFlag == 1
            && (row.Status == nameof(SessionStatus.Active) || row.Status == nameof(SessionStatus.Paused));

    /// <summary>Stessa regola applicata in memoria alle righe già lette.</summary>
    public static bool Matches(LessonSessionEntity row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (row.AdmissionsOpenFlag != 1)
        {
            return false;
        }

        return string.Equals(row.Status, nameof(SessionStatus.Active), StringComparison.Ordinal)
            || string.Equals(row.Status, nameof(SessionStatus.Paused), StringComparison.Ordinal);
    }
}
