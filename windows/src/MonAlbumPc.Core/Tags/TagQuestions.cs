using MonAlbumPc.Core.Activity;

namespace MonAlbumPc.Core.Tags;

/// <summary>A confirmation before a change of the tag list: its title, what it does, a note, and its button.</summary>
public sealed record TagQuestion(string Title, string Message, string? Detail, string Action);

/// <summary>
/// The words of the confirmations of the Tags pages (web administration and Windows app), from counts made at the moment
/// of asking: « Ce tag est posé sur 269 photos. Voulez-vous le supprimer ? ».
/// </summary>
public static class TagQuestions
{
    /// <param name="count">Photos carrying the tag or one under it.</param>
    /// <param name="children">Tags under it.</param>
    public static TagQuestion Delete(string tag, int count, int children)
    {
        var photos = WorkText.Plural(count, "photo", "photos");
        var message = children == 0
            ? count == 0 ? "Ce tag n'est posé sur aucune photo." : $"Ce tag est posé sur {photos}. Voulez-vous le supprimer ?"
            : count == 0
                ? $"« {tag} » et {SubTags(children)} ne sont posés sur aucune photo. Voulez-vous les supprimer ?"
                : $"« {tag} » et {SubTags(children)} sont posés sur {photos}. Voulez-vous les supprimer ?";
        var detail = count == 0 ? null
            : children == 0 ? "Il sera retiré des fichiers ; les photos elles-mêmes sont conservées."
            : "Ils seront retirés des fichiers ; les photos elles-mêmes sont conservées.";
        return new TagQuestion($"Supprimer le tag « {tag} » ?", message, detail, "Supprimer");
    }

    /// <param name="already">Photos already carrying <paramref name="to"/> when it exists (a merge), else null.</param>
    public static TagQuestion Rename(string from, string to, int count, int children, int? already)
    {
        var subject = children == 0 ? $"Le tag « {from} »" : $"Le tag « {from} » et {SubTags(children)}";
        var photos = WorkText.Plural(count, "photo", "photos");
        string message;
        if (already is { } existing)
        {
            var target = $"« {to} » (déjà posé sur {WorkText.Plural(existing, "photo", "photos")})";
            message = count == 0
                ? $"{subject} {(children == 0 ? "sera fusionné" : "seront fusionnés")} dans {target}."
                : children == 0
                    ? $"Le tag « {from} » de {photos} sera fusionné dans {target}."
                    : $"{subject}, posés sur {photos}, seront fusionnés dans {target}.";
        }
        else
        {
            message = count == 0
                ? $"{subject} {(children == 0 ? "sera renommé" : "seront renommés")} en « {to} »."
                : children == 0
                    ? $"Le tag « {from} » de {photos} sera renommé en « {to} »."
                    : $"{subject}, posés sur {photos}, seront renommés en « {to} ».";
        }
        var detail = count == 0 ? null : "Les fichiers sont modifiés en arrière-plan ; l'avancement s'affiche dans Activité.";
        return already is null
            ? new TagQuestion($"Renommer « {from} » ?", message, detail, "Renommer")
            : new TagQuestion($"Fusionner « {from} » dans « {to} » ?", message, detail, "Fusionner");
    }

    /// <summary>The titles of the tag operations on the activity board, to show their progress on the Tags pages.</summary>
    public static bool IsTagWork(RunningWork work) =>
        work.Title.StartsWith("Ajout du tag", StringComparison.Ordinal) || work.Title.StartsWith("Retrait du tag", StringComparison.Ordinal)
        || work.Title.StartsWith("Renommage du tag", StringComparison.Ordinal) || work.Title.StartsWith("Fusion du tag", StringComparison.Ordinal)
        || work.Title.StartsWith("Suppression du tag", StringComparison.Ordinal);

    private static string SubTags(int children) => children == 1 ? "son sous-tag" : $"ses {children} sous-tags";
}
