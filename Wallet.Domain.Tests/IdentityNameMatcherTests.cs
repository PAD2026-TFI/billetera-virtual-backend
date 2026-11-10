using Wallet.Domain;
using Xunit;

namespace Wallet.Domain.Tests;

public sealed class IdentityNameMatcherTests
{
    [Theory]
    [InlineData("Juan", "Perez", "JUAN", "PEREZ")]
    [InlineData("juan", "perez", "JUAN", "PEREZ")]
    [InlineData("JUAN", "PEREZ", "Juan", "Perez")]
    public void Matches_exact_names_ignoring_case(
        string reqFirst, string reqLast, string verFirst, string verLast)
    {
        Assert.True(IdentityNameMatcher.Matches(reqFirst, reqLast, verFirst, verLast));
    }

    [Theory]
    [InlineData("María", "Pérez", "MARIA", "PEREZ")]
    [InlineData("Agustín", "González", "AGUSTIN", "GONZALEZ")]
    [InlineData("Álvaro", "Núñez", "ALVARO", "NUNEZ")]
    [InlineData("Sofía", "Gómez", "SOFIA", "GOMEZ")]
    public void Matches_names_with_accents_and_diacritics(
        string reqFirst, string reqLast, string verFirst, string verLast)
    {
        Assert.True(IdentityNameMatcher.Matches(reqFirst, reqLast, verFirst, verLast));
    }

    [Theory]
    [InlineData("María Laura", "Pérez", "MARIA", "PEREZ")]
    [InlineData("María", "Pérez", "MARIA LAURA", "PEREZ")]
    [InlineData("Juan Carlos", "Gómez", "JUAN", "GOMEZ")]
    [InlineData("Juan", "Gómez", "JUAN CARLOS", "GOMEZ")]
    [InlineData("Lucas", "Pérez González", "LUCAS", "PEREZ")]
    [InlineData("Lucas", "Pérez", "LUCAS", "PEREZ GONZALEZ")]
    public void Matches_compound_names_and_surnames_partially(
        string reqFirst, string reqLast, string verFirst, string verLast)
    {
        Assert.True(IdentityNameMatcher.Matches(reqFirst, reqLast, verFirst, verLast));
    }

    [Theory]
    [InlineData("María de los Ángeles", "de la Cruz", "MARIA ANGELES", "CRUZ")]
    [InlineData("Juan", "del Pozo", "JUAN", "DEL POZO")]
    [InlineData("Pedro", "de la Fuente", "PEDRO", "FUENTE")]
    public void Matches_names_with_spanish_stopwords_and_prepositions(
        string reqFirst, string reqLast, string verFirst, string verLast)
    {
        Assert.True(IdentityNameMatcher.Matches(reqFirst, reqLast, verFirst, verLast));
    }

    [Theory]
    [InlineData("Juan", "Pérez", "CARLOS", "PEREZ")]
    [InlineData("Juan", "Pérez", "JUAN", "GONZALEZ")]
    [InlineData("Esteban", "Quito", "PEDRO", "ALVAREZ")]
    public void Does_not_match_when_name_or_surname_is_different(
        string reqFirst, string reqLast, string verFirst, string verLast)
    {
        Assert.False(IdentityNameMatcher.Matches(reqFirst, reqLast, verFirst, verLast));
    }

    [Theory]
    [InlineData("Pedro", "de la Cruz", "PEDRO", "DE LA RUA")]
    [InlineData("María", "del Carmen", "MARIA", "DEL VALLE")]
    public void Does_not_match_when_only_stopwords_overlap_in_surnames(
        string reqFirst, string reqLast, string verFirst, string verLast)
    {
        Assert.False(IdentityNameMatcher.Matches(reqFirst, reqLast, verFirst, verLast));
    }

    [Theory]
    [InlineData("", "Perez", "JUAN", "PEREZ")]
    [InlineData("   ", "Perez", "JUAN", "PEREZ")]
    [InlineData(null, "Perez", "JUAN", "PEREZ")]
    [InlineData("Juan", "", "JUAN", "PEREZ")]
    [InlineData("Juan", "Perez", "", "PEREZ")]
    [InlineData("Juan", "Perez", "JUAN", "")]
    [InlineData("Juan", "Perez", null, "PEREZ")]
    [InlineData("Juan", "Perez", "JUAN", null)]
    public void Returns_false_for_null_or_empty_or_whitespace_values(
        string? reqFirst, string? reqLast, string? verFirst, string? verLast)
    {
        Assert.False(IdentityNameMatcher.Matches(reqFirst, reqLast, verFirst, verLast));
    }

    [Fact]
    public void NormalizeText_collapses_multiple_spaces_and_special_characters()
    {
        var normalized = IdentityNameMatcher.NormalizeText("  María - Laura   de   los   Ángeles  ");
        Assert.Equal("MARIA LAURA DE LOS ANGELES", normalized);
    }
}
