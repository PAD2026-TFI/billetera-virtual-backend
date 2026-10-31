# Guía Integral de Integración de Pasarela de Pagos (B2B, Links de Pago y Códigos QR)

Este documento detalla la arquitectura, visión de negocio, especificación técnica y guía de integración paso a paso de la **Pasarela de Pagos (Wallet as a Service - WaaS)** y el **Checkout Omnicanal Híbrido (Links de Pago & QR)** de la plataforma de Billetera Virtual.

---

## 1. Contexto y Visión de Negocio

El sistema provee dos modalidades complementarias de recaudación y cobro digital:

1. **Modelo B2B M2M (Machine-to-Machine)**: Diseñado para plataformas de e-commerce o aplicaciones backend corporativas que se integran de servidor a servidor consumiendo la API de pasarela (`/api/v1/gateway/*`) mediante cabeceras criptográficas de autenticación `X-Api-Key` y control estricto de idempotencia (`Idempotency-Key`).
2. **Modelo Omnicanal Híbrido (Links de Pago y Códigos QR)**: Diseñado para democratizar el cobro digital, permitiendo que tanto comercios registrados como profesionales o vendedores informales generen enlaces públicos de cobro y códigos QR en formato SVG. Al abrir el link o escanear el QR, el pagador accede a una **Web Checkout UI** unificada y responsiva donde abona con **Saldo de Billetera Virtual** o con **Tarjetas de Débito/Crédito Simuladas**.

---

## 2. Formato IDEAL del Ecosistema

### A. Intención
- Proveer una infraestructura de pagos de alta disponibilidad, tolerancia a fallos y auditoría de doble partida para el ecosistema financiero digital.
- Garantizar que cualquier comercio o particular pueda recibir cobros instantáneos en pesos argentinos (`ARS`) sin requerir terminales físicas de punto de venta (POS) ni complejas homologaciones bancarias externas durante el ciclo de vida del producto.

### B. Descripción
- **Clean Architecture & DDD**:
  - `Wallet.Domain`: Contiene los agregados y entidades esenciales: `WalletOwner` (jerarquía TPT con `User` y `ExternalClient`), `Wallet` (cuenta de balance en `ARS`), `Transfer`, `Movement` (asientos contables inmutables de débito/crédito) y `PaymentLink` (intención de cobro, estado `Pending`, `Completed`, `Expired`, `Cancelled`).
  - `Wallet.Application`: Casos de uso de pasarela (`GatewayService`), checkout omnicanal (`PaymentLinkService`) y simulador financiero de tarjetas (`CardSimulatorService`).
  - `Wallet.Infrastructure`: Mapeos relacionales con EF Core, soporte de índices únicos para tokens de cobro, transacciones atómicas SQL e integración con SQL Server / LocalDB.
  - `Wallet.Api`: Exposición REST con control de concurrencia, serialización estricta (`JsonUnmappedMemberHandling.Disallow`), Swagger OpenAPI interactivo con inyección de scripts para idempotencia automática (`wallet-tools.js`), y servidor estático para la interfaz web de checkout (`/checkout/index.html`).

### C. Ejemplo de Flujo de Extremo a Extremo

#### Flujo 1: Cobro M2M con API Key (B2B)
1. **Onboarding del Comercio**:
   ```http
   POST /api/v1/gateway/clients HTTP/1.1
   Content-Type: application/json

   {
     "companyName": "Tech Store SRL",
     "notificationEmail": "pagos@techstore.com",
     "webhookUrl": "https://techstore.com/webhooks/payments"
   }
   ```
   *Respuesta*: Retorna `clientId`, `walletId` y la `apiKey` en texto plano (mostrada una única vez; en base de datos se almacena su hash SHA-256).

2. **Ejecución de Pago / Transferencia**:
   ```http
   POST /api/v1/gateway/payments HTTP/1.1
   Host: localhost:7081
   X-Api-Key: waas_sec_xxxxxxxxxxxxxxxxxxxx
   Idempotency-Key: a4c89284-984e-4b62-9e2c-29b1e9c20a81
   Content-Type: application/json

   {
     "destinationWalletId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
     "amount": 1500.00,
     "currency": "ARS"
   }
   ```

#### Flujo 2: Cobro Omnicanal con Link de Pago y QR
1. **Creación del Enlace de Cobro**:
   ```http
   POST /api/v1/checkout/links HTTP/1.1
   Content-Type: application/json

   {
     "amount": 2500.00,
     "currency": "ARS",
     "title": "Curso de Desarrollo Backend",
     "description": "Acceso a la plataforma y mentoría",
     "merchantEmail": "academia@code.com",
     "type": 0,
     "ttlMinutes": 1440
   }
   ```
   *Respuesta*:
   ```json
   {
     "id": "e43b...",
     "token": "6240b9a4ac9880b1104ca57fef49a713",
     "amount": 2500.00,
     "currency": "ARS",
     "status": "Active",
     "checkoutUrl": "https://localhost:7081/checkout/index.html?token=6240b9a4ac9880b1104ca57fef49a713",
     "qrPayload": "https://localhost:7081/checkout/index.html?token=6240b9a4ac9880b1104ca57fef49a713"
   }
   ```

2. **Obtención del Código QR**:
   `GET /api/v1/checkout/links/6240b9a4ac9880b1104ca57fef49a713/qr` retorna un gráfico vectorial puro en formato SVG (`image/svg+xml`), apto para ser impreso en un mostrador o renderizado dinámicamente en una página web.

3. **Liquidación del Pago en el Checkout**:
   El usuario abre el link en su navegador o la aplicación invoca el endpoint de pago:
   ```http
   POST /api/v1/checkout/pay HTTP/1.1
   Content-Type: application/json
   Idempotency-Key: c9d02081-3318-47bc-8c3b-7ea3295db283

   {
     "token": "6240b9a4ac9880b1104ca57fef49a713",
     "paymentMethod": "Card",
     "card": {
       "cardNumber": "4532015112830366",
       "cardHolderName": "Juan Pérez",
       "expirationMonth": 12,
       "expirationYear": 2028,
       "cvv": "123"
     }
   }
   ```
   *Respuesta*: `200 OK` con comprobante contable y código de autorización. La billetera del comercio es acreditada inmediatamente y se genera el respectivo movimiento en el libro mayor.

### D. Acción
- **Validación Automática de Tarjetas de Prueba**:
  - Algoritmo de Luhn para números de tarjeta.
  - Validación de vigencia (mes/año) y código CVV (3 o 4 dígitos).
  - Escenarios de prueba simulados:
    - Tarjeta aprobada: números válidos por Luhn con CVV estándar (ej: `123`).
    - Fondos insuficientes: CVV `999` o número terminado en `0000` $\rightarrow$ `422 INSUFFICIENT_FUNDS`.
    - Tarjeta bloqueada: número terminado en `9999` $\rightarrow$ `422 CARD_BLOCKED`.
    - Sospecha de fraude: número terminado en `1111` $\rightarrow$ `422 FRAUD_SUSPECTED`.
- **Resolución de Comercios No Registrados**:
  - Si quien genera el link no está autenticado, puede proporcionar su `merchantEmail`. El sistema crea de forma transparente un usuario y billetera puente en estado pendiente de activación, asegurando que los fondos no se extravíen y permanezcan acreditados a su nombre.

### E. Límites y Restricciones
1. **Moneda Única**: Operaciones restringidas estrictamente a `ARS`. Cualquier otra divisa es rechazada con `400 INVALID_INPUT`.
2. **Tope de Transferencia**: Mínimo `0,01 ARS`, máximo `780.400,00 ARS` por transacción unitaria.
3. **Idempotencia de Enlaces**: Los links de tipo `SingleUse` (`0`) se marcan como `Completed` inmediatamente después del primer pago exitoso; cualquier reintento de pago posterior devuelve `409 LINK_NOT_PAYABLE`.
4. **Seguridad y Privacidad**:
   - Las ApiKeys se hashean con `SHA-256` y nunca se guardan en texto plano.
   - La interfaz pública de checkout (`GET /api/v1/checkout/links/{token}`) no expone datos sensibles ni identidades privadas del titular.

---

## 3. Endpoints Disponibles

| Endpoint | Método | Auth | Descripción |
|---|---|---|---|
| `/api/v1/gateway/clients` | `POST` | Libre | Alta de clientes B2B y provisión de credenciales. |
| `/api/v1/gateway/wallet` | `GET` / `POST` | `X-Api-Key` | Consulta y gestión de billetera de la aplicación. |
| `/api/v1/gateway/payments` | `POST` | `X-Api-Key` | Transferencias B2B con `Idempotency-Key`. |
| `/api/v1/gateway/movements` | `GET` | `X-Api-Key` | Conciliación y libro contable con cursor. |
| `/api/v1/checkout/links` | `POST` | Híbrido | Creación de links de pago para comercio registrado o invitado. |
| `/api/v1/checkout/links/{token}` | `GET` | Libre | Información pública para renderizar el checkout. |
| `/api/v1/checkout/links/{token}/qr` | `GET` | Libre | Código QR vectorial SVG dinámico. |
| `/api/v1/checkout/pay` | `POST` | `Idempotency-Key` | Procesamiento de pago vía Tarjeta Simulada o Billetera. |
| `/checkout/index.html?token={token}` | `GET` | Libre | Interfaz web responsiva con presets de prueba. |
