using Qwerty.Codi.Application.Dto;

namespace Qwerty.Codi.Application.Abstractions;

/// <summary>
/// Cache in memoria dell'ultima anteprima per partecipante (§15.5). Vive in memoria e non sul
/// disco: un'anteprima persa non è un dato mancante, è un invito a richiederne una nuova. Il server
/// ne conserva al massimo una per partecipante e ne applica la scadenza.
/// </summary>
public interface IParticipantPreviewCache
{
    bool Update(ParticipantPreviewDto preview);

    ParticipantPreviewDto? Get(Guid participantId);

    void Remove(Guid participantId);
}
