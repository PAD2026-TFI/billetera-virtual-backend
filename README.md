# Wallet · primer incremento de identidad

Paquete de implementación propuesto, 19/10/2026. Conserva Wallet.Api, Wallet.Application, Wallet.Domain y Wallet.Infrastructure de la captura. Agrega Wallet.Tests, previsto en el plan. No modifica tu repositorio ni marca tareas como terminadas.

## Qué estamos construyendo

Objetivo funcional: registrar una cuenta, iniciar sesión y consultar/actualizar exclusivamente el perfil propio. Es el incremento de identidad, objetivo v0.2.0; no es la billetera completa ni v1.0.0. H1 también exige frontend y persistencia verificados; este paquete cubre el backend.

Hay dos estados explícitos de la misma aplicación:

- **Base técnica ejecutable ahora:** Identity:Enabled=false (valor inicial). GET /health/live → 200, GET /health/ready → 503, rutas /api → 503 IDENTITY_NOT_CONFIGURED. No se simula un registro exitoso.
- **Prototipo funcional de identidad:** habilitar Identity:Enabled, configurar Azure SQL y clave JWT, aplicar la migración y ejecutar el flujo. Sin esos recursos no puede afirmarse que identidad funciona de punta a punta.

La consigna §4.2 pospone Azure SQL y prohíbe introducir una base alternativa sin una decisión documentada. Por eso no se agregó almacenamiento en memoria, SQLite ni LocalDB a la aplicación. El doble en memoria vive sólo en Wallet.Tests para probar contratos HTTP; no demuestra persistencia ni sustituye Azure.

## Decisiones de implementación que hay que revisar en las tarjetas

Los documentos dejan sin fijar SDK, límites y política de credenciales. Para entregar código concreto se proponen: .NET 9, SDK 9.0.319, paquetes Microsoft 9.0.21; nombres de 1–100 caracteres después de trim; email hasta 254 caracteres normalizado con Trim + ToUpperInvariant; contraseña de 12–128 caracteres, no vacía ni sólo espacios, sin modificar su contenido; JWT de 15 minutos, tolerancia de reloj 30 segundos; clave aleatoria de al menos 32 bytes en Base64; 10 intentos de registro/login combinados por IP/minuto; request máximo 16 KiB. Son decisiones propuestas para este paquete, no acuerdos previos del equipo.

Una captura anterior muestra Visual Studio 2022 17.14. Se eligió .NET 9 para mantener compatibilidad con ese entorno; confirmar igualmente el SDK instalado. .NET 9 termina su soporte el 10/11/2026: registrar una actualización a .NET 10 y un IDE compatible antes de continuar más allá de esa fecha. Esta elección es para el prototipo inmediato, no para congelar el stack del proyecto. También se puede ejecutar con el SDK desde terminal. No cambiar sólo un csproj a otra versión: framework, paquetes, herramientas, global.json y pruebas deben permanecer compatibles. No es una certificación de cumplimiento normativo ni una solución bancaria productiva.

## Incorporarlo sin perder tu solución

1. Crear rama feature/ENV-004-base-backend desde develop y conservar el trabajo existente.
2. Verificar `dotnet --list-sdks` y confirmar la decisión de SDK.
3. Copiar las carpetas/proyectos del paquete o abrir Wallet.slnx en una carpeta aparte para revisarlo. Si conservás tu .sln actual, agregar Wallet.Tests y configurar las referencias indicadas abajo. Los csproj del paquete heredan TargetFramework desde Directory.Build.props: copiar también ese archivo.
4. Eliminar únicamente los archivos de ejemplo WeatherForecast.cs y WeatherForecastController.cs si siguen sin uso. No conservar dos Program.cs.
5. Seguir TARJETAS.md; no fusionar todo como una tarea de registro.

Referencias: Application → Domain; Infrastructure → Application y Domain; Api → Application e Infrastructure; Tests → los proyectos probados. Domain no referencia EF, ASP.NET ni SQL.

## Ejecutar la base técnica

Desde la carpeta que contiene Wallet.slnx:

```powershell
dotnet --version
dotnet restore Wallet.slnx
dotnet build Wallet.slnx -c Release
dotnet test Wallet.slnx -c Release
dotnet dev-certs https --trust
dotnet run --project Wallet.Api --launch-profile https
```

Abrir https://localhost:7081/health/live. Debe mostrar `{"status":"Healthy"}`. La documentación OpenAPI está en https://localhost:7081/openapi/v1.json sólo en Development. Es el contrato JSON; no se agregó interfaz Swagger UI. Wallet.Api.http permite ejecutar requests desde el editor. El test Azure SQL aparece omitido cuando no hay conexión de pruebas: no se interpreta como aprobado.

## Habilitar identidad cuando Azure esté disponible

Usar **una base de desarrollo vacía**, acceso de red habilitado para tu equipo y una cuenta con permisos necesarios. La suscripción, costo y aprovisionamiento no fueron verificados ni creados por este trabajo. ENV-007 sigue bloqueada hasta resolverlos. Alinear la mención de base local del plan con Azure SQL.

Guardar los valores reales mediante Administrar secretos de usuario del proyecto Wallet.Api (no incluirlos en Git). Esquema del archivo de secretos:

```json
{
  "Identity:Enabled": true,
  "ConnectionStrings:DefaultConnection": "<CADENA REAL DE AZURE SQL, Encrypt=True;TrustServerCertificate=False>",
  "Jwt:Key": "<CLAVE ALEATORIA BASE64>"
}
```

Para generar la clave localmente en PowerShell, sin enviarla al chat:

```powershell
$jwtBytes = New-Object byte[] 32
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($jwtBytes)
$rng.Dispose()
[Convert]::ToBase64String($jwtBytes)
```

Copiar el resultado en los secretos. No usar literalmente los marcadores. User Secrets evita guardar secretos en Git; no es una bóveda cifrada. En despliegue usar configuración externa. Variables equivalentes: Identity__Enabled, ConnectionStrings__DefaultConnection, Jwt__Key, Jwt__Issuer y Jwt__Audience. Configurar AllowedHosts para el hostname real al desplegar.

Aplicar la migración incluida (no volver a crear InitialIdentity):

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project Wallet.Infrastructure --startup-project Wallet.Api
dotnet ef migrations script 0 InitialIdentity --project Wallet.Infrastructure --startup-project Wallet.Api --output migration.sql
# Revisar migration.sql antes de aplicarlo a la base elegida.
dotnet ef database update --project Wallet.Infrastructure --startup-project Wallet.Api
dotnet run --project Wallet.Api --launch-profile https
```

No se ejecutan migraciones automáticamente al arrancar ni se usa EnsureCreated. Una cuenta de migración puede requerir permisos distintos a los de ejecución. `database update 0` elimina Users y sus datos: sólo sirve para volver atrás en una base descartable vacía de datos importantes; en una base compartida se planifica recuperación antes de aplicarlo.

Con conexión correcta pero sin esquema, /health/ready devuelve 503. Con esquema listo devuelve 200. Si falta configuración al habilitar identidad, el proceso falla al inicio con un mensaje de configuración; no se inventa una clave ni se degrada a una base temporal.

## Contratos del incremento

| Método y ruta | Acceso | Éxito | Errores principales |
|---|---|---|---|
| GET /health/live | Público | 200, status Healthy | No consulta SQL |
| GET /health/ready | Público | 200, status Healthy | 503, status Unhealthy |
| POST /api/users | Público | 201, UserResponse, Location /api/users/me | 400, 409, 415, 429, 503 |
| POST /api/auth/login | Público | 200, accessToken/tokenType/expiresIn | 400, 401, 415, 429, 503 |
| GET /api/users/me | Bearer + Client | 200, UserResponse | 401, 403, 503 |
| PUT /api/users/me/profile | Bearer + Client | 200, UserResponse | 400, 401, 403, 409, 415, 503 |

UserResponse sólo expone id, firstName, lastName y email. Registro recibe firstName, lastName, email, password. Login recibe email, password. PUT reemplaza los dos campos editables: firstName y lastName. Cuerpos JSON con propiedades adicionales (por ejemplo role, balance, userId) se rechazan con 400. No existe GET de otro usuario ni un endpoint administrativo. `Location: /api/users/me` se consulta tras iniciar sesión. 413 indica cuerpo demasiado grande; 500 indica falla inesperada. El health de disponibilidad conserva su contrato de estado; los errores de /api usan ProblemDetails.

Para verificar el flujo, ejecutar las requests en Wallet.Api.http; copiar accessToken al encabezado Bearer. Reiniciar la API y volver a iniciar sesión demuestra persistencia cuando se usa la base real. Una sesión emitida antes de incrementar SessionVersion o deshabilitar al usuario debe dejar de servir. La revocación está modelada y comprobada por solicitud; este alcance no agrega un endpoint de logout, administración o refresh token.

## Reparto de responsabilidades

- Domain/User: invariantes, estado, normalización, perfil y versión de sesión. Sin contraseñas en claro persistidas.
- Application: DTOs, contratos, Result<T>, casos de uso y validaciones también al invocarlos fuera de HTTP.
- Infrastructure: EF Core, índice único de email, rowversion de SQL, repositorio específico y PasswordHasher de ASP.NET Core Identity.
- Api: controllers, JWT, identidad actual, configuración, límites y traducción HTTP.
- Tests: dominio, hash, API y prueba optativa de unicidad concurrente en SQL real.

I/O usa Task y CancellationToken. Validaciones, mapeo y hashing son síncronos; no se usa Task.Run para simular I/O. List<T> se usa donde hay colecciones reales (claims y validaciones). Result<T> y el helper de acceso SQL son genéricos porque eliminan repetición concreta. No se agrega Queue<T>: la mensajería está en el incremento 5, y una cola local tampoco equivaldría a un broker durable. Tampoco hay CRUD genérico, MediatR ni microservicios.

## Seguridad y límites conocidos de este prototipo

Validación de firma, algoritmo, issuer, audience y expiración; revisión de estado/rol/session version desde SQL en cada request privado; contraseñas hasheadas con sal y componente probado; HTTPS; no-store en respuestas; secretos externos; campos explícitos; límite de intentos por IP; consultas parametrizadas por EF; errores sin detalles SQL ni credenciales. No se configura CORS abierto: las pruebas HTTP no lo necesitan; el origen real del frontend se acordará en ENV-011.

El límite por IP es local a esta instancia y no es una solución distribuida ni protección completa contra ataques. El login responde un mensaje genérico; no promete tiempo de respuesta constante ni imposibilidad de enumeración (el registro ya usa 409 según la consigna). No hay políticas de producción, MFA, recuperación de contraseña o verificación de email: quedan fuera de este corte. PasswordHasher puede indicar necesidad futura de rehash; este corte verifica hashes y una actualización de política debe agregar una migración gradual del hash. No se registran cuerpos de request ni tokens. No hay dinero real.

## Pruebas y feedback

`dotnet test` ejecuta pruebas locales y HTTP con un doble exclusivo de tests. Para la prueba SQL configurar WALLET_TEST_CONNECTION con una base **aislada de pruebas**, aplicar previamente la migración allí y volver a ejecutar. Esa prueba crea un usuario de email aleatorio y elimina sólo ese usuario al terminar; jamás apuntarla a una base de usuarios reales. No crea ni elimina la base completa.

Antes de cerrar TEST-001 completar PRUEBAS.md en Azure. No afirmar H0/H1 ni DoD completa por tener tests locales verdes. La CI incluida ejecuta restore/build/test, no despliega ni dispone de Azure por sí sola.

Después de cada demostración registrar BUG o CHANGE con pasos, esperado, obtenido y evidencia; corregir en bugfix/<ticket>-<descripcion> desde develop y repetir el flujo afectado. Mostrar primero registro/login/perfil, recibir feedback y mantener pequeño el siguiente incremento. Transferencias, saldos, pagos, tarjetas, geolocalización y colas esperan sus tarjetas.

## Fuentes que delimitan el trabajo

Lectura actual de los seis documentos: Consigna, Plan de Desarrollo, Plan de Mantenimiento, Control de Versiones, DoR y DoD. Se conservan los identificadores del plan; TARJETAS.md amplía su definición para este corte. El inventario de la carpeta enlazada devuelve los cinco documentos de gestión; la Consigna se leyó desde su documento conocido. No se modificaron esos documentos en esta entrega.

- Consigna: https://docs.google.com/document/d/18jI1IpjO5SqIquSSp6t_smMAccTvSbhqO7jDUj_yYro
- Plan: https://docs.google.com/document/d/1AX-_na1qXV5yY7RU8kCaj34E3mRkdmYft7oO2eFNbbA
- DoR: https://docs.google.com/document/d/1iyhpQGIlRO7K5BftSBNJ04fz7YQSCY1xPPMi9rsCAKg
- DoD: https://docs.google.com/document/d/1ivZhuq6ELIlCdFphwwW6saqMsUGHX109dfadezSUD4A
- Versiones: https://docs.google.com/document/d/15BE9V_7xXOVsK0c6b-G11Beo8bZ07NhoxjnXIWnnTKw
- Mantenimiento: https://docs.google.com/document/d/1-em45Bswy8wO6Sd0NqbqGzAqTDq9OjqiobSuLLxN6AI
- JWT: https://learn.microsoft.com/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-9.0
- OpenAPI: https://learn.microsoft.com/aspnet/core/fundamentals/openapi/customize-openapi?view=aspnetcore-9.0

Los fundamentos de capas, SOLID, DTOs y evolución incremental provienen de la consigna y sus referencias bibliográficas ya revisadas. No se afirmó haber releído íntegramente todos los libros en esta entrega.

Compatibilidad y soporte consultados: https://learn.microsoft.com/dotnet/core/porting/versioning-sdk-msbuild-vs y https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/ . Se fijó además System.Security.Cryptography.Pkcs 9.0.21 para evitar una versión transitiva vulnerable detectada durante la restauración. Mantener la auditoría NuGet activa; no silenciar vulnerabilidades para lograr un build verde.

Resultados ejecutados y límites de la validación: ver VERIFICACION.md. El script SQL generado se incluye como InitialIdentity.sql.
