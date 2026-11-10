using System.Globalization;
using System.Text;

namespace Wallet.Domain;

/// <summary>
/// Provee comparación tolerante de nombres y apellidos de personas frente a registros oficiales (RENAPER).
/// Aplica normalización a mayúsculas, remoción de acentos/diacríticos y contención de tokens.
/// </summary>
public static class IdentityNameMatcher
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "DE", "DEL", "LA", "LAS", "LOS", "Y", "DA", "DI", "DO", "DOS", "DAS", "SAN", "SANTA"
    };

    /// <summary>
    /// Determina si los nombres y apellidos ingresados coinciden de manera razonable con el nombre completo oficial.
    /// Verifica que al menos un token significativo de los nombres y de los apellidos ingresados esté presente en el nombre oficial.
    /// </summary>
    public static bool Matches(
        string? requestedFirstName,
        string? requestedLastName,
        string? verifiedFullName)
    {
        if (string.IsNullOrWhiteSpace(requestedFirstName) ||
            string.IsNullOrWhiteSpace(requestedLastName) ||
            string.IsNullOrWhiteSpace(verifiedFullName))
        {
            return false;
        }

        return TokensMatch(requestedFirstName, verifiedFullName) &&
               TokensMatch(requestedLastName, verifiedFullName);
    }

    /// <summary>
    /// Determina si los nombres y apellidos ingresados coinciden de manera razonable con los registrados oficialmente en campos separados.
    /// </summary>
    public static bool Matches(
        string? requestedFirstName,
        string? requestedLastName,
        string? verifiedFirstName,
        string? verifiedLastName)
    {
        if (string.IsNullOrWhiteSpace(requestedFirstName) ||
            string.IsNullOrWhiteSpace(requestedLastName) ||
            string.IsNullOrWhiteSpace(verifiedFirstName) ||
            string.IsNullOrWhiteSpace(verifiedLastName))
        {
            return false;
        }

        return TokensMatch(requestedFirstName, verifiedFirstName) &&
               TokensMatch(requestedLastName, verifiedLastName);
    }

    /// <summary>
    /// Normaliza un texto removiendo diacríticos/acentos, convirtiendo a mayúsculas y colapsando espacios y caracteres especiales.
    /// </summary>
    public static string NormalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized = text.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        var previousWasSpace = false;

        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category != UnicodeCategory.NonSpacingMark)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(char.ToUpperInvariant(ch));
                    previousWasSpace = false;
                }
                else if (!previousWasSpace)
                {
                    sb.Append(' ');
                    previousWasSpace = true;
                }
            }
        }

        return sb.ToString().Trim().Normalize(NormalizationForm.FormC);
    }

    private static bool TokensMatch(string requested, string verified)
    {
        var reqTokens = Tokenize(requested);
        var verTokens = Tokenize(verified);

        if (reqTokens.Count == 0 || verTokens.Count == 0)
            return false;

        var reqSignificant = reqTokens.Where(t => !StopWords.Contains(t) && t.Length > 1).ToHashSet(StringComparer.Ordinal);
        var verSignificant = verTokens.Where(t => !StopWords.Contains(t) && t.Length > 1).ToHashSet(StringComparer.Ordinal);

        // Si ambos lados tienen tokens significativos (no stopwords), requerimos que al menos uno coincida
        if (reqSignificant.Count > 0 && verSignificant.Count > 0)
        {
            return reqSignificant.Overlaps(verSignificant);
        }

        // Si alguno o ambos tienen únicamente stopwords o palabras muy cortas, comparamos los tokens generales
        return reqTokens.Overlaps(verTokens);
    }

    private static HashSet<string> Tokenize(string text)
    {
        var normalized = NormalizeText(text);
        return normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
    }
}
