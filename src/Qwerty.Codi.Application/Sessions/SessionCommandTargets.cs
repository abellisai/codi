using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Sessions;

namespace Qwerty.Codi.Application.Sessions;

/// <summary>
/// Destinatari di un comando di regia che cambia lo stato desiderato (§6.5): tutti gli alunni
/// **ammessi** della sessione per un comando di classe, oppure il solo indicato per un comando
/// individuale — che deve essere ammesso e appartenere alla sessione, altrimenti è una risorsa
/// assente e non un comando senza effetto.
///
/// Gli stati si leggono dal repository per sessione, non dai partecipanti uno per uno: è la tabella
/// dello stato desiderato a dire chi c'è, e un partecipante senza riga di stato non esiste
/// (l'ingresso la crea nella stessa transazione, §9.4). La risoluzione sta qui perché la usano
/// l'assegnazione e i comandi che ne cambiano il permesso: duplicarla significherebbe due elenchi
/// di destinatari che possono divergere.
/// </summary>
internal static class SessionCommandTargets
{
    public static async Task<IReadOnlyList<ParticipantStateEntity>> ResolveAsync(
        IUnitOfWork work,
        Guid sessionId,
        Guid? participantId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        var sessionKey = sessionId.ToString("D");
        var participants = work.Repo<ISessionParticipantRepository>();

        var rows = await participants
            .QueryAsync(row => row.SessionId == sessionKey, cancellationToken)
            .ConfigureAwait(false);

        var admitted = rows
            .Where(row => row.AdmissionStatusText == nameof(AdmissionStatus.Admitted))
            .Select(row => row.Id)
            .ToHashSet(StringComparer.Ordinal);

        if (participantId is { } targetParticipantId)
        {
            var key = targetParticipantId.ToString("D");
            if (!admitted.Contains(key))
            {
                throw CodiRequestException.ResourceNotFound();
            }

            admitted = [key];
        }

        var states = work.Repo<IParticipantStateRepository>();
        var stateRows = await states
            .QueryAsync(row => row.SessionId == sessionKey, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. stateRows
                .Where(row => admitted.Contains(row.Id))
                .OrderBy(row => row.Id, StringComparer.Ordinal),
        ];
    }
}
