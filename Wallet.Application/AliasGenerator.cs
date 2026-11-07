using System.Security.Cryptography;

namespace Wallet.Application;

/// <summary>
/// Generador de alias bancarios aleatorios a partir de un diccionario de palabras en español.
/// </summary>
public sealed class AliasGenerator : IAliasGenerator
{
    private static readonly string[] Words =
    [
        "sol", "luna", "rio", "cielo", "mar", "nube", "viento", "bosque", "puma", "aguila",
        "palma", "roble", "sauce", "cedro", "verde", "azul", "rojo", "oro", "plata", "bronce",
        "valle", "monte", "senda", "puente", "faro", "onda", "estrella", "andes", "pampa", "delta"
    ];

    public string Generate()
    {
        Span<byte> randomBytes = stackalloc byte[3];
        RandomNumberGenerator.Fill(randomBytes);

        var first = Words[randomBytes[0] % Words.Length];
        var second = Words[randomBytes[1] % Words.Length];
        var third = Words[randomBytes[2] % Words.Length];

        return $"{first}.{second}.{third}";
    }
}
