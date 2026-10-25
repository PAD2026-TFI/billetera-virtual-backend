# TEST-001 — Autenticación, autorización y endpoints protegidos

Trabajo en la rama `feature/TEST-001-auth-tests`. Para las pruebas HTTP uso WebApplicationFactory con el pipeline real de JWT, autorización, rate limiting y hashing. Reemplazo únicamente `IUserRepository` por un doble exclusivo de tests. Aíslo cada caso con su propio host, clave efímera y estado; mantengo la autenticación real y un limitador independiente por prueba.

## Ejecutar

Uso un SDK compatible con `global.json`. En mi entorno cloud preparado, ejecuto primero `source /workspace/.setup/wallet-env.sh`. Desde la raíz del repositorio ejecuto:

```sh
dotnet restore Wallet.slnx
dotnet test Wallet.slnx -c Release --no-restore --filter 'Category!=Sql' --logger trx --results-directory TestResults
dotnet test Wallet.slnx -c Release --no-restore --logger trx --results-directory TestResults
```

Con el primer comando de pruebas ejecuto los casos sin SQL; con el segundo incluyo los casos SQL optativos. Si falta conexión, los registro como omitidos. Uso el workflow existente para ejecutar la suite completa y conservar los TRX.

Para las pruebas reales configuro `WALLET_TEST_CONNECTION` por un mecanismo seguro y uso exclusivamente una base de pruebas previamente migrada con `InitialIdentity`. Creo registros con identificadores únicos y elimino únicamente los usuarios de cada prueba. No uso una base de usuarios reales ni aplico migraciones automáticamente desde los tests.

```sh
dotnet test Wallet.slnx -c Release --no-restore --filter 'Category=Sql' --logger trx --results-directory TestResults
```

No considero aprobado un test omitido. Proporciono la conexión mediante una variable real del proceso y mantengo su valor fuera del README, el chat y los logs.

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

Para comprobar el 403 construyo mediante reflexión un usuario con rol `TestObserver` y un token coincidente, exclusivamente en el fixture. Así pruebo la política real `Client` con una sesión válida. No incorporo ese rol en producción ni agrego endpoints de revocación, deshabilitación o asignación de roles.

En el test SQL hago login por HTTP con el repositorio real, compruebo acceso, guardo una revocación con un contexto independiente y verifico la versión con otro contexto. Después compruebo el 401 con el token anterior, inicio una nueva sesión, persisto la deshabilitación y verifico el rechazo de esa sesión y de un nuevo login.

## Defecto encontrado y corrección

En la primera ejecución obtuve 27 pruebas aprobadas, 1 fallida y 2 omitidas. El caso missing:jti recibió 200 en lugar de 401: faltaba validar ese claim en OnTokenValidated. Agregué la validación de `jti` como UUID no vacío, coherente con el formato del emisor actual. También rechazo `sub` vacío y versiones de sesión negativas. Conservé la validación criptográfica y la prueba de `jti` ausente; agregué casos de `jti` malformado y rol discrepante.

Uso UUID para `jti` como contrato de mi emisor; el estándar JWT no exige ese formato. Si incorporo un proveedor externo, revisaré este contrato.

## Límites de la evidencia

Con los dobles verifico contratos HTTP, pero no demuestro persistencia, restricciones ni concurrencia SQL. Antes de cerrar la tarjeta necesito ejecutar y registrar la prueba SQL de revocación y la de unicidad concurrente. Todavía no verifiqué la ejecución remota de GitHub Actions ni modifiqué Trello. Guardo los reportes locales en `TestResults`, ignorado por Git.

En la última ejecución local compilé correctamente y obtuve 30 pruebas aprobadas, 0 fallidas y 2 SQL omitidas porque no configuré `WALLET_TEST_CONNECTION`. Guardé el reporte en `TestResults/_0fe974b3472d_2026-10-25_21_40_50.trx` y comprobé `git diff --check` sin errores.
