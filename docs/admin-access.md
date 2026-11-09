# Acceso administrativo y documentación interna

## Cambio de autorización

Todo `AdminController` exige la política `Admin`. El registro de administradores ya no permite acceso anónimo: tanto `POST /api/v1/admin/register` como su alias `POST /api/admin/register` requieren el JWT vigente de un administrador existente. Las sesiones se validan contra el usuario activo y su rol en el servidor.

- Sin JWT: 401 antes de validar o persistir el cuerpo.
- JWT de Client, incluido un vendedor: 403.
- API Key de comercio sin JWT Admin: 401.
- JWT Admin vencido, revocado o de un usuario desactivado: 401.
- JWT Admin válido: puede crear otro administrador, sujeto a validación y límite de solicitudes.

Las rutas de fondeo administrativo conservan su política Admin. La política a nivel controlador protege también futuras acciones, salvo que se introduzca una excepción explícita que debe revisarse.

No existe un registro público de primer administrador ni una clave de configuración para saltar esta política. Antes de desplegar, confirmar que el equipo conserva una cuenta Admin válida. Si no existe ninguna, el primer administrador debe provisionarse mediante un procedimiento interno controlado, fuera del registro público; no agregar una excepción anónima temporal.

## Swagger y OpenAPI

Se publican únicamente cuando se cumplen ambas condiciones:

1. `ASPNETCORE_ENVIRONMENT=Development`.
2. `Documentation:Enabled=true`, configurado explícitamente para desarrollo local (por ejemplo, `Documentation__Enabled=true` en su entorno).

El valor predeterminado en appsettings es false. En Production permanecen deshabilitados incluso si el flag es true. Cuando están deshabilitados, `/swagger/*` y `/openapi/*` responden 404 y `/` redirige a `/health/live`.

El opt-in de desarrollo no es un sistema de inicio de sesión para Swagger: no habilitarlo en el hosting público. Mintlify mantiene su propia referencia comercial curada en archivos, por lo que no necesita acceder al catálogo completo del backend en cada visita.

## Despliegue

No hay cambios de modelo, esquema ni migraciones en esta corrección. Publicar los archivos actualizados del backend y reiniciar la app en MonsterASP; no ejecutar una migración nueva para este cambio. Mantener Documentation:Enabled=false en el hosting y preferir el entorno Production con la configuración de identidad correspondiente.

Después del despliegue, verificar con cuerpos vacíos (sin crear cuentas ni acreditar fondos):

- Ambas rutas de registro Admin sin JWT devuelven 401; con JWT de usuario normal devuelven 403.
- Las rutas de fondeo siguen devolviendo 401/403 a esos actores.
- Swagger y el OpenAPI completo devuelven 404.
- Registro/login de usuarios y cobros comerciales siguen funcionando.

La revisión previa confirmó que el registro publicado llegaba a validar cuerpos sin autenticar y que Swagger estaba público. La corrección no elimina ni audita administradores existentes. Revisar esos accesos con el responsable de la plataforma si corresponde; no se crearon cuentas Admin ni se modificaron datos alojados durante esta revisión.
