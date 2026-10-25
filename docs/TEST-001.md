# TEST-001 — Autenticación, autorización y endpoints protegidos

Rama: feature/TEST-001-auth-tests. Las pruebas HTTP usan WebApplicationFactory con el pipeline real de JWT, autorización, rate limiting y hashing. Sólo reemplazan IUserRepository por un doble exclusivo de tests. Cada caso tiene un host, clave efímera y estado propios; no se simula la autenticación ni se comparte el limitador entre pruebas.

## Ejecutar

Requiere el SDK compatible con global.json. En el entorno preparado, ejecutar primero `source /workspace/.setup/wallet-env.sh`. Desde la raíz del repositorio:

```sh
dotnet restore Wallet.slnx
dotnet test Wallet.slnx -c Release --no-restore --filter 'Category!=Sql' --logger trx --results-directory TestResults
dotnet test Wallet.slnx -c Release --no-restore --logger trx --results-directory TestResults
```

La primera selección ejecuta pruebas sin SQL; la segunda incluye las pruebas SQL optativas, que aparecen omitidas si falta conexión. El workflow existente ya ejecuta la suite completa y conserva los TRX.

Para pruebas reales configurar WALLET_TEST_CONNECTION por un mecanismo seguro, apuntando exclusivamente a una base de pruebas previamente migrada con InitialIdentity. Nunca usar una base de usuarios reales. Las pruebas SQL crean registros con identificadores únicos y eliminan únicamente sus propios usuarios; no crean ni eliminan la base y no aplican migraciones automáticamente.

```sh
dotnet test Wallet.slnx -c Release --no-restore --filter 'Category=Sql' --logger trx --results-directory TestResults
```

Un resultado omitido no demuestra funcionamiento de SQL. El origen de esta conexión es una variable real del proceso; no copiar valores al README, chat ni logs.

## Cobertura de criterios

| Criterio | Prueba |
|---|---|
| Login válido, Bearer, duración y rechazo genérico | Login_issues_bearer_with_configured_lifetime_and_identity_claims; Invalid_credentials_unknown_and_disabled_users_have_same_generic_rejection |
| Perfil propio y datos de respuesta permitidos | Profile_is_private_and_editing_names_preserves_session |
| Firma, expiración, algoritmo, emisor, audiencia y claims | Invalid_tokens_cannot_read_or_update_profile (Theory parametrizada sobre lectura y escritura) |
| 403 con sesión válida sin rol Client | Valid_session_with_non_client_role_is_forbidden |
| Rechazo de role/userId y ausencia de cambios cruzados | Registration_and_profile_reject_unmapped_fields |
| Revocación | Revocation_or_disabling_invalidates_previously_valid_token (doble); Committed_revocation_and_disabling_are_observed_by_following_http_requests (SQL real) |
| Editar perfil conserva sesión y versión | Profile_is_private_and_editing_names_preserves_session |
| SQL indisponible no concede acceso | Unavailable_storage_never_grants_access_or_updates_profile (fallo simulado en repositorio) |
| Límite compartido de diez intentos | Registration_and_login_share_ten_attempts_per_window |
| SQL separado, CI y reproducción | Traits Category=Sql, workflow existente, comandos de este documento |

El fixture del 403 construye mediante reflexión un usuario con rol TestObserver y un token coincidente, exclusivamente en tests. Esto permite alcanzar la política real Client, en lugar de provocar un 401 por discrepancia entre token y usuario. No agrega un rol disponible en producción. No hay endpoint nuevo para revocar, deshabilitar o asignar roles.

El test SQL hace login por HTTP usando repositorio SQL real, comprueba acceso, guarda una revocación con un contexto independiente, verifica la versión con otro contexto y comprueba 401 con el token anterior. Luego inicia una nueva sesión, persiste la deshabilitación y verifica que se rechazan tanto esa sesión como un nuevo login.

## Defecto encontrado y corrección

La primera ejecución produjo 27 aprobadas, 1 fallida y 2 omitidas. El caso missing:jti recibió 200 en lugar de 401: faltaba validar ese claim en OnTokenValidated. Se agregó validación de jti como UUID no vacío, coherente con el formato emitido por el servicio actual. También se rechaza sub vacío y versión de sesión negativa. Se mantiene la validación criptográfica existente. La prueba de jti ausente no se debilitó ni se omitió; se agregó jti malformado y rol discrepante.

El formato UUID de jti es el contrato de este emisor, no una obligación general del estándar JWT. Si se adopta un proveedor externo, revisar este contrato explícitamente.

## Límites de la evidencia

Los tests con dobles no demuestran persistencia, restricciones ni concurrencia SQL. La prueba SQL de revocación y la existente de unicidad concurrente requieren ejecución y evidencia con la base configurada antes de cerrar la tarjeta. Tampoco se validó ejecución remota de GitHub Actions en esta tarea ni se modificó Trello. Los reportes locales se generan bajo TestResults, ignorado por Git.

Resultado final local de la suite completa: compilación exitosa, 30 pruebas aprobadas, 0 fallidas y 2 SQL omitidas por ausencia de WALLET_TEST_CONNECTION. Reporte: TestResults/_0fe974b3472d_2026-10-25_21_40_50.trx. git diff --check sin errores.
