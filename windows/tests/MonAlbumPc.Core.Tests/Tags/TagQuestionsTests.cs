using MonAlbumPc.Core.Tags;

namespace MonAlbumPc.Core.Tests.Tags;

public sealed class TagQuestionsTests
{
    [Fact]
    public void Deleting_tells_how_many_photos_carry_the_tag()
    {
        var question = TagQuestions.Delete("boulot", 269, 0);

        Assert.Equal("Supprimer le tag « boulot » ?", question.Title);
        Assert.Equal("Ce tag est posé sur 269 photos. Voulez-vous le supprimer ?", question.Message);
        Assert.Equal("Supprimer", question.Action);
        Assert.Equal("« Bricolage » et ses 2 sous-tags sont posés sur 312 photos. Voulez-vous les supprimer ?",
            TagQuestions.Delete("Bricolage", 312, 2).Message);
        Assert.Equal("Ce tag n'est posé sur aucune photo.", TagQuestions.Delete("vide", 0, 0).Message);
        Assert.Null(TagQuestions.Delete("vide", 0, 0).Detail);
    }

    [Fact]
    public void Renaming_and_merging_tell_what_happens_to_the_photos()
    {
        Assert.Equal("Le tag « boulot » de 269 photos sera renommé en « Travail ».", TagQuestions.Rename("boulot", "Travail", 269, 0, null).Message);
        Assert.Equal("Le tag « Bricolage » et son sous-tag, posés sur 12 photos, seront renommés en « DIY ».",
            TagQuestions.Rename("Bricolage", "DIY", 12, 1, null).Message);
        var merge = TagQuestions.Rename("boulot", "Travail", 269, 0, 40);
        Assert.Equal("Fusionner « boulot » dans « Travail » ?", merge.Title);
        Assert.Equal("Le tag « boulot » de 269 photos sera fusionné dans « Travail » (déjà posé sur 40 photos).", merge.Message);
        Assert.Equal("Fusionner", merge.Action);
    }
}
