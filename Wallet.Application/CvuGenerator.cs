using System.Security.Cryptography;

namespace Wallet.Application;

/// <summary>
/// Implementación criptográfica para generar CVUs aleatorios con prefijo '00001234'.
/// </summary>
public sealed class CvuGenerator : ICvuGenerator
{
    private const string Prefix = "00001234";
    private const int SuffixLength = 14;

    public string Generate()
    {
        Span<char> suffix = stackalloc char[SuffixLength];
        Span<byte> randomBytes = stackalloc byte[SuffixLength];
        RandomNumberGenerator.Fill(randomBytes);

        for (var i = 0; i < SuffixLength; i++)
        {
            suffix[i] = (char)('0' + (randomBytes[i] % 10));
        }

        return $"{Prefix}{suffix.ToString()}";
    }
}
