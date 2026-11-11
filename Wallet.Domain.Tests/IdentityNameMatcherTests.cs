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

    [Theory]
    [InlineData("Ana", "Pérez", "ANA MARIA PEREZ")]
    [InlineData("Ana María", "Pérez", "PEREZ ANA")]
    [InlineData("María Laura", "de la Cruz", "CRUZ MARIA")]
    [InlineData("Juan", "Gómez", "GOMEZ JUAN CARLOS")]
    [InlineData("Ana", "Gonzalez", "Ana María de la Paz Gonzalez")]
    [InlineData("Ana", "Gonzalez", "GONZALEZ, ANA MARIA DE LA PAZ")]
    [InlineData("Ana María", "Gonzalez", "GONZALEZ ANA MARIA DE LA PAZ")]
    [InlineData("Ana", "de la Paz Gonzalez", "GONZALEZ ANA MARIA DE LA PAZ")]
    public void Matches_single_full_name_string_when_first_and_last_name_tokens_present(
        string reqFirst, string reqLast, string verFullName)
    {
        Assert.True(IdentityNameMatcher.Matches(reqFirst, reqLast, verFullName));
    }

    [Theory]
    [InlineData("Ana", "Pérez", "CARLOS PEREZ")]
    [InlineData("Ana", "Pérez", "ANA GOMEZ")]
    [InlineData("Juan", "Pérez", "PERSONA DE EJEMPLO")]
    [InlineData("Ana", "Pérez", "")]
    [InlineData("Ana", "Pérez", null)]
    public void Does_not_match_single_full_name_when_first_or_last_name_missing(
        string reqFirst, string reqLast, string? verFullName)
    {
        Assert.False(IdentityNameMatcher.Matches(reqFirst, reqLast, verFullName));
    }

    [Fact]
    public void NormalizeText_collapses_multiple_spaces_and_special_characters()
    {
        var normalized = IdentityNameMatcher.NormalizeText("  María - Laura   de   los   Ángeles  ");
        Assert.Equal("MARIA LAURA DE LOS ANGELES", normalized);
    }

    [Theory]
    [InlineData("Ana", "Peña", "ANA PE\uFFFDA")]       // Unicode replacement char 
    [InlineData("Ana", "Peña", "ANA PE?A")]         // Signo de pregunta incrustado
    [InlineData("Ana", "Peña", "ANA PEÃ±A")]        // Decodificación UTF-8 / Latin1 rota
    [InlineData("Ana", "Peña", "ANA PEA")]          // Caracter 'ñ' omitido por completo
    [InlineData("Lucas", "Núñez", "LUCAS NUEZ")]     // 'ñ' omitida
    [InlineData("Lucas", "Núñez", "LUCAS NU?EZ")]    // 'ñ' reemplazada por '?'
    [InlineData("Lucas", "Núñez", "LUCAS NUÃ‘EZ")]   // 'Ñ' mayúscula decodificada como Ã‘
    [InlineData("Lucas", "Núñez", "LUCAS NU\uFFFD\uFFFDEZ")] // doble reemplazo corrupto
    [InlineData("Federico", "Acuña", "FEDERICO ACUA")] // 'ñ' omitida
    [InlineData("Carlos", "Ibáñez", "CARLOS IBA?EZ")]
    [InlineData("Carlos", "Ibáñez", "CARLOS IBAEZ")]
    [InlineData("Esteban", "Cañete", "ESTEBAN CAETE")]
    [InlineData("Laura", "Muñoz", "LAURA MUOZ")]
    [InlineData("Laura", "Muñoz", "LAURA MU?OZ")]
    [InlineData("Martín", "Zúñiga", "MARTIN ZUIGA")]
    public void Matches_when_tilde_is_corrupted_or_dropped_in_provider(
        string reqFirst, string reqLast, string verFullName)
    {
        Assert.True(IdentityNameMatcher.Matches(reqFirst, reqLast, verFullName));
    }

    [Theory]
    [InlineData("Carlos", "Pérez", "CARLOS GOMEZ")]
    [InlineData("Carlos", "López", "CARLOS GOMEZ")]
    [InlineData("Pedro", "Díaz", "PEDRO RUIZ")]
    [InlineData("Juan", "Peña", "JUAN SENA")]
    [InlineData("María", "Pérez", "MARIO PEREZ")]
    [InlineData("Mario", "Pérez", "MARIA PEREZ")]
    [InlineData("Ana", "Ferreyra", "ANA HERRERA")]
    [InlineData("Carlos", "Pérez", "CARLOS VELEZ")]
    [InlineData("Juan", "Pérez", "JOSE PEREZ")]
    [InlineData("Marcos", "Gómez", "MARTIN GOMEZ")]
    [InlineData("Pedro", "Gómez", "PABLO GOMEZ")]
    [InlineData("Ana", "Pérez", "EVA PEREZ")]
    public void Does_not_match_distinct_names_even_with_superficial_similarity(
        string reqFirst, string reqLast, string verFullName)
    {
        Assert.False(IdentityNameMatcher.Matches(reqFirst, reqLast, verFullName));
    }
}
