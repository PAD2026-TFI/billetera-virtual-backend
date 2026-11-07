# Pagos con débito, domicilios y catálogo de API

## Audiencias y autenticación

Swagger muestra únicamente las rutas canónicas agrupadas por audiencia. Las rutas anteriores siguen funcionando como alias, conservando sus controles de acceso. No se redirigen POST ni se duplican operaciones financieras por el cambio de nombre. El registro y login son públicos; las rutas de salud también.

| Audiencia | Rutas | Credencial |
| --- | --- | --- |
| Acceso | `/api/v1/auth/register`, `/api/v1/auth/login` | Públicas |
| Cliente | `/api/v1/me/*` | JWT de cliente |
| Comercio / app externa | `/api/v1/integrations/*` | X-Api-Key; `/register` es público |
| Checkout | `/api/v1/public/*` | Público para consulta y tarjeta externa simulada; JWT para pagar desde billetera o tarjeta asociada |
| Administración | `/api/v1/admin/*` | JWT Admin; el alta requiere la clave de configuración existente |
| Salud | `/health/live`, `/health/ready` | Públicas |

Una API Key no permite operar tarjetas del usuario, ni un JWT de cliente permite operar la integración de un comercio. El encabezado `X-Api-Key` no autoriza a descontar dinero de cualquier cliente: el titular debe autenticar su propio pago.

## Débito asociado a la billetera

Antes, PayWithSavedCardAsync reconstruía un PAN de prueba y acreditaba al comercio con DevelopmentFunding. Ahora una tarjeta asociada de tipo Debit consume el saldo de la billetera de su titular.

1. El cliente obtiene sus tarjetas con `GET /api/v1/me/cards`.
2. El comercio crea un link mediante `POST /api/v1/integrations/payment-links` con su API Key.
3. El cliente paga usando el ID del link y el ID de su tarjeta:

```http
POST /api/v1/me/payment-links/{paymentLinkId}/card-payments
Authorization: Bearer {JWT_CLIENTE}
Idempotency-Key: compra-123
Content-Type: application/json

{"cardId":"ID_DE_LA_TARJETA_DEBIT"}
```

No se envía PAN ni CVV para el débito interno: la identidad proviene del JWT y se comprueba la titularidad de CardId. Si se envía CVV opcional, el contrato HTTP conserva la validación de formato de tres dígitos. La tarjeta debe estar activa y vigente. Todas las tarjetas asociadas de tipo Debit consumen la misma billetera del cliente, que ya es única por titular.

El almacenamiento SQL confirma en una sola transacción el débito, el crédito al comercio, la transferencia, sus dos movimientos, Payment con PayerWalletId/TransferId y el estado del link. No utiliza una carga de fondos simulada. Bloquea las billeteras en el orden ya usado para transferencias y evita sobregiros. Se conserva el límite de transferencia de 0,01 a 780.400 ARS por operación.

`Idempotency-Key` es obligatorio para débito, admite hasta 128 caracteres y debe conservarse al reintentar la misma compra. Su ámbito es el comercio, como el índice de Payments existente. El hash incluye link, tarjeta exacta, usuario e importe, no solo las últimas cuatro cifras. Un reintento confirmado devuelve el mismo pago con `Idempotency-Replayed: true`, aunque ya se haya agotado el saldo. Otra tarjeta o link con la misma clave produce `409 IDEMPOTENCY_CONFLICT`. Un link de uso único no admite otro cobro, ni siquiera con otra clave. Ante falta de saldo no se consumen la clave ni el link.

Errores relevantes: `409 INSUFFICIENT_FUNDS`, `409 IDEMPOTENCY_CONFLICT`, `409 CONCURRENCY_CONFLICT`, `404 SOURCE_WALLET_NOT_FOUND`, `400 SAME_WALLET`. Las billeteras inactivas o no disponibles conservan las guardas de transferencia.

El checkout público también acepta `paymentMethod: "Card"` y `cardId` con JWT y la misma cabecera de idempotencia, y delega al mismo flujo. No admite enviar simultáneamente cardId y el objeto card con PAN. El checkout web alojado usa la ruta canónica, informa que débito consume saldo y conserva la clave en memoria durante reintentos de la misma compra/tarjeta.

Los pagos con `card` (PAN/CVV) y las tarjetas guardadas de crédito conservan el simulador externo. El sistema no almacena PAN, por lo que no puede identificar de forma confiable una tarjeta interna por sus últimas cuatro cifras. Para pagar con la tarjeta de la billetera se usa siempre cardId; no se deduce titularidad a partir de coincidencias de PAN parcial. No se agregó una red de autorización externa ni cobros físicos por PAN.

## Domicilio para tarjeta física

Antes, `POST /api/v1/cards/requests` recibía solo `addressId` y tipo de tarjeta. Sin addressId buscaba una dirección válida del usuario; si no había ninguna fallaba. No existía una entrada de domicilio completo en ese endpoint.

Ahora se puede llamar a `POST /api/v1/me/physical-card-orders` con:

```json
{
  "type": "Debit",
  "deliveryAddress": {
    "street": "Av. Corrientes",
    "number": "1234",
    "city": "CABA",
    "state": "Buenos Aires",
    "postalCode": "1043",
    "country": "Argentina"
  }
}
```

El servidor asigna el domicilio al usuario del JWT. No acepta dueño, estado de validación ni coordenadas enviados por el cliente. Guarda Address y CardRequest en el mismo SaveChanges/transacción y devuelve el ID del domicilio, el texto estructurado y ValidationStatus junto al estado del pedido. Type acepta Debit/Credit; se conservan los valores numéricos 0/1 por compatibilidad.

También admite `{ "type": "Debit", "addressId": "ID_PROPIO" }`. AddressId y deliveryAddress son mutuamente excluyentes. El ID de otro titular devuelve 404. Si no se envía ninguno, conserva la selección de la dirección válida más reciente del usuario; si no existe devuelve 400 con una indicación para cargar el domicilio.

No hay proveedor geográfico registrado todavía. Por eso un domicilio nuevo queda Pending y el pedido AwaitingAddressValidation. No se marca como geográficamente válido por tener campos completos. En este estado el dominio impide Processing/Issued y permite cancelar o rechazar. El futuro proceso de validación debe marcar Address como válido y llamar ConfirmAddressValidation sobre ese mismo pedido/domicilio para pasar a Pending. Ese worker/endpoint administrativo y el transportista siguen pendientes. Si se registra IAddressGeocoder en DI, la solicitud usa su resultado y puede nacer Pending con domicilio validado.

`GET /api/v1/me/physical-card-orders/{id}` devuelve el pedido y el domicilio exclusivamente a su dueño. La ruta anterior `/api/v1/cards/requests/{id}/status` sigue funcionando. Se conserva AddressId y una dirección nueva por carga; no se agregó edición de domicilios ni snapshot independiente del envío en esta entrega.

## Rutas canónicas principales

| Método | Ruta | Función |
| --- | --- | --- |
| GET / PUT | `/api/v1/me/profile` | Perfil propio |
| GET / POST | `/api/v1/me/wallet` | Saldo / creación |
| PATCH | `/api/v1/me/wallet/alias` | Cambiar alias |
| GET | `/api/v1/me/wallet/movements` | Historial propio |
| POST | `/api/v1/me/transfers/by-cvu` | Transferir por CVU |
| POST | `/api/v1/me/transfers/by-alias` | Transferir por alias |
| POST | `/api/v1/me/transfers/by-wallet-id` | Transferir por ID |
| GET / POST | `/api/v1/me/cards` | Consultar / asociar tarjeta |
| POST | `/api/v1/me/physical-card-orders` | Solicitar plástico con domicilio |
| GET | `/api/v1/me/physical-card-orders/{id}` | Consultar pedido y domicilio |
| POST | `/api/v1/me/payment-links/{paymentLinkId}/card-payments` | Pago con tarjeta asociada |
| POST | `/api/v1/integrations/register` | Alta de comercio y API Key |
| GET | `/api/v1/integrations/profile` | Perfil de la integración |
| GET | `/api/v1/integrations/wallet` | Billetera comercial |
| GET | `/api/v1/integrations/wallet/movements` | Movimientos comerciales |
| PUT / DELETE | `/api/v1/integrations/webhook` | Configurar / quitar webhook |
| POST | `/api/v1/integrations/transfers` | Transferir saldo de la integración |
| POST / GET | `/api/v1/integrations/payment-links[/{id}]` | Crear / consultar link de cobro |
| GET | `/api/v1/integrations/received-payments[/{paymentId}]` | Listar / consultar cobros recibidos |
| GET | `/api/v1/public/payment-links/{token}` | Consultar link público |
| GET | `/api/v1/public/payment-links/{token}/qr` | Obtener QR |
| POST | `/api/v1/public/checkout/payments` | Resolver un pago de checkout |
| POST | `/api/v1/admin/wallet-funding/by-cvu` | Cargar saldo de prueba por CVU |
| POST | `/api/v1/admin/wallet-funding/by-alias` | Cargar saldo de prueba por alias |
| POST | `/api/v1/admin/wallet-funding/by-wallet-id` | Cargar saldo de prueba por ID |

Los corchetes de la tabla indican dos rutas distintas, con o sin identificador; no forman parte de la URL. El mapa exacto de alias se mantiene en Wallet.Api/Routing/ApiEndpointCatalog.cs. Swagger toma la seguridad de los metadatos de autorización reales, incluyendo las excepciones públicas, en lugar de adivinar por prefijos.

## Despliegue y verificación

Aplicar la migración SupportPendingCardDeliveryAddress al entorno elegido antes de usar los pedidos nuevos: amplía Status de nvarchar(20) a nvarchar(32). Up no borra datos; Down requiere resolver antes estados de más de 20 caracteres para no truncarlos. También debe estar aplicada AssociateAddressesWithUsers de la revisión anterior.

No se actualizaron las bases de la aplicación en esta tarea. Las migraciones se probaron en una base exclusiva SQL LocalDB. Reiniciar la API tras compilar para ver las nuevas rutas y grupos de Swagger. Los ejemplos ejecutables están en Wallet.Api/Wallet.Api.http.

### Resultados de verificación

- Dominio: 221 pruebas correctas.
- Aplicación/API sin SQL: 208 pruebas correctas.
- Selección SQL de débito y transferencias: 21 pruebas correctas, sin omisiones; incluye 11 escenarios nuevos de pago/domicilio, 7 de transferencia y 3 de concurrencia coordinada.
- Se comprobó débito/crédito y dos movimientos, falta de saldo sin efectos, reintentos, claves compartidas entre links distintos, contención de saldo, fallo al insertar Payment con rollback, confirmación perdida de commit, compatibilidad de rutas, permisos por audiencia y domicilio persistido por titular.
- Se adaptó la limpieza de la fixture compartida a la herencia TPT y el punto de sincronización a la columna OwnerId actual para poder ejecutar esas pruebas SQL.
- Compilación correcta sin advertencias ni errores; modelo EF sin cambios pendientes respecto de migraciones; scripts JavaScript del checkout parseados correctamente; diff sin errores de espacios.
- La base de la aplicación y la API en ejecución no fueron modificadas ni reiniciadas.
