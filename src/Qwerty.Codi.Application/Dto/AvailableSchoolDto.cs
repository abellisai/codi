using Qwerty.Codi.Domain.Organization;
namespace Qwerty.Codi.Application.Dto;

/// <summary>
/// Scuola nella scoperta pubblica (§10.3): solo ciò che serve a sceglierla e a proseguire verso le
/// classi — identificativo e nome. Non è il DTO amministrativo di §10.5 e non porta archiviazione
/// né versione di riga: la scoperta è anonima e di sola lettura, e quei campi non le servono.
/// </summary>
public sealed record AvailableSchoolDto(Guid Id, string Name)
{
    public static AvailableSchoolDto FromDomain(School school)
    {
        ArgumentNullException.ThrowIfNull(school);

        return new AvailableSchoolDto(school.Id, school.Name);
    }
}
