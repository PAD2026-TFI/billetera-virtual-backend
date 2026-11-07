namespace Wallet.Application;

/// <summary>
/// Generador de alias mnemotécnicos únicos para transferencias monetarias.
/// </summary>
public interface IAliasGenerator
{
    /// <summary>
    /// Genera un alias válido compuesto por palabras separadas por puntos.
    /// </summary>
    string Generate();
}
