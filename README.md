# Wallet · Identidad y billetera

Desarrollo el backend de una billetera virtual con ASP.NET Core. En este incremento implemento registro, inicio de sesión y consulta y actualización del perfil propio. La persistencia local de identidad fue comprobada manualmente; la aceptación integrada desde el frontend continúa pendiente para completar H1.

Para el Incremento 2 implemento billeteras ARS, consulta de saldo, fondos ficticios de desarrollo, transferencias con idempotencia y movimientos con historial paginado. Distingo implementación, validación del backend y aceptación integrada H2.

## Arquitectura

Organizo el backend en las siguientes capas y proyecto de pruebas; además dispongo de una herramienta de desarrollo:

- `Wallet.Domain`: entidades, invariantes, perfil y versión de sesión.
- `Wallet.Application`: casos de uso, DTOs y contratos.
- `Wallet.Infrastructure`: EF Core, SQL, repositorios y hashing.
- `Wallet.Api`: endpoints, JWT, autorización y configuración.
- `Wallet.Tests`: pruebas de dominio, hashing, contratos HTTP y SQL.
- `Wallet.DevelopmentTools`: cargas ficticias de desarrollo; no es un endpoint público.

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

Para habilitar identidad necesito una base SQL accesible, el esquema migrado y una clave JWT externa. EF Core gestiona acceso y migraciones; LocalDB, SQL Server y Azure SQL son destinos del proveedor SQL, no reemplazos de EF.

| Ambiente | Motor | Base | Propósito |
|---|---|---|---|
| Desarrollo local | LocalDB en Windows | WalletDev | Pruebas manuales y datos ficticios |
| Pruebas locales | LocalDB en Windows | WalletTests | Pruebas automatizadas aisladas |
| CI | SQL Server 2022 en Docker | WalletIntegrationTests | Pruebas automatizadas descartables |
| Integración compartida | Por confirmar | Por definir | Validación frontend/backend |
| Azure SQL | Previsto, configuración no acreditada | Por definir | Posible integración o despliegue |

LocalDB no funciona en contenedores Linux; para CI uso SQL Server. Azure no es requisito para el backend local. No ejecuto pruebas automatizadas contra WalletDev ni datos reales.

La API y la herramienta de desarrollo utilizan `ConnectionStrings:DefaultConnection`; las pruebas SQL utilizan `WALLET_TEST_CONNECTION`. Varias copias del repositorio con el mismo UserSecretsId pueden compartir configuración privada y base. Cambiar rama o carpeta no crea una base independiente. No elimino ni recreo bases para resolver un error sin identificar primero su causa.

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

Mantengo un modelo y una secuencia común de migraciones: `InitialIdentity` → `CreateWallets` → `DevelopmentFunding` → `Transfers`. Aplico los cambios explícitamente en la base seleccionada, sin migración automática al iniciar la API. Para revisar y aplicar la secuencia completa ejecuto:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project Wallet.Infrastructure --startup-project Wallet.Api
dotnet ef migrations script 0 Transfers --project Wallet.Infrastructure --startup-project Wallet.Api --output migration.sql
# Reviso migration.sql antes de aplicarlo a la base elegida.
dotnet ef database update --project Wallet.Infrastructure --startup-project Wallet.Api
```

No vuelvo a crear `InitialIdentity` ni aplico migraciones automáticamente al arrancar. Distingo los permisos de migración de los permisos de ejecución. Antes de cualquier rollback evalúo la pérdida de datos: `database update 0` revierte todo el esquema. Distingo rollback de esquema, restauración de un backup y compensación financiera; una migración destructiva no es una devolución de transferencia.

Actualmente `/health/ready` consulta Users con identidad habilitada: puede responder 200 aunque falten tablas financieras o haya migraciones pendientes. ENV-010 debe corregirlo para comprobar conectividad, tablas requeridas y ausencia de migraciones pendientes, sin modificar datos; una dependencia requerida no disponible debe producir 503 sin credenciales ni detalles SQL. `/health/live` sigue independiente de la base. La verificación completa se documentará como realizada sólo después de implementarla y probarla. Si falta conexión o clave JWT, rechazo el inicio sin inventar configuración.

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

Para validar atomicidad y concurrencia necesito SQL real, una base exclusiva de pruebas migrada hasta Transfers y `WALLET_TEST_CONNECTION` configurada de forma segura:

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

No registro la herramienta como servicio de la API. Los movimientos actuales incluyen cargas de desarrollo y transferencias; pagos sigue fuera del incremento. La separación efectiva de permisos de migración, ejecución y carga ficticia permanece como refinamiento pendiente.

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

En Windows preparo una base de pruebas separada. La conexión siguiente es local, sin contraseña, y no modifica User Secrets de la API. `--connection` selecciona la base a migrar; no la publica ni la convierte en configuración permanente:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:WALLET_TEST_CONNECTION = "Server=(localdb)\MSSQLLocalDB;Database=WalletTests;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True"
dotnet tool restore
dotnet ef database update --project Wallet.Infrastructure --startup-project Wallet.Api --connection "$env:WALLET_TEST_CONNECTION"
if ($LASTEXITCODE -ne 0) { throw "Falló la aplicación de migraciones." }

$sqlResults = Join-Path "TestResults" ("Sql-" + [guid]::NewGuid().ToString("N"))
dotnet test Wallet.slnx -c Release --filter "Category=Sql" --logger trx --results-directory "$sqlResults"
if ($LASTEXITCODE -ne 0) { throw "Fallaron las pruebas SQL; conservar los TRX." }
python scripts/testing/verify-sql-results.py "$sqlResults"
if ($LASTEXITCODE -ne 0) { throw "El verificador rechazó los resultados SQL." }
Get-ChildItem "$sqlResults" -Filter *.trx
Remove-Item Env:WALLET_TEST_CONNECTION
```

La variable es temporal al proceso de PowerShell. La conexión con confianza del certificado se limita a LocalDB local; no se traslada a Azure/producción. La base debe ser exclusivamente WalletTests. Conservar los TRX con commit, fecha y comando de ejecución, sin secretos, y adjuntarlos a TEST-002. En Docker/CI sigo `.github/workflows/sql-tests.yml`, que prepara su propio SQL Server y base descartable; estas instrucciones LocalDB no se ejecutan en Linux.

Uso un directorio de resultados nuevo para cada ejecución; el verificador examina todos sus TRX. Una conexión configurada pero inaccesible hace fallar las pruebas. Una conexión ausente produce pruebas omitidas y el verificador rechaza el resultado. No considero una suite omitida como evidencia de consistencia.

Para cerrar H2 necesito desde el frontend iniciar sesión con dos usuarios, consultar saldo, transferir, comprobar ambos saldos e historial, y tratar saldo insuficiente, sesión inválida y solicitudes repetidas. Swagger y los checks del backend no sustituyen ese recorrido. La existencia del workflow no demuestra éxito remoto: debo revisar SQL consistency y exigir sql-consistency en protección de ramas (ENV-018).

## Pruebas y evidencia

Ejecuto las pruebas HTTP con JWT real y un doble de repositorio exclusivo de tests. Para las pruebas SQL configuro `WALLET_TEST_CONNECTION` con una base aislada y previamente migrada. Los tests crean usuarios propios y eliminan únicamente esos registros. No interpreto una prueba omitida como aprobada.

En la ejecución de TEST-001 obtuve **30 pruebas aprobadas, 0 fallidas y 2 SQL omitidas** porque no configuré la conexión de pruebas. Todavía necesito evidencia SQL y de la ejecución remota de CI para cerrar la tarjeta.

Para validar H1 tengo pendiente comprobar desde el frontend registro → login → perfil, reiniciar la API y confirmar que los usuarios y cambios persisten. No considero H0/H1 ni la DoD completos por tener pruebas locales en verde.

## Organización del trabajo

Trabajo desde `develop` en ramas por tarjeta y propongo la integración mediante Pull Request. Registro defectos con pasos, resultado esperado, resultado obtenido y evidencia. Mantengo trazabilidad entre tarjeta, rama, commits, PR y versión.

Para TEST-002 se informa una ejecución concreta en LocalDB / WalletTests con 28 pruebas SQL aprobadas, 0 fallidas, 0 omitidas y verificador satisfactorio. El TRX no se aportó a esta revisión documental: queda pendiente adjuntarlo con fecha y commit. No convierto ese número en garantía permanente ni en evidencia de SQL Server 2022 en GitHub Actions. Pagos, tarjetas, geolocalización y mensajería siguen sus incrementos. No incorporo dinero real, procesamiento de tarjetas bancarias ni funcionalidades ajenas al alcance del prototipo.

## Pendientes y orden antes del Incremento 3

1. Alinear ambientes y configuración.
2. Corregir y probar readiness completo (ENV-010).
3. Confirmar SQL consistency remoto y sus reglas de protección.
4. Completar evidencia frontend/backend H2.
5. Refinar secuencia contable por billetera y reconciliación saldo/movimientos.
6. Continuar con pagos.

La separación de permisos de migración, ejecución y fondos ficticios, y la recuperación y backups del ambiente compartido, siguen pendientes. Esta actualización documental no implementa esas capacidades ni cierra tarjetas automáticamente.

## Referencias

Uso como referencia el Plan de Desarrollo, el Plan de Mantenimiento, Control de Versiones, DoR, DoD y el Modelo de Dominio y Datos compartidos para el proyecto. Mantengo estos enlaces documentales:

- [Plan de Desarrollo](https://docs.google.com/document/d/1AX-_na1qXV5yY7RU8kCaj34E3mRkdmYft7oO2eFNbbA)
- [Plan de Mantenimiento](https://docs.google.com/document/d/1-em45Bswy8wO6Sd0NqbqGzAqTDq9OjqiobSuLLxN6AI)
- [Control de Versiones](https://docs.google.com/document/d/15BE9V_7xXOVsK0c6b-G11Beo8bZ07NhoxjnXIWnnTKw)
- [Definition of Ready](https://docs.google.com/document/d/1iyhpQGIlRO7K5BftSBNJ04fz7YQSCY1xPPMi9rsCAKg)
- [Definition of Done](https://docs.google.com/document/d/1ivZhuq6ELIlCdFphwwW6saqMsUGHX109dfadezSUD4A)
- [Autenticación JWT en ASP.NET Core](https://learn.microsoft.com/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-9.0)
- [OpenAPI en ASP.NET Core](https://learn.microsoft.com/aspnet/core/fundamentals/openapi/customize-openapi?view=aspnetcore-9.0)
