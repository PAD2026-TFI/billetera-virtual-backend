using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Wallet.Domain;

/// <summary>
/// Provee comparación tolerante de nombres y apellidos de personas frente a registros oficiales (RENAPER).
/// Aplica normalización a mayúsculas, remoción de acentos/diacríticos, sanitización de caracteres corruptos de la 'Ñ'
/// y similitud fonética/ortográfica con protección estricta contra falsos positivos.
/// </summary>
public static class IdentityNameMatcher
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "DE", "DEL", "LA", "LAS", "LOS", "Y", "DA", "DI", "DO", "DOS", "DAS", "SAN", "SANTA"
    };

    private static readonly Regex CorruptedTildeRegex = new(
        @"(?<=\p{L})[\uFFFD?_#*]+(?=\p{L})",
        RegexOptions.Compiled);

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
    /// Sanitiza artefactos típicos de mala decodificación UTF-8 / Latin-1 producidos por la letra 'Ñ'
    /// o caracteres de reemplazo incrustados entre letras.
    /// </summary>
    public static string PreSanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var sanitized = text
            .Replace("Ã±", "N", StringComparison.OrdinalIgnoreCase)
            .Replace("Ã‘", "N", StringComparison.OrdinalIgnoreCase)
            .Replace("Ã\u0091", "N", StringComparison.OrdinalIgnoreCase)
            .Replace("Ã\u2018", "N", StringComparison.OrdinalIgnoreCase);

        sanitized = CorruptedTildeRegex.Replace(sanitized, "N");

        return sanitized;
    }

    /// <summary>
    /// Normaliza un texto removiendo diacríticos/acentos, convirtiendo a mayúsculas y colapsando espacios y caracteres especiales.
    /// </summary>
    public static string NormalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var preSanitized = PreSanitize(text);
        var normalized = preSanitized.Trim().Normalize(NormalizationForm.FormD);
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

    /// <summary>
    /// Compara dos tokens individuales permitiendo tolerancia a caracteres corruptos o aleatorios
    /// típicamente generados por mala codificación de la letra 'Ñ' o caracteres diacríticos.
    /// Exige coincidencia estricta en la letra inicial y en palabras cortas para evitar falsos positivos.
    /// </summary>
    public static bool TokensAreSimilar(string reqToken, string verToken)
    {
        if (string.Equals(reqToken, verToken, StringComparison.Ordinal))
            return true;

        // La primera letra debe coincidir siempre (las corrupciones de 'Ñ' y diacríticos nunca ocurren en la letra inicial)
        if (reqToken.Length == 0 || verToken.Length == 0 || reqToken[0] != verToken[0])
            return false;

        var maxLen = Math.Max(reqToken.Length, verToken.Length);
        var minLen = Math.Min(reqToken.Length, verToken.Length);

        // Diferencias de longitud mayores a 2 no se admiten en nombres
        if (maxLen - minLen > 2 || minLen <= 2)
            return false;

        var distance = ComputeLevenshteinDistance(reqToken, verToken);

        // Caso A: Omisión o sustitución de 1 caracter (ej: PENA vs PEA, NUNEZ vs NUEZ, NUNEZ vs NUXEZ, ACUNA vs ACUA)
        if (distance == 1 && minLen >= 3 && maxLen >= 4)
        {
            // Evita alternancias de género en nombres propios (ej: MARIA vs MARIO)
            if (reqToken.Length == verToken.Length &&
                (reqToken.EndsWith('A') && verToken.EndsWith('O') || reqToken.EndsWith('O') && verToken.EndsWith('A')))
            {
                return false;
            }

            return true;
        }

        // Caso B: Palabras largas (longitud >= 8, ej: RODRIGUEZ, GONZALEZ, FERNANDEZ) con similitud >= 75%
        if (distance <= 2 && minLen >= 7 && maxLen >= 8)
        {
            var similarity = 1.0 - ((double)distance / maxLen);
            if (similarity >= 0.75)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Calcula la distancia de edición de Levenshtein entre dos cadenas.
    /// </summary>
    public static int ComputeLevenshteinDistance(string s1, string s2)
    {
        if (string.Equals(s1, s2, StringComparison.Ordinal)) return 0;
        if (s1.Length == 0) return s2.Length;
        if (s2.Length == 0) return s1.Length;

        var v0 = new int[s2.Length + 1];
        var v1 = new int[s2.Length + 1];

        for (var i = 0; i <= s2.Length; i++)
            v0[i] = i;

        for (var i = 0; i < s1.Length; i++)
        {
            v1[0] = i + 1;

            for (var j = 0; j < s2.Length; j++)
            {
                var cost = s1[i] == s2[j] ? 0 : 1;
                v1[j + 1] = Math.Min(Math.Min(v1[j] + 1, v0[j + 1] + 1), v0[j] + cost);
            }

            for (var j = 0; j <= s2.Length; j++)
                v0[j] = v1[j];
        }

        return v1[s2.Length];
    }

    private static bool TokensMatch(string requested, string verified)
    {
        var reqTokens = Tokenize(requested);
        var verTokens = Tokenize(verified);

        if (reqTokens.Count == 0 || verTokens.Count == 0)
            return false;

        var reqSignificant = reqTokens.Where(t => !StopWords.Contains(t) && t.Length > 1).ToHashSet(StringComparer.Ordinal);
        var verSignificant = verTokens.Where(t => !StopWords.Contains(t) && t.Length > 1).ToHashSet(StringComparer.Ordinal);

        // Si ambos lados tienen tokens significativos (no stopwords), requerimos que al menos uno coincida o sea similar
        if (reqSignificant.Count > 0 && verSignificant.Count > 0)
        {
            return reqSignificant.Any(rt => verSignificant.Any(vt => TokensAreSimilar(rt, vt)));
        }

        // Si alguno o ambos tienen únicamente stopwords o palabras muy cortas, comparamos los tokens generales
        return reqTokens.Any(rt => verTokens.Any(vt => TokensAreSimilar(rt, vt)));
    }

    private static HashSet<string> Tokenize(string text)
    {
        var normalized = NormalizeText(text);
        return normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
    }
}
