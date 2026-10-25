# Wallet · Identidad y creación de billetera

Desarrollo el backend de una billetera virtual con ASP.NET Core. En este incremento implemento registro, inicio de sesión y consulta y actualización del perfil propio. Mi objetivo es preparar identidad para la versión v0.2.0; todavía tengo pendiente validar el frontend integrado y la persistencia real para completar H1.

Para el Incremento 2 incorporo la creación explícita de una billetera ARS por usuario.

## Arquitectura

Organizo la solución en cinco proyectos:

- `Wallet.Domain`: entidades, invariantes, perfil y versión de sesión.
- `Wallet.Application`: casos de uso, DTOs y contratos.
- `Wallet.Infrastructure`: EF Core, SQL, repositorios y hashing.
- `Wallet.Api`: endpoints, JWT, autorización y configuración.
- `Wallet.Tests`: pruebas de dominio, hashing, contratos HTTP y SQL.

Mantengo Domain independiente de ASP.NET Core y EF Core. Desde Application referencio Domain; desde Infrastructure referencio Application y Domain; desde Api referencio Application e Infrastructure.

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

Para habilitar identidad necesito una base SQL accesible, el esquema migrado y una clave JWT externa. Tengo pendiente alinear la validación LocalDB indicada en ENV-008 con el destino Azure SQL documentado para integración. No uso LocalDB de Windows desde el contenedor Linux ni introduzco otra base sin acordarlo.

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
| `POST /api/wallets` | JWT + Client, sin cuerpo | 201 al crear, 200 si existe | 400, 401, 403, 503 |
| `GET /api/users/me` | JWT + Client | 200 | 401, 403, 503 |
| `PUT /api/users/me/profile` | JWT + Client | 200 | 400, 401, 403, 409, 415, 503 |

Para registro recibo `firstName`, `lastName`, `email` y `password`; para login, `email` y `password`. En la edición de perfil permito únicamente nombre y apellido. Devuelvo `id`, `firstName`, `lastName` y `email`, sin contraseñas ni hashes. Rechazo propiedades adicionales como `role`, `userId` o `balance`.

Uso nombres obligatorios de hasta 100 caracteres, correo de hasta 254 y contraseña de 12 a 128 caracteres, no compuesta sólo por espacios. Normalizo el correo con `Trim + ToUpperInvariant` para comprobar unicidad y conservo la contraseña sin transformaciones. Limito el cuerpo de las solicitudes a 16 KiB.

## Autenticación y autorización

Emito tokens HS256 de quince minutos con tolerancia de reloj de treinta segundos. Valido firma, algoritmo, emisor, audiencia, vigencia y claims de identidad. En este emisor uso UUID no vacío para `sub` y `jti`, y una versión de sesión no negativa.

En cada solicitud privada consulto el usuario persistido y compruebo estado activo, rol y versión de sesión. Asigno `Client` desde el servidor y no permito que el cliente elija su rol. Al incrementar `SessionVersion` o deshabilitar la cuenta invalido los tokens anteriores en las siguientes comprobaciones. No expongo todavía endpoints de logout, administración ni refresh tokens.

Comparto un límite de diez intentos de registro/login por IP y minuto. Uso respuestas `no-store`, errores controlados y consultas parametrizadas. No registro contraseñas, claves, tokens ni conexiones. Mantengo pendiente acordar el origen del frontend para CORS en ENV-011.

## Pruebas y evidencia

Ejecuto las pruebas HTTP con JWT real y un doble de repositorio exclusivo de tests. Para las pruebas SQL configuro `WALLET_TEST_CONNECTION` con una base aislada y previamente migrada. Los tests crean usuarios propios y eliminan únicamente esos registros. No interpreto una prueba omitida como aprobada.

En la ejecución de TEST-001 obtuve **30 pruebas aprobadas, 0 fallidas y 2 SQL omitidas** porque no configuré la conexión de pruebas. Todavía necesito evidencia SQL y de la ejecución remota de CI para cerrar la tarjeta.

Para validar H1 tengo pendiente comprobar desde el frontend registro → login → perfil, reiniciar la API y confirmar que los usuarios y cambios persisten. No considero H0/H1 ni la DoD completos por tener pruebas locales en verde.

## Organización del trabajo

Trabajo desde `develop` en ramas por tarjeta y propongo la integración mediante Pull Request. Registro defectos con pasos, resultado esperado, resultado obtenido y evidencia. Mantengo trazabilidad entre tarjeta, rama, commits, PR y versión.

En el Incremento 2 implemento la creación de billeteras; mantengo pendientes consulta de saldo y transferencias. Pagos, tarjetas, geolocalización y mensajería siguen sus incrementos. No incorporo dinero real, procesamiento de tarjetas bancarias ni funcionalidades ajenas al alcance del prototipo.

## Referencias

Uso como referencia el Plan de Desarrollo, el Plan de Mantenimiento, Control de Versiones, DoR, DoD y el Modelo de Dominio y Datos compartidos para el proyecto. Mantengo estos enlaces documentales:

- [Plan de Desarrollo](https://docs.google.com/document/d/1AX-_na1qXV5yY7RU8kCaj34E3mRkdmYft7oO2eFNbbA)
- [Plan de Mantenimiento](https://docs.google.com/document/d/1-em45Bswy8wO6Sd0NqbqGzAqTDq9OjqiobSuLLxN6AI)
- [Control de Versiones](https://docs.google.com/document/d/15BE9V_7xXOVsK0c6b-G11Beo8bZ07NhoxjnXIWnnTKw)
- [Definition of Ready](https://docs.google.com/document/d/1iyhpQGIlRO7K5BftSBNJ04fz7YQSCY1xPPMi9rsCAKg)
- [Definition of Done](https://docs.google.com/document/d/1ivZhuq6ELIlCdFphwwW6saqMsUGHX109dfadezSUD4A)
- [Autenticación JWT en ASP.NET Core](https://learn.microsoft.com/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-9.0)
- [OpenAPI en ASP.NET Core](https://learn.microsoft.com/aspnet/core/fundamentals/openapi/customize-openapi?view=aspnetcore-9.0)
