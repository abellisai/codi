namespace Qwerty.Codi.Application.Abstractions;

/// <summary>
/// Istanza del server in esecuzione (§11.4, §11.5). L'identificativo cambia a ogni avvio: un
/// riavvio invalida epoche e autorizzazioni temporanee, e il client deve accorgersene invece di
/// riprendere da uno stato che non esiste più (§12.3 passo 1). Non è un segreto, è un'etichetta di
/// ordinamento.
/// </summary>
public interface IServerInstance
{
    /// <summary>Identificativo dell'istanza, generato all'avvio del processo.</summary>
    Guid InstanceId { get; }

    /// <summary>Istante di avvio del processo: serve alla diagnosi, non è una scadenza.</summary>
    DateTimeOffset StartedAtUtc { get; }
}
