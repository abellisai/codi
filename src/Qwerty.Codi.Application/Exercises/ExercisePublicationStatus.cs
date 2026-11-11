using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Persistence;

namespace Qwerty.Codi.Application.Exercises;

/// <summary>Stato autore ricavato dai documenti salvati, senza modificare le versioni pubblicate.</summary>
internal static class ExercisePublicationStatus
{
    public static async Task<string> ReadAsync(IUnitOfWork work, string exerciseId, CancellationToken cancellationToken)
    {
        var versions = await work.Repo<IExerciseVersionRepository>()
            .QueryAsync(row => row.ExerciseId == exerciseId, cancellationToken).ConfigureAwait(false);
        var latest = versions.MaxBy(row => row.VersionNumber);
        if (latest is null) return "Draft";
        var draft = await work.Repo<IExerciseDraftRepository>().GetAsync(exerciseId, cancellationToken).ConfigureAwait(false);
        var companion = await work.Repo<IExerciseVersionPrivateRepository>().GetAsync(latest.Id, cancellationToken).ConfigureAwait(false);
        return Compare(draft, latest, companion);
    }

    /// <summary>Tre letture per l'intero elenco; nessuna richiesta aggiuntiva per singola riga.</summary>
    public static async Task<IReadOnlyDictionary<string, string>> ReadAllAsync(IUnitOfWork work, CancellationToken cancellationToken)
    {
        var versions = await work.Repo<IExerciseVersionRepository>().GetAllAsync(cancellationToken).ConfigureAwait(false);
        var drafts = await work.Repo<IExerciseDraftRepository>().GetAllAsync(cancellationToken).ConfigureAwait(false);
        var companions = await work.Repo<IExerciseVersionPrivateRepository>().GetAllAsync(cancellationToken).ConfigureAwait(false);
        var draftsById = drafts.ToDictionary(row => row.Id, StringComparer.Ordinal);
        var privateById = companions.ToDictionary(row => row.Id, StringComparer.Ordinal);
        return versions.GroupBy(row => row.ExerciseId).ToDictionary(group => group.Key, group =>
        {
            var latest = group.MaxBy(row => row.VersionNumber)!;
            return Compare(draftsById.GetValueOrDefault(group.Key), latest, privateById.GetValueOrDefault(latest.Id));
        }, StringComparer.Ordinal);
    }

    private static string Compare(ExerciseDraftEntity? draft, ExerciseVersionEntity latest, ExerciseVersionPrivateEntity? companion)
    {
        if (draft is null) return "Available";
        return string.Equals(draft.DefinitionJson, latest.DefinitionJson, StringComparison.Ordinal)
            && string.Equals(draft.WorkspaceJson, latest.InitialWorkspaceJson, StringComparison.Ordinal)
            && string.Equals(draft.ReferenceSolutionJson, companion?.ReferenceSolutionJson, StringComparison.Ordinal)
            ? "Available" : "UnpublishedChanges";
    }
}
