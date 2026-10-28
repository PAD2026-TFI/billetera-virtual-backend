# Wallet · Billetera y pagos sandbox

Desarrollo el backend de una billetera virtual con ASP.NET Core. Implemento identidad, billeteras ARS, transferencias, movimientos y el flujo de cargas sandbox mediante Mercado Pago. Verifico por separado las pruebas controladas, SQL y la integración externa; no considero una prueba omitida como aprobada.

Para el Incremento 2 incorporo creación de billeteras ARS, consulta de saldo y transferencias entre usuarios.

## Arquitectura

Organizo la solución en seis proyectos:

- `Wallet.Domain`: entidades, invariantes, perfil y versión de sesión.
- `Wallet.Application`: casos de uso, DTOs y contratos.
- `Wallet.Infrastructure`: EF Core, SQL, repositorios y hashing.
- `Wallet.Api`: endpoints, JWT, autorización y configuración.
- `Wallet.Tests`: pruebas de dominio, hashing, contratos HTTP y SQL.
- `Wallet.DevelopmentTools`: herramienta local de fondos de desarrollo.

Mantengo Domain independiente de ASP.NET Core y EF Core. Desde Application referencio Domain; desde Infrastructure referencio Application y Domain; desde Api referencio Application e Infrastructure.

## Registro de movimientos

Registro conjuntamente un débito y un crédito por cada transferencia completada. Compruebo que las billeteras y monedas correspondan a la transferencia, y que el saldo posterior del crédito no sea inferior al importe acreditado. Cada movimiento conserva importe positivo, saldo posterior, fecha UTC y referencia de origen.

Guardo ambos movimientos junto con saldos y transferencia. No expongo creación, modificación ni eliminación manual de movimientos. Mantengo la fuente DevelopmentFunding para cargas de prueba y evito duplicar movimientos mediante restricciones SQL e idempotencia. La verificación SQL de TX-002 se ejecuta con `dotnet test Wallet.slnx -c Release --filter "FullyQualifiedName~SqlMovementRegistrationTests"` sobre una base aislada y migrada.

## Requisitos

Uso .NET 9 y un SDK compatible con `global.json`: versión base 9.0.300 y política `latestPatch`. En el entorno cloud validé el SDK 9.0.319. Uso paquetes Microsoft y la herramienta `dotnet-ef` en la versión 9.0.21.

Mantengo compatibles el framework, los paquetes, las herramientas y las pruebas al actualizar el SDK. Antes de continuar más allá del fin de soporte de .NET 9, previsto para el 10/11/2026, tengo pendiente planificar la actualización a .NET 10 y un IDE compatible.

## Ejecución de la base técnica

Desde la carpeta que contiene `Wallet.slnx` ejecuto:

```powershell
dotnet --version
dotnet restore Wallet.slnx
dotnet build Wallet.slnx -c Release
dotnet test Wallet.slnx -c Release
dotnet dev-certs https --trust
dotnet run --project Wallet.Api --launch-profile https
```

Uso la confianza del certificado HTTPS en mi entorno local cuando la plataforma lo permite. Para mi sesión cloud preparada, primero activo el SDK con `source /workspace/.setup/wallet-env.sh` y puedo comprobar la base técnica por HTTP local:

```sh
ASPNETCORE_ENVIRONMENT=Development dotnet run --project Wallet.Api -c Release --no-build --no-restore --no-launch-profile --urls http://localhost:5081
```

Con el perfil HTTPS consulto `/health/live` en el puerto 7081; para la comprobación HTTP uso el puerto 5081. En Development consulto el contrato en `/openapi/v1.json` y Swagger UI en `/swagger`.

Mantengo `Identity:Enabled=false` como configuración inicial. En ese estado verifico:

| Solicitud | Resultado esperado |
|---|---|
| `GET /health/live` | 200, `{"status":"Healthy"}` |
| `GET /health/ready` | 503, `{"status":"Unhealthy"}` |
| Rutas `/api` | 503, código `IDENTITY_NOT_CONFIGURED` |

Con este arranque compruebo la base técnica; no considero validado un registro ni la persistencia.

## Configuración de identidad y persistencia

Para habilitar identidad necesito una base SQL accesible, el esquema migrado y una clave JWT externa. Uso WalletDev en LocalDB para mis pruebas manuales y WalletTests como base aislada de pruebas. En CI uso SQL Server 2022 descartable. Mantengo EF Core y las mismas migraciones en todos esos ambientes; Azure SQL queda como destino futuro. No intento ejecutar LocalDB de Windows dentro del contenedor Linux.

Guardo los valores reales mediante User Secrets de `Wallet.Api` en Development o mediante configuración externa. Uso este esquema como referencia, reemplazando los marcadores fuera del repositorio:

```json
{
  "Identity:Enabled": true,
  "ConnectionStrings:DefaultConnection": "<CONEXION SQL REAL>",
  "Jwt:Key": "<CLAVE ALEATORIA BASE64>"
}
```

Para Azure SQL mantengo `Encrypt=True;TrustServerCertificate=False`. Genero una clave aleatoria de al menos 32 bytes y la represento en Base64. En PowerShell puedo generarla localmente así:

```powershell
$jwtBytes = New-Object byte[] 32
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($jwtBytes)
$rng.Dispose()
[Convert]::ToBase64String($jwtBytes)
```

Guardo el resultado de forma segura; no lo copio a Git ni a logs. Reconozco que User Secrets evita versionar secretos, pero no es una bóveda cifrada.

También puedo configurar `Identity__Enabled`, `ConnectionStrings__DefaultConnection`, `Jwt__Key`, `Jwt__Issuer` y `Jwt__Audience`. Al desplegar ajusto `AllowedHosts` al hostname real.

## Migraciones

Trabajo sobre una base vacía de desarrollo autorizada. Para aplicar la migración existente ejecuto:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project Wallet.Infrastructure --startup-project Wallet.Api
dotnet ef migrations script 0 InitialIdentity --project Wallet.Infrastructure --startup-project Wallet.Api --output migration.sql
# Reviso migration.sql antes de aplicarlo a la base elegida.
dotnet ef database update --project Wallet.Infrastructure --startup-project Wallet.Api
```

No vuelvo a crear `InitialIdentity` ni aplico migraciones automáticamente al arrancar. Distingo los permisos de migración de los permisos de ejecución. Antes de cualquier rollback evalúo la pérdida de datos: `database update 0` elimina la tabla de usuarios.

Con identidad habilitada compruebo que `/health/ready` responde 200 cuando el esquema está disponible y 503 cuando la base o el esquema no están accesibles. Si falta conexión o clave JWT, rechazo el inicio sin inventar configuración.

## Contratos HTTP

Expongo estos endpoints para el incremento:

| Método y ruta | Acceso | Éxito | Errores principales |
|---|---|---|---|
| `GET /health/live` | Público | 200 | No consulta SQL |
| `GET /health/ready` | Público | 200 | 503 |
| `POST /api/users` | Público | 201 + `Location: /api/users/me` | 400, 409, 415, 429, 503 |
| `POST /api/auth/login` | Público | 200 + token Bearer | 400, 401, 415, 429, 503 |
| `GET /api/wallets/me/movements` | JWT + Client | 200, historial paginado | 400, 401, 403, 404, 503 |
| `POST /api/transfers` | JWT + Client, Idempotency-Key | 201 nueva, 200 repetida | 400, 401, 403, 404, 409, 422, 503 |
| `GET /api/wallets/me` | JWT + Client | 200 con billetera y saldo actual | 401, 403, 404, 503 |
| `POST /api/wallets` | JWT + Client, sin cuerpo | 201 al crear, 200 si existe | 400, 401, 403, 503 |
| `GET /api/users/me` | JWT + Client | 200 | 401, 403, 503 |
| `PUT /api/users/me/profile` | JWT + Client | 200 | 400, 401, 403, 409, 415, 503 |

Para registro recibo `firstName`, `lastName`, `email` y `password`; para login, `email` y `password`. En la edición de perfil permito únicamente nombre y apellido. Devuelvo `id`, `firstName`, `lastName` y `email`, sin contraseñas ni hashes. Rechazo propiedades adicionales como `role`, `userId` o `balance`.

Uso nombres obligatorios de hasta 100 caracteres, correo de hasta 254 y contraseña de 12 a 128 caracteres, no compuesta sólo por espacios. Normalizo el correo con `Trim + ToUpperInvariant` para comprobar unicidad y conservo la contraseña sin transformaciones. Limito el cuerpo de las solicitudes a 16 KiB.

Para consultar mi saldo envío `GET /api/wallets/me` con el token Bearer, sin cuerpo ni identificador de usuario. Recibo `id`, `balance`, `currency` y `createdAt`. Si todavía no creé mi billetera, recibo 404 con código `WALLET_NOT_FOUND`; la consulta no la crea. Ante indisponibilidad recibo 503 en lugar de un saldo cero ficticio. Uso respuestas `no-store` y no modifico datos al consultar.

## Historial de movimientos

Consulto `GET /api/wallets/me/movements` con mi token Bearer para recuperar únicamente los movimientos de mi billetera. Recibo `items` y `nextCursor`; cada movimiento informa identificador, tipo, importe positivo, moneda, saldo posterior histórico, fuente, referencia y fecha UTC. Incluyo cargas de desarrollo y transferencias.

Uso `limit=20` por defecto, entre 1 y 100. Para continuar envío el `nextCursor` recibido como query `cursor`, codificado para URL. Ordeno de más reciente a más antiguo, con identificador como desempate. No prometo una instantánea de múltiples páginas mientras se registran nuevas operaciones.

Protejo los cursores mediante ASP.NET Core Data Protection y los vinculo a la billetera. No los edito ni reutilizo con otra cuenta; un cursor inválido recibe 400. Conservo las claves de Data Protection para mantener válidos los cursores tras un reinicio; en varias instancias necesito compartir el anillo de claves. No guardo esas claves en Git.

Si no tengo movimientos recibo 200 con `items: []` y `nextCursor: null`; sin billetera recibo 404 `WALLET_NOT_FOUND`. Una falla SQL devuelve 503, no una colección vacía. La consulta utiliza no-store y no modifica el saldo ni crea recursos. No agrego filtros por fecha, tipo ni exportación en este alcance.

## Transferencias entre billeteras

Para enviar ARS uso `POST /api/transfers` con mi token Bearer y un encabezado obligatorio `Idempotency-Key` que contiene un UUID. El origen se obtiene de mi sesión; en el cuerpo envío únicamente:

```json
{
  "destinationWalletId": "<UUID_BILLETERA_DESTINO>",
  "amount": 300.00
}
```

Necesito dos billeteras distintas con titulares activos. Acepto importes desde 0,01 hasta 780400 ARS inclusive, sin fracciones de centavo. Compruebo saldo suficiente y capacidad del destino dentro de la transacción.

Recibo 201 al completar una transferencia nueva y 200 al repetir la misma clave con el mismo contenido. La respuesta contiene `id`, `sourceWalletId`, `destinationWalletId`, `amount`, `currency`, `status` y `createdAt`. No incluyo datos personales del destinatario.

En Swagger completo automáticamente la clave al activar Try it out. Uso el botón **Nueva clave** para otra intención de transferencia; Execute conserva la clave actual para reintentar sin duplicar la operación. Puedo editarla manualmente. Fuera de Swagger, genero una clave por intención de transferencia y la conservo en los reintentos. Distingo la recuperación de una operación mediante el encabezado `Idempotency-Replayed: true`; una creación informa `false`. Misma clave con otro importe o destino devuelve 409 `IDEMPOTENCY_CONFLICT`. La clave tiene alcance por billetera de origen y no vence automáticamente. Recuperar una transferencia completada no vuelve a exigir el saldo original ni vuelve a mover dinero.

Debito el origen, acredito el destino y guardo la transferencia y dos movimientos dentro de una única transacción SQL. Bloqueo las billeteras en un orden estable y uso RowVersion como protección adicional. Si fallo antes del commit, revierto todos los cambios; si pierdo su confirmación, recupero el resultado con la misma clave. No borro movimientos para simular una reversión después del commit.

Distingo `INVALID_INPUT`/`INVALID_AMOUNT`/`SAME_WALLET` (400), sesión inválida (401), falta de permiso (403), `SOURCE_WALLET_NOT_FOUND` (404), `DESTINATION_UNAVAILABLE` (422), `INSUFFICIENT_FUNDS`/`DESTINATION_BALANCE_LIMIT`/`IDEMPOTENCY_CONFLICT`/`CONCURRENCY_CONFLICT` (409) e indisponibilidad SQL (503). Si no conozco el resultado por un fallo de red, no genero otra clave para repetir la misma intención.

Antes de probar aplico las migraciones con la conexión y configuración de desarrollo de la API:

```sh
dotnet tool restore
dotnet ef database update --project Wallet.Infrastructure --startup-project Wallet.Api
```

La migración `Transfers` requiere la anterior `DevelopmentFunding` de TECH-001. Conservo las cargas existentes y amplío la referencia de movimientos con TransferId y una restricción que exige una única fuente. No incorporo referencias a pagos hasta implementar esa integración.

Para la prueba manual preparo dos cuentas y billeteras, acredito fondos al emisor con la herramienta local, obtengo su token y ejecuto la transferencia desde Swagger. Consulto el saldo con la sesión de cada titular: una transferencia de 300 sobre saldos iniciales de 1000 y 0 debe dejarlos en 700 y 300. Repetir la misma clave devuelve la operación original sin cambiar esos saldos.

Para validar atomicidad y concurrencia necesito SQL real, una base exclusiva de pruebas con todas las migraciones de pagos aplicadas y `WALLET_TEST_CONNECTION` configurada de forma segura:

```sh
dotnet test Wallet.slnx -c Release --filter "FullyQualifiedName~SqlTransferTests"
```

No considero demostrados rollback, prevención de doble gasto ni concurrencia sólo por las pruebas HTTP con un doble del store. Mantengo pendientes las pruebas SQL cuando se omiten por falta de conexión. El rollback de la migración elimina el esquema de transferencias y restringe de nuevo los movimientos: no lo ejecuto sobre transferencias confirmadas como mecanismo de devolución.

## Fondos de prueba en desarrollo

Para preparar fondos sin pasarela uso la herramienta local `Wallet.DevelopmentTools`. No expongo esta operación mediante la API. Necesito una billetera existente con titular activo y una base exclusiva de desarrollo; no uso fondos reales ni una conexión productiva.

Comparto la conexión de User Secrets de `Wallet.Api` (`pad2026-wallet-initial`) o la suministro mediante `ConnectionStrings__DefaultConnection`. No la escribo en argumentos ni logs. Aplico primero la migración `DevelopmentFunding` con la configuración de desarrollo de la API:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet tool restore
dotnet ef database update --project Wallet.Infrastructure --startup-project Wallet.Api
```

Para cada intención de carga genero una clave una sola vez. En PowerShell ejecuto:

```powershell
$env:DOTNET_ENVIRONMENT = "Development"
$fundingKey = [guid]::NewGuid().ToString()
dotnet run --project Wallet.DevelopmentTools -c Release -- fund-wallet --wallet-id "<ID_BILLETERA>" --amount 1000.00 --idempotency-key "$fundingKey" --server "<SERVIDOR_EXACTO>" --database "<BASE_DESARROLLO>" --confirm-development-database
```

Reemplazo los marcadores por el identificador de la billetera y los nombres exactos de servidor y base de mi conexión. La herramienta exige coincidencia de ambos nombres, rechaza bases del sistema y exige `DOTNET_ENVIRONMENT=Development`. Esa confirmación es mi responsabilidad: etiquetar el proceso como Development no convierte una base productiva en una base de pruebas.

Uso punto decimal, sin separador de miles, e importes positivos sin fracciones de centavo. El límite de $780.400 corresponde a transferencias, no a estas cargas. Respeto la capacidad del saldo `decimal(18,2)`.

La respuesta JSON informa `id`, `walletId`, `amount`, `currency`, `createdAt`, `balanceAfter` y `created`. La primera ejecución devuelve `created=true`; repetir el comando con la misma clave y contenido devuelve `created=false` sin acreditar nuevamente. Para reintentar conservo `$fundingKey`: no vuelvo a generarlo. Si cambio importe o billetera con la misma clave, recibo `IDEMPOTENCY_CONFLICT`.

Registro la carga y su movimiento Credit/DevelopmentFunding, y sumo el saldo, dentro de la misma transacción SQL. Si fallo antes del commit, revierto todos los cambios. Si pierdo la confirmación del commit, reintento con la misma clave para recuperar el resultado; no asumo que hubo rollback. El `balanceAfter` de una repetición es el saldo histórico de esa carga; consulto `GET /api/wallets/me` para conocer el actual.

No registro la herramienta como servicio de la API. Por ahora sólo implemento movimientos de cargas de desarrollo; las referencias y restricciones de transferencias y pagos se ampliarán con sus respectivas funcionalidades. No creo sus tablas anticipadamente.

Consulto la ayuda sin acceder a SQL:

```sh
dotnet run --project Wallet.DevelopmentTools -c Release -- --help
```

Para ejecutar las pruebas SQL de cargas uso una base aislada, previamente migrada, configurada mediante `WALLET_TEST_CONNECTION`:

```sh
dotnet test Wallet.slnx -c Release --filter "FullyQualifiedName~SqlDevelopmentFundingTests"
```

Estas pruebas cubren repetición, conflicto de clave, concurrencia, rollback tras una escritura y recuperación de una confirmación perdida después del commit. Las omitidas no cuentan como aprobadas.

## Autenticación y autorización

Emito tokens HS256 de quince minutos con tolerancia de reloj de treinta segundos. Valido firma, algoritmo, emisor, audiencia, vigencia y claims de identidad. En este emisor uso UUID no vacío para `sub` y `jti`, y una versión de sesión no negativa.

En cada solicitud privada consulto el usuario persistido y compruebo estado activo, rol y versión de sesión. Asigno `Client` desde el servidor y no permito que el cliente elija su rol. Al incrementar `SessionVersion` o deshabilitar la cuenta invalido los tokens anteriores en las siguientes comprobaciones. No expongo todavía endpoints de logout, administración ni refresh tokens.

Comparto un límite de diez intentos de registro/login por IP y minuto. Uso respuestas `no-store`, errores controlados y consultas parametrizadas. No registro contraseñas, claves, tokens ni conexiones. Mantengo pendiente acordar el origen del frontend para CORS en ENV-011.

## Atomicidad de transferencias

Antes de aplicar los saldos valido ambos extremos, el importe, el saldo disponible y la capacidad del receptor. Si una validación falla, no modifico ninguno de los dos objetos. Esa validación de dominio complementa la transacción SQL; no la reemplaza.

Mantengo los locks de las billeteras durante la transacción, uso contextos nuevos en cada reintento y realizo como máximo tres intentos totales por conflictos recuperables. Si agoto esos intentos informo CONCURRENCY_CONFLICT, sin confirmar un resultado parcial. Respeto la cancelación antes de aplicar saldos y antes del commit. Si el commit ya ocurrió, recupero la operación con su clave en vez de asumir un rollback.

Verifico las fallas de cancelación y confirmación con `dotnet test Wallet.slnx -c Release --filter "FullyQualifiedName~SqlTransferAtomicityTests"`, sobre una base exclusiva de pruebas configurada en WALLET_TEST_CONNECTION. Las pruebas SQL omitidas no demuestran atomicidad.

## Pruebas de consistencia financiera

Uso el workflow **SQL consistency** para ejecutar las pruebas SQL sobre SQL Server 2022 descartable en GitHub Actions. En cada ejecución genero credenciales y clave JWT efímeras, las enmascaro en logs y no las guardo en Git. Publico SQL sólo en loopback del runner, espero readiness y aplico las migraciones antes de ejecutar `Category=Sql`.

Conservo el reporte TRX siete días y retiro el contenedor al finalizar. Rechazo ejecuciones con cero pruebas, resultados fallidos u omitidos, o sin las suites financieras obligatorias. El check `sql-consistency` se agrega al workflow backend existente; necesito exigirlo en las reglas de protección si quiero que bloquee el merge. La confianza del certificado autofirmado se limita a ese contenedor local, sin cambiar la configuración de producción.

Serializo las clases SQL para aislar sus fixtures. Dentro de las pruebas de carrera uso una barrera explícita antes de los locks para solapar solicitudes. Verifico duplicados, doble gasto y ambos límites inclusivos del importe, además de las pruebas existentes de rollback, cancelación, movimientos y recuperación del commit.

Para ejecutar SQL localmente configuro WALLET_TEST_CONNECTION con una base exclusiva de pruebas, previamente migrada. No uso WalletDev con mis usuarios manuales ni una base productiva:

```sh
dotnet test Wallet.slnx -c Release --filter "Category=Sql" --logger trx --results-directory TestResults/Sql
python scripts/testing/verify-sql-results.py TestResults/Sql
```

Uso un directorio de resultados nuevo para cada ejecución; el verificador examina todos sus TRX. Una conexión configurada pero inaccesible hace fallar las pruebas. Una conexión ausente produce pruebas omitidas y el verificador rechaza el resultado. No considero una suite omitida como evidencia de consistencia.

Para cerrar el Incremento 2 necesito también el recorrido frontend/backend de saldo, historial y transferencias, con sus criterios de H2. Los checks del backend por sí solos no acreditan esa integración.

## Pruebas y evidencia

Ejecuto las pruebas HTTP con JWT real y un doble de repositorio exclusivo de tests. Para las pruebas SQL configuro `WALLET_TEST_CONNECTION` con una base aislada y previamente migrada. Los tests crean usuarios propios y eliminan únicamente esos registros. No interpreto una prueba omitida como aprobada.

Conservé la evidencia local del Incremento 2: 28 pruebas SQL aprobadas, sin fallas ni omisiones, sobre WalletTests. Después de incorporar Payments necesito aplicar su migración y repetir la suite ampliada; la evidencia anterior no valida las pruebas nuevas de pagos. Verifico también el resultado remoto de CI antes del merge.

Para validar H1 tengo pendiente comprobar desde el frontend registro → login → perfil, reiniciar la API y confirmar que los usuarios y cambios persisten. No considero H0/H1 ni la DoD completos por tener pruebas locales en verde.

## Organización del trabajo

Trabajo desde `develop` en ramas por tarjeta y propongo la integración mediante Pull Request. Registro defectos con pasos, resultado esperado, resultado obtenido y evidencia. Mantengo trazabilidad entre tarjeta, rama, commits, PR y versión.

En el Incremento 3 implemento cargas sandbox con Mercado Pago. Mantengo pendiente la verificación real del proveedor hasta configurar su aplicación de prueba y completar la evidencia. Tarjetas, geolocalización y mensajería siguen sus incrementos. No habilito dinero real ni capturo datos de tarjetas.

## Referencias

Uso como referencia el Plan de Desarrollo, el Plan de Mantenimiento, Control de Versiones, DoR, DoD y el Modelo de Dominio y Datos compartidos para el proyecto. Mantengo estos enlaces documentales:

- [Plan de Desarrollo](https://docs.google.com/document/d/1AX-_na1qXV5yY7RU8kCaj34E3mRkdmYft7oO2eFNbbA)
- [Plan de Mantenimiento](https://docs.google.com/document/d/1-em45Bswy8wO6Sd0NqbqGzAqTDq9OjqiobSuLLxN6AI)
- [Control de Versiones](https://docs.google.com/document/d/15BE9V_7xXOVsK0c6b-G11Beo8bZ07NhoxjnXIWnnTKw)
- [Definition of Ready](https://docs.google.com/document/d/1iyhpQGIlRO7K5BftSBNJ04fz7YQSCY1xPPMi9rsCAKg)
- [Definition of Done](https://docs.google.com/document/d/1ivZhuq6ELIlCdFphwwW6saqMsUGHX109dfadezSUD4A)
- [Autenticación JWT en ASP.NET Core](https://learn.microsoft.com/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-9.0)
- [OpenAPI en ASP.NET Core](https://learn.microsoft.com/aspnet/core/fundamentals/openapi/customize-openapi?view=aspnetcore-9.0)


## Cargas sandbox con Mercado Pago

Uso Checkout Pro para iniciar una carga en ARS. Creo la operación local antes de contactar al proveedor y devuelvo su enlace sandbox. Verifico el pago mediante la API del proveedor antes de acreditar. No interpreto el retorno del navegador como una aprobación.

Mantengo `Payments:Enabled=false` hasta tener mi aplicación de prueba configurada. Uso un máximo inicial configurable de $100.000 ARS por carga; es independiente del límite de transferencias. No almaceno PAN, CVV ni credenciales en el frontend.

Desde `Wallet.Api` abro mi archivo de secretos con el administrador de secretos de usuario. Integro estos valores con mi conexión y JWT existentes, sin reemplazarlos ni publicarlos:

```json
{
  "Payments:Enabled": true,
  "Payments:AccessToken": "<TOKEN DEL VENDEDOR DE PRUEBA>",
  "Payments:WebhookSecret": "<SECRETO DE WEBHOOKS>",
  "Payments:CollectorId": "<ID DEL VENDEDOR DE PRUEBA>",
  "Payments:PublicBaseUrl": "https://<HOST PUBLICO DE MI API>",
  "Payments:MaxTopUpAmount": 100000,
  "Payments:TimeoutSeconds": 15,
  "Frontend:AllowedOrigins:0": "http://localhost:5173",
  "AllowedHosts": "localhost;<HOST PUBLICO DE MI API>"
}
```

Configuro el evento de pagos en Webhooks del proveedor con la URL `https://<HOST PUBLICO DE MI API>/api/payments/mercadopago/webhook`. Necesito HTTPS público para recibir notificaciones: el proveedor no puede llamar a mi localhost. En local apunto el túnel a mi API HTTPS en https://localhost:7081. Uso un túnel o un ambiente de integración y mantengo su hostname alineado con PublicBaseUrl y AllowedHosts. La verificación implementada utiliza `x-signature`, `x-request-id` y el `data.id` de la query; compruebo su compatibilidad con las notificaciones reales como parte de SPIKE-001.

Aplico las migraciones antes de arrancar:

```powershell
dotnet ef database update --project Wallet.Infrastructure --startup-project Wallet.Api
dotnet run --project Wallet.Api --launch-profile https
```

Uso estas operaciones autenticadas con rol Client:

| Operación | Comportamiento |
|---|---|
| `GET /api/payments/config` | Consulto habilitación, máximo y ARS, sin secretos. |
| `POST /api/payments` | Envío `{ "amount": 1000 }` y `Idempotency-Key` UUID. Recibo 201 al preparar una carga nueva, 200 al recuperar una existente o 202 si sigue incierta. |
| `GET /api/payments/{id}` | Consulto únicamente una operación propia. |
| `POST /api/payments/{id}/refresh` | Consulto al proveedor y reconcilio la misma operación, sin crear otro checkout. |

En Swagger genero automáticamente una clave por intención, separada de la clave de transferencias. Execute conserva la clave y Nueva clave inicia otra intención. Reutilizarla con otro importe devuelve 409. Una repetición no vuelve a crear la preferencia externa; crear preferencias no presupone soporte de idempotencia por parte de Mercado Pago.

Guardo Created, Pending, Unknown, Approved, Rejected, Cancelled o Error. Approved significa que verifiqué la aprobación y confirmé el crédito local. Compruebo referencia, importe, ARS, cuenta receptora y el ambiente de prueba. Si Mercado Pago informa `live_mode=true`, exijo verificar por API que el token pertenece al vendedor configurado y que vendedor y comprador son usuarios distintos de prueba. Verifico `test_user` del vendedor por API; para el comprador uso su etiqueta cuando está disponible o, si su perfil público omite tags, exijo su ID en `Payments:TestBuyerIds`, confirmado previamente en Cuentas de prueba del panel de Mercado Pago; si no puedo comprobarlo, no acredito. Confirmo saldo, estado, movimiento Credit/Payment y recibo de procesamiento en una única transacción SQL, con locks y restricciones únicas. Un pago puede tener varios intentos rechazados, pero sólo una aprobación puede acreditar la intención. Una segunda aprobación diferente requiere revisión.

Ejecuto recuperación periódica cada minuto, con consultas acotadas y espera creciente entre intentos de cada operación. Cuando no puedo resolver una operación pendiente o incierta tras diez comprobaciones, marco RequiresReview; puedo volver a consultarla explícitamente mediante refresh. Una operación con discrepancias queda identificada para revisión. Si una creación de checkout queda incierta y no hay pagos localizables, conservo la identidad local sin crear otra preferencia automáticamente.

No revierto cobros externos con rollback SQL. Mantengo devoluciones y contracargos fuera de este incremento; detecto esos estados para revisión y no borro movimientos anteriores. La habilitación de dinero real requiere una validación adicional de las condiciones comerciales y operativas del proveedor.

Para mi prueba manual, inicio sesión en Swagger, anoto el saldo, preparo una carga y abro CheckoutUrl en el navegador. Completo el checkout con medios sandbox, consulto la operación y verifico un único crédito en saldo e historial. Repito la solicitud con la misma clave y compruebo que no aumente nuevamente el saldo. También pruebo rechazo y pendiente sin acreditación.

Ejecuto las pruebas controladas de pagos con:

```powershell
dotnet test Wallet.slnx -c Release --filter "FullyQualifiedName~Payment"
```

Para SQL uso WalletTests, aplico Payments a esa conexión mediante `--connection`, configuro WALLET_TEST_CONNECTION y ejecuto la suite completa Category=Sql. El verificador exige también SqlPaymentTests. No interpreto las pruebas del adaptador con HTTP simulado como evidencia de sandbox real. `/health/ready` comprueba migraciones pendientes y las tablas del modelo financiero, incluso si las cargas están deshabilitadas.

Cuando el perfil público del comprador omite tags, agrego su ID confirmado en el panel a User Secrets como `"Payments:TestBuyerIds:0": "<ID DEL COMPRADOR DE PRUEBA>"`. La lista queda vacía por defecto y nunca acepto IDs enviados por el cliente como prueba de sandbox. Una respuesta con tags presentes sin test_user sigue siendo rechazada.

Al crear cada checkout envío notification_url con `?source_news=webhooks` para solicitar Webhooks en lugar de IPN. Mantengo la verificación de firma y no proceso notificaciones IPN con resource/topic. El cambio aplica a preferencias nuevas; no modifica las URLs guardadas en preferencias anteriores. La URL del panel de Webhooks conserva la ruta /api/payments/mercadopago/webhook.
