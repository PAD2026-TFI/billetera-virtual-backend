# Guía de Pruebas: Links de Pago y Códigos QR (Checkout Omnicanal Híbrido)

Este documento detalla paso a paso cómo probar, validar y demostrar la funcionalidad completa de **Links de Pago** y **Códigos QR**, tanto a través de la **Interfaz Gráfica de Usuario (Web Checkout UI)** como mediante **Llamadas de API (cURL / PowerShell / Swagger)**.

---

## 1. Prerrequisitos y Puesta en Marcha

Asegurate de que la API esté en ejecución en tu entorno local:

```powershell
# En la raíz del repositorio
dotnet run --project Wallet.Api
```

Una vez iniciada, dispondrás de los siguientes puntos de acceso:
- **API HTTPS Base**: `https://localhost:7081`
- **Documentación Swagger / OpenAPI**: `https://localhost:7081/swagger`
- **Interfaz Web de Checkout**: `https://localhost:7081/checkout/index.html?token={TOKEN}`

---

## 2. Método 1: Prueba Visual e Interactiva (Recomendada)

La forma más ágil y amigable de probar el flujo es utilizando la pantalla web de cobro integrada en la aplicación.

### Paso 1: Generar un Link de Cobro
Podés crearlo desde Swagger (`POST /api/v1/checkout/links`) o ejecutando el siguiente comando en PowerShell:

```powershell
$body = @{
    amount = 2500.00
    currency = "ARS"
    title = "Suscripción Premium Mensual"
    description = "Acceso total a la plataforma y soporte 24/7"
    merchantEmail = "tienda@comercio.com"
    type = 0 # 0 = SingleUse (un solo uso), 1 = MultiUse (reutilizable)
    ttlMinutes = 1440 # Opcional: vence en 24 horas
} | ConvertTo-Json

$res = Invoke-RestMethod -Uri "https://localhost:7081/api/v1/checkout/links" -Method Post -Body $body -ContentType "application/json" -SkipCertificateCheck
$res | Select-Object token, checkoutUrl, qrPayload | Format-List
```

El servidor responderá con un objeto que incluye el campo `token` y el `checkoutUrl`, por ejemplo:
```
token       : 6240b9a4ac9880b1104ca57fef49a713
checkoutUrl : https://localhost:7081/checkout/index.html?token=6240b9a4ac9880b1104ca57fef49a713
```

---

### Paso 2: Abrir la Web UI de Checkout
Copiá la URL generada (`checkoutUrl`) y abrila en cualquier navegador web moderno:
```
https://localhost:7081/checkout/index.html?token=<TOKEN_GENERADO>
```

#### ¿Qué vas a ver en pantalla?
1. **Encabezado del Cobro**:
   - Nombre del comercio o concepto (`Suscripción Premium Mensual`).
   - Descripción y monto destacado en moneda local (`$ 2,500.00 ARS`).
2. **Visualizador de Código QR**:
   - El código QR se renderiza dinámicamente en formato vectorial SVG a la derecha/abajo.
   - Cualquier dispositivo que escanee ese QR abrirá exactamente este link de pago.
3. **Pestaña 1: Tarjeta de Crédito/Débito (Simulador Financiero)**:
   - Para no tipear datos manualmente, tenés **3 botones de acceso rápido**:
     - 🟢 **Tarjeta Aprobada**: Completa una tarjeta Visa de prueba con fondos suficientes.
     - 🟠 **Fondos Insuficientes**: Completa una tarjeta con CVV `999` para simular un rechazo bancario.
     - 🔴 **Tarjeta Bloqueada**: Completa una tarjeta con terminación `9999` para simular bloqueo emisor.
   - Presioná **"Pagar con Tarjeta"**.
4. **Pestaña 2: Billetera Virtual (Transferencia Directa)**:
   - Permite abonar debitando el saldo de otra billetera ingresando su `payerWalletId`.
5. **Comprobante Digital de Pago (Receipt)**:
   - Tras completar el cobro exitosamente, la interfaz dibuja el recibo oficial con ID de transacción, código de autorización bancaria y fecha exacta.

---

## 3. Método 2: Pruebas por Consola de Comandos (cURL / PowerShell)

Si preferís realizar pruebas automatizadas o scripting, podés seguir este flujo paso a paso:

### Paso 1: Crear el Link de Pago
```bash
curl -k -i -X POST https://localhost:7081/api/v1/checkout/links \
  -H "Content-Type: application/json" \
  -d '{
    "amount": 1500.50,
    "currency": "ARS",
    "title": "Zapatillas Deportivas",
    "description": "Talle 42 - Color Negro",
    "merchantEmail": "ventas@deportes.com",
    "type": 0
  }'
```
> **Respuesta Esperada**: `201 Created` con encabezado `Location` y el JSON con el `token`.

---

### Paso 2: Consultar Información Pública del Cobro
Permite a cualquier frontend verificar los datos del cobro sin requerir autenticación:

```bash
curl -k -i -X GET https://localhost:7081/api/v1/checkout/links/<TOKEN>
```
> **Respuesta Esperada**: `200 OK`
```json
{
  "token": "6240b9a4ac9880b1104ca57fef49a713",
  "title": "Zapatillas Deportivas",
  "description": "Talle 42 - Color Negro",
  "amount": 1500.50,
  "currency": "ARS",
  "status": "Active",
  "type": "SingleUse",
  "expiresAt": null,
  "isPayable": true
}
```

---

### Paso 3: Obtener el Código QR en Formato Vectorial (SVG)
Podés solicitar directamente el archivo vectorial del código QR para incrustar en un HTML (`<img src="...">`) o imprimir:

```bash
curl -k -i -X GET https://localhost:7081/api/v1/checkout/links/<TOKEN>/qr
```
> **Respuesta Esperada**: `200 OK`, cabecera `Content-Type: image/svg+xml`, cuerpo con el XML `<svg ...>...</svg>`.

---

### Paso 4: Ejecutar el Pago

#### Caso A: Pago Aprobado con Tarjeta Simulada
```bash
curl -k -i -X POST https://localhost:7081/api/v1/checkout/pay \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: $(uuidgen)" \
  -d '{
    "token": "<TOKEN>",
    "paymentMethod": "Card",
    "card": {
      "cardNumber": "4532015112830366",
      "cardHolderName": "Juan Perez",
      "expirationMonth": 12,
      "expirationYear": 2028,
      "cvv": "123"
    }
  }'
```
> **Respuesta Esperada**: `200 OK`
```json
{
  "paymentId": "07493942-6b27-495b-91c0-6ebf38057071",
  "token": "<TOKEN>",
  "amount": 1500.50,
  "currency": "ARS",
  "destinationWalletId": "77f3b426-90fb-4435-bd66-5d7be8b5062e",
  "paymentMethod": "Card",
  "status": "Completed",
  "paidAt": "2026-10-31T06:22:25Z",
  "authorizationCode": "9C7EB069"
}
```

#### Caso B: Rechazo por Fondos Insuficientes (Simulador)
Utilizá el CVV de prueba `999`:
```bash
curl -k -i -X POST https://localhost:7081/api/v1/checkout/pay \
  -H "Content-Type: application/json" \
  -d '{
    "token": "<TOKEN>",
    "paymentMethod": "Card",
    "card": {
      "cardNumber": "4532015112830366",
      "cardHolderName": "Carlos Sin Fondos",
      "expirationMonth": 10,
      "expirationYear": 2029,
      "cvv": "999"
    }
  }'
```
> **Respuesta Esperada**: `422 Unprocessable Entity`
```json
{
  "title": "Fondos insuficientes en la tarjeta emisora de prueba.",
  "status": 422,
  "code": "INSUFFICIENT_FUNDS"
}
```

#### Caso C: Prevención de Doble Cobro (Single-Use Links)
Si intentás pagar nuevamente el link del Caso A:
```bash
curl -k -i -X POST https://localhost:7081/api/v1/checkout/pay \
  -H "Content-Type: application/json" \
  -d '{
    "token": "<TOKEN>",
    "paymentMethod": "Card",
    "card": {
      "cardNumber": "4532015112830366",
      "cardHolderName": "Juan Perez",
      "expirationMonth": 12,
      "expirationYear": 2028,
      "cvv": "123"
    }
  }'
```
> **Respuesta Esperada**: `409 Conflict`
```json
{
  "title": "This payment link has already been completed.",
  "status": 409,
  "code": "LINK_NOT_PAYABLE"
}
```

---

## 4. Matriz de Tarjetas del Simulador Financiero

Para probar todos los casos de borde sin depender de redes de pago externas, el simulador incorpora validaciones de algoritmo de **Luhn** y reglas predefinidas según los datos ingresados:

| Número de Tarjeta | CVV | Vencimiento | Resultado Esperado | Código HTTP / Error |
|---|---|---|---|---|
| `4532015112830366` (Visa) | `123` | Futuro (ej. 12/2028) | **Aprobado** | `200 OK` |
| `5555555555554444` (Mastercard) | `456` | Futuro | **Aprobado** | `200 OK` |
| Cualquiera válida | `999` | Futuro | **Fondos Insuficientes** | `422 INSUFFICIENT_FUNDS` |
| Terminada en `0000` | Cualquiera | Futuro | **Fondos Insuficientes** | `422 INSUFFICIENT_FUNDS` |
| Terminada en `9999` | Cualquiera | Futuro | **Tarjeta Bloqueada** | `422 CARD_BLOCKED` |
| Terminada en `1111` | Cualquiera | Futuro | **Sospecha de Fraude** | `422 FRAUD_SUSPECTED` |
| Cualquiera válida | `123` | Pasado (ej. 01/2020) | **Tarjeta Expirada** | `422 EXPIRED_CARD` |
| `4532015112830367` (Inválida) | `123` | Futuro | **Falla algoritmo Luhn** | `422 INVALID_CARD_NUMBER` |

---

## 5. Verificación de Impacto Contable y Saldos

Cada vez que un pago se completa con éxito, los fondos impactan inmediatamente en la billetera de destino mediante una transacción atómica con registro contable en el libro mayor (`Movements`):

1. **Si el cobro fue con Tarjeta Simulada**:
   - Se acredita el importe en la billetera receptora.
   - Se crea un movimiento contable de tipo `Credit` con origen `Funding` / `CardSimulator`.
2. **Si el cobro fue con Billetera Virtual**:
   - Se genera un débito en la billetera del pagador (`Debit`) y un crédito en la del comercio (`Credit`), garantizando que no se creen ni destruyan fondos sin partida doble.

Para auditar el saldo del comercio:
- **Comercios B2B**: Consultar `GET /api/v1/gateway/wallet` y `GET /api/v1/gateway/movements`.
- **Usuarios B2C**: Consultar `GET /api/v1/wallets` y `GET /api/v1/movements`.
