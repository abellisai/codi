using Qwerty.Codi.Application.Persistence;
using Qwerty.Codi.Domain.Organization;

namespace Qwerty.Codi.Application.Abstractions;

/// <summary>
/// Classi di una scuola. La modifica di nome, anno e archiviazione usa il CRUD comune con il
/// controllo di versione; l'inserimento ha un esito di conflitto previsto, perché la coppia
/// nome e anno è unica dentro la scuola.
/// </summary>
public interface IClassroomRepository : IRepository<ClassroomEntity, string>
{
    /// <summary>Falso se nome e anno sono già usati nella scuola; gli altri vincoli sono errori.</summary>
    Task<bool> TryInsertAsync(Classroom classroom, CancellationToken cancellationToken);
}
