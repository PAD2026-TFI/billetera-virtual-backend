# Publicar Wallet API en Mintlify Starter

El portal vive en `deploy/mintlify`. Contiene las guías para comercios externos y la referencia de las operaciones que necesitan para integrar el checkout alojado de Padi. El contrato se exportó desde el backend `develop` en `b22502f`. Mintlify publica la documentación desde GitHub y la API sigue en MonsterASP.NET.

## Vista previa local

Con Node.js, ejecutá desde la raíz del backend:

```powershell
Push-Location deploy/mintlify
npx.cmd --yes mint@latest validate
npx.cmd --yes mint@latest dev
Pop-Location
```

Abrí `http://localhost:3000`. Revisá la pestaña **Guías** y al menos una operación de **Referencia de la API**. [CLI de Mintlify](https://www.mintlify.com/docs/installation)

## Conectar el repositorio

1. Subí `deploy/mintlify`, `docs/mintlify-publicacion.md` y `scripts/export-public-openapi.mjs` a una rama del backend que puedas seleccionar en GitHub. Para mantener el contrato alineado con el backend, seleccioná `develop` una vez que esa rama contenga el portal y represente la versión que quieren documentar.
2. Creá el proyecto en [Mintlify](https://app.mintlify.com/). Durante el alta pueden ofrecer una prueba de Pro. El sitio puede continuar en **Starter** al terminar la prueba.
3. En **Git Settings**, conectá `PAD2026-TFI/billetera-virtual-backend`. Si se solicita instalar la app de GitHub, limitá el acceso a ese repositorio. La autorización requiere permisos de administración o la aprobación de un administrador de la organización.
4. Seleccioná la rama de publicación. Activá **docs.json is in a subdirectory** e ingresá `/deploy/mintlify`, sin barra final.
5. Revisá el estado del despliegue en el panel y abrí la URL asignada. Comprobá que el catálogo muestre las siete operaciones de alta, links de pago, consulta de cobros, detalles públicos y QR.

[Plan Starter](https://www.mintlify.com/pricing) · [Conexión de repositorio y subdirectorio](https://www.mintlify.com/library/publish-open-source-local-docs-on-mintlify)

## Actualizar el OpenAPI

Levantá en `Development` el commit del backend que querés documentar, con el perfil `https`. Exportá el contrato desde la raíz del repositorio y ejecutá el filtro para el portal.

```powershell
$walletOpenApi = Join-Path $env:TEMP 'wallet-openapi.json'
curl.exe --fail --silent --show-error https://localhost:7081/openapi/v1.json --output $walletOpenApi
if ($LASTEXITCODE -ne 0) { throw 'No se pudo exportar el contrato de la API.' }
node scripts/export-public-openapi.mjs --mintlify $walletOpenApi
```

El archivo resultante es `deploy/mintlify/openapi.json`. El exportador usa una lista explícita de operaciones del checkout para que los endpoints nuevos del backend no aparezcan por defecto. Para incluir la URL de la API en los ejemplos generados, agregá el origen HTTPS real de MonsterASP.NET como último argumento. El explorador está en modo consulta.
