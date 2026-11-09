# Integración API vinculada al vendedor

El usuario conserva su identidad `Client`, su billetera y los enlaces ya creados. `accountType` distingue `personal` de `merchant` y se persiste en `Users`. La nueva clave autentica con el mismo `sub` del usuario, pero con el rol limitado `MerchantIntegration`; nunca autoriza las operaciones JWT del usuario ni las transferencias B2B.

## Administración desde la app (JWT)

| Método y ruta | Resultado |
| --- | --- |
| `PUT /api/v1/me/account-type` | `{ "accountType": "merchant" }` o `personal`. Pasar a personal revoca la clave activa. |
| `GET /api/v1/me/merchant/integration` | Estado y metadatos, nunca la clave completa ni su hash. |
| `POST /api/v1/me/merchant/integration` | `{ "name": "Mi tienda" }`. Devuelve `integration` y `apiKey` una sola vez. Falla con 409 si ya hay una activa. |
| `POST /api/v1/me/merchant/integration/rotate` | Reemplaza la clave activa y muestra la nueva una vez. La anterior deja de autenticar. |
| `DELETE /api/v1/me/merchant/integration` | Revoca la clave sin borrar links ni pagos. Repetir la revocación es seguro. |

La generación requiere un vendedor activo y una billetera existente. La clave tiene el prefijo `padi_` y 32 bytes aleatorios; se almacena solo SHA-256, un prefijo visible y fechas. El límite es una integración/clave activa por usuario. La fila del titular usa la concurrencia optimista existente de EF; un conflicto al guardar devuelve 409 y exige consultar el estado. Las respuestas son `no-store` y las generaciones/rotaciones tienen limitación de solicitudes.

## Uso desde el servidor del comercio (X-Api-Key)

La clave permite:

- `POST /api/v1/integrations/payment-links`
- `GET /api/v1/integrations/payment-links/{id}`
- `GET /api/v1/integrations/received-payments`
- `GET /api/v1/integrations/received-payments/{paymentId}`

Los endpoints de creación/consulta verifican el mismo propietario que usa la sesión en `/api/v1/me/merchant/payment-links`. No se crea una segunda billetera. Los cobros manuales y automatizados son visibles en la misma cuenta.

```bash
curl "$PADI_API_URL/api/v1/integrations/payment-links" \
  -H "X-Api-Key: $PADI_API_KEY" \
  -H 'Content-Type: application/json' \
  --data '{"amount":1500,"title":"Pedido 1042","type":"SingleUse"}'
```

Guardar `id` junto al pedido y enviar al comprador a `checkoutUrl`. Para confirmar, verificar `paymentLinkId`, `amount`, `currency` y `status: "Succeeded"` en el pago recibido. El retorno al frontend no confirma un pago. La creación de enlaces no tiene deduplicación por `Idempotency-Key`; ante timeout no reintentar automáticamente. El listado devuelve los pagos recientes con `limit` (20 por defecto), sin cursor de paginación. No se incluyen webhooks en esta versión.

Las claves nuevas no permiten transferencias, consultar el saldo completo, administrar webhooks ni el perfil de la integración B2B. Las claves de `ExternalClient` existentes conservan su comportamiento previo.

## Despliegue coordinado

1. Aplicar la migración `AddMerchantApiIntegration` a una base de pruebas y validar la persistencia/concurrencia.
2. Desplegar el backend con el esquema actualizado.
3. Desplegar el frontend de la rama correspondiente con `VITE_API_BASE_URL` apuntando a ese backend. La clave nunca va en variables `VITE_*`.
4. Los usuarios existentes quedan con `accountType: "personal"`, porque la modalidad anterior vivía solo en el navegador. Pueden activar Vendedor desde Mi perfil sin cambiar de billetera ni recibir nuevamente el bono.
5. Probar generación, link visible en la app, reemplazo (clave anterior 401) y revocación (clave revocada 401).

No se aplica automáticamente ninguna migración ni se activan claves en el backend alojado. Una reversión debe coordinar ambas aplicaciones; no quitar las columnas mientras siga corriendo esta versión del backend.
