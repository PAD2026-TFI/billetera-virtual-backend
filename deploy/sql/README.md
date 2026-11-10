# Actualización SQL del hosting db78060

Archivo para subir: **db78060_upgrade_20261107.sql**.

Destino final de EF: `20261107210656_PrepareLegacyDataForHostedUpgrade`.

## Importar desde el panel

1. Guardar un respaldo de la base del hosting antes de actualizar su esquema.
2. Abrir la base **db78060** en la sección SQL Import.
3. Seleccionar **Upload SQL file** y subir `db78060_upgrade_20261107.sql` de esta carpeta.
4. Ejecutar **Import SQL file** y esperar el resultado **Success**. Revisar Detail si aparece un error.
5. Desplegar la API actualizada y reiniciarla para usar las nuevas rutas y el estado de domicilio pendiente.

No se ejecutó este script en el hosting durante su preparación. El archivo no contiene conexiones, contraseñas, API Keys, datos de prueba, CREATE DATABASE ni DROP DATABASE; usa la base seleccionada en el panel. Incluye las opciones SET requeridas por los índices filtrados de SQL Server y separadores GO.

## Qué actualiza

Es un script completo e idempotente generado desde la migración inicial hasta la última. Usa `__EFMigrationsHistory` para ejecutar solo migraciones no registradas. Por eso incluye las migraciones de tarjetas, titularidad de domicilios y estado AwaitingAddressValidation aunque el servidor todavía esté en una versión anterior a noviembre de 2026. No hay que cargar los archivos .cs ni ejecutar todos los scripts históricos uno por uno.

Prerrequisito: si la base ya tiene tablas de la aplicación, su historial EF debe corresponder con ese esquema. Una tabla existente sin su fila en `__EFMigrationsHistory` necesita conciliación previa; idempotente no significa detectar cualquier esquema modificado manualmente. La captura del panel muestra una importación anterior, pero no permite verificar el contenido del historial remoto.

La preparación detectó y corrigió problemas con datos existentes:

- CVU y alias faltantes se generan antes de crear índices únicos. Se conserva cualquier CVU o alias no vacío, así como los IDs y saldos. Los nuevos alias de registros heredados usan `legacy.` seguido del ID de la billetera.
- Se completa Users.WalletId solo cuando existe una billetera cuyo OwnerId coincide con el usuario.
- Las solicitudes antiguas sin tipo se normalizan a Debit.
- Los CUIT heredados vacíos quedan pendientes de completar con datos reales; no se inventa una identidad fiscal. El índice exige unicidad para CUIT no vacíos.
- Si se atraviesa la migración histórica a WalletOwner, se conserva IsActive y CreatedAt de cada usuario, en lugar de reactivar cuentas o cambiar fechas.
- Las direcciones heredadas sin titular quedan con UserId nulo; no se asignan dueños por inferencia.

Se corrigieron las migraciones históricas que deben pasar esas transiciones antes de llegar a la última. La nueva migración correctiva atiende bases que ya tenían registradas esas migraciones; EF no vuelve a ejecutar una migración registrada.

No usar una reversión automática de estas migraciones: los CVU/alias generados pueden haberse comunicado a usuarios y el índice anterior de CUIT no admite varios valores vacíos. La reducción de Status a nvarchar(20) tampoco admite AwaitingAddressValidation sin resolver primero esos pedidos.

## Regenerar y verificar

Desde la raíz del repositorio:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/New-HostingMigration.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-HostingMigration.ps1
```

La opción ExecutionPolicy se limita al proceso y no cambia la configuración persistente del equipo. El generador usa .NET SDK y dotnet-ef 9.0.21. La verificación necesita SQL Server LocalDB y sqlcmd; crea únicamente bases con nombres aleatorios WalletHostingUpgradeTest_* y las elimina al finalizar.

Verificación ejecutada: cuatro escenarios, con dos importaciones en cada uno:

- Base vacía.
- Esquema hasta Transfers, con dos usuarios, una cuenta inactiva y billeteras con saldo.
- Esquema hasta AddPaymentEntityAndB2BConsultation, con usuarios, billeteras, dos comercios sin CUIT y un pedido previo de tarjeta.
- Esquema hasta SupportPendingCardDeliveryAddress, con CVU/alias existentes, valores faltantes y CUIT real preservado.

Todos pasaron; la segunda importación conservó exactamente los mismos datos de billeteras. El modelo EF no presenta diferencias pendientes respecto de la migración final.

## Verificación de correo al crear una cuenta

La migración `AddRegistrationEmailVerification` agrega la tabla de desafíos de correo. Para un hosting que ya tenga aplicada `20261109201218_AddMerchantApiIntegration`, usá el script incremental `padi_email_verification.sql` en **SQL Import**. Verificá primero que la migración anterior figure en `__EFMigrationsHistory`. Si la base está más atrasada, generá un script EF desde su última migración aplicada hasta `AddRegistrationEmailVerification`.

Para publicar el registro con verificación, coordiná estos pasos en el mismo despliegue.

1. Importá la migración y verificá que figure en el historial.
2. Publicá el backend. En las variables del sitio de MonsterASP, configurá `Mail__Host`, `Mail__Port`, `Mail__EnableSsl`, `Mail__Username` y `Mail__Password`. Para Gmail usá `smtp.gmail.com`, puerto `587` y TLS activado. El usuario es el remitente y la contraseña es la de aplicación de ese Gmail.
3. Agregá `Mail__Enabled=true` y reiniciá el backend. Desde ese momento, las altas por API requieren la verificación. Publicá el frontend que llama a los endpoints nuevos como parte de este mismo cambio.
4. Probá un registro con un correo de prueba que puedas abrir. Comprobá que llegue el código, que la app rechace uno incorrecto y que la cuenta se cree después de verificar el correcto. Probá también el login con una cuenta anterior.

El código vence en 10 minutos y admite cinco intentos. El reenvío se habilita después de un minuto. La confirmación devuelve un token válido por 20 minutos, ligado al correo y consumido al crear la cuenta. Las altas de desarrollo conservan el flujo anterior mientras `Mail__Enabled` esté desactivado.
