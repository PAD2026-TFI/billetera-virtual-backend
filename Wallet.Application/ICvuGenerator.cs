namespace Wallet.Application;

/// <summary>
/// Generador de Claves Virtuales Uniformes (CVU) bancarias/virtuales de 22 dígitos con prefijo oficial.
/// </summary>
public interface ICvuGenerator
{
    /// <summary>
    /// Genera un CVU de 22 dígitos numéricos que inicia con '00001234'.
    /// </summary>
    string Generate();
}
