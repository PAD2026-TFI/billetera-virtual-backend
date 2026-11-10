namespace Wallet.Infrastructure.OpenRenaper;

/// <summary>
/// Opciones de configuración para la conexión con el servicio OpenRENAPER.
/// </summary>
public sealed class OpenRenaperOptions
{
    public const string SectionName = "OpenRenaper";

    public string BaseUrl { get; set; } = "https://openrenaper.local";

    public int TimeoutSeconds { get; set; } = 5;
}
