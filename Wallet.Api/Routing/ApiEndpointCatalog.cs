using System.Text.RegularExpressions;

namespace Wallet.Api.Routing;

public static class ApiEndpointCatalog
{
    // Legacy routes remain callable; OpenAPI presents only the canonical route.
    public static readonly IReadOnlyDictionary<string, string> Routes = new Dictionary<string, string>
    {
        ["api/users/me"] = "api/v1/me/profile",
        ["api/users/me/profile"] = "api/v1/me/profile",
        ["api/wallets/me"] = "api/v1/me/wallet",
        ["api/wallets"] = "api/v1/me/wallet",
        ["api/wallets/alias"] = "api/v1/me/wallet/alias",
        ["api/wallets/me/movements"] = "api/v1/me/wallet/movements",
        ["api/transfers/cvu"] = "api/v1/me/transfers/by-cvu",
        ["api/transfers/alias"] = "api/v1/me/transfers/by-alias",
        ["api/transfers/wallet-id"] = "api/v1/me/transfers/by-wallet-id",
        ["api/v1/cards"] = "api/v1/me/cards",
        ["api/v1/cards/requests"] = "api/v1/me/physical-card-orders",
        ["api/v1/cards/requests/{id:guid}/status"] = "api/v1/me/physical-card-orders/{id:guid}",
        ["api/v1/checkout/{paymentLinkId:guid}/pay"] = "api/v1/me/payment-links/{paymentLinkId:guid}/card-payments",
        ["api/v1/merchants/register"] = "api/v1/integrations/register",
        ["api/v1/gateway/me"] = "api/v1/integrations/profile",
        ["api/v1/gateway/webhook"] = "api/v1/integrations/webhook",
        ["api/v1/gateway/wallet"] = "api/v1/integrations/wallet",
        ["api/v1/gateway/payments"] = "api/v1/integrations/transfers",
        ["api/v1/gateway/movements"] = "api/v1/integrations/wallet/movements",
        ["api/v1/gateway/checkout-payments"] = "api/v1/integrations/received-payments",
        ["api/v1/gateway/checkout-payments/{paymentId:guid}"] = "api/v1/integrations/received-payments/{paymentId:guid}",
        ["api/v1/payment-intents"] = "api/v1/integrations/payment-links",
        ["api/v1/payment-intents/{id:guid}"] = "api/v1/integrations/payment-links/{id:guid}",
        ["api/v1/checkout/links/{token}"] = "api/v1/public/payment-links/{token}",
        ["api/v1/checkout/links/{token}/qr"] = "api/v1/public/payment-links/{token}/qr",
        ["api/v1/checkout/pay"] = "api/v1/public/checkout/payments",
        ["api/admin/register"] = "api/v1/admin/register",
        ["api/admin/funds/cvu"] = "api/v1/admin/wallet-funding/by-cvu",
        ["api/admin/funds/alias"] = "api/v1/admin/wallet-funding/by-alias",
        ["api/admin/funds/wallet-id"] = "api/v1/admin/wallet-funding/by-wallet-id"
    };

    public static string OpenApiPath(string route) => "/" + Regex.Replace(route.TrimStart('/'), @":\w+(?=})", "");

    public static string Group(string route) => route switch
    {
        var p when p.StartsWith("/api/v1/auth/") => "01 - Acceso - registro e inicio de sesión",
        var p when p.StartsWith("/api/v1/me/") => "02 - Clientes - mi billetera y mis tarjetas (JWT)",
        var p when p.StartsWith("/api/v1/integrations/") => "03 - Apps externas - comercios e integraciones (API Key)",
        var p when p.StartsWith("/api/v1/public/") => "04 - Checkout público - consulta y pago",
        var p when p.StartsWith("/api/v1/admin/") => "05 - Administración (JWT Admin)",
        _ => "06 - Salud del servicio"
    };

    public static string Summary(string controller, string action) => (controller, action) switch
    {
        ("Auth", "Login") => "Iniciar sesión y obtener JWT",
        ("Users", "Register") => "Registrar cliente con billetera y tarjeta virtual",
        ("Users", "GetMe") => "Consultar mi perfil",
        ("Users", "Update") => "Actualizar mi perfil",
        ("Wallets", "GetMine") => "Consultar mi billetera y saldo",
        ("Wallets", "Create") => "Crear mi billetera",
        ("Wallets", "ModifyAlias") => "Cambiar el alias de mi billetera",
        ("Movements", _) => "Consultar los movimientos de mi billetera",
        ("Transfers", "CreateByCvu") => "Transferir desde mi billetera a un CVU",
        ("Transfers", "CreateByAlias") => "Transferir desde mi billetera a un alias",
        ("Transfers", "CreateByWalletId") => "Transferir desde mi billetera a otra billetera",
        ("Cards", "GetUserCards") => "Consultar mis tarjetas asociadas",
        ("Cards", "AddCard") => "Asociar una tarjeta a mi cuenta",
        ("CardRequests", "CreateRequest") => "Solicitar tarjeta física indicando domicilio de entrega",
        ("CardRequests", "GetStatus") => "Consultar mi pedido de tarjeta física y su domicilio",
        ("PaymentIntents", "Create") => "Crear link de cobro para el comercio autenticado",
        ("PaymentIntents", "GetById") => "Consultar mi link de cobro",
        ("Checkout", "GetLinkByToken") => "Consultar un link de pago público",
        ("Checkout", "GetQrCode") => "Obtener QR de un link de pago",
        ("Checkout", "Pay") => "Pagar un link con tarjeta externa, tarjeta asociada o billetera",
        ("Checkout", "PayWithSavedCard") => "Pagar con mi tarjeta asociada; débito descuenta de mi billetera",
        ("Gateway", "RegisterMerchant") => "Registrar comercio y obtener API Key",
        ("Gateway", "GetMe") => "Consultar perfil de mi integración",
        ("Gateway", "GetWallet") => "Consultar billetera y saldo de la integración",
        ("Gateway", "ProcessPayment") => "Transferir desde la billetera de la integración",
        ("Gateway", "GetMovements") => "Consultar movimientos de la integración",
        ("Gateway", "GetCheckoutPaymentById") => "Consultar un cobro recibido",
        ("Gateway", "GetReceivedCheckoutPayments") => "Listar cobros recibidos",
        ("Gateway", "ConfigureWebhook") => "Configurar webhook de la integración",
        ("Gateway", "DeactivateWebhook") => "Eliminar webhook de la integración",
        ("Admin", "RegisterAdmin") => "Registrar administrador con clave de configuración",
        ("Admin", "FundByCvu") => "Cargar saldo de prueba por CVU",
        ("Admin", "FundByAlias") => "Cargar saldo de prueba por alias",
        ("Admin", "FundByWalletId") => "Cargar saldo de prueba por ID de billetera",
        ("Health", "Live") => "Comprobar disponibilidad del proceso",
        ("Health", "Ready") => "Comprobar conexión y disponibilidad del servicio",
        _ => action
    };
}
