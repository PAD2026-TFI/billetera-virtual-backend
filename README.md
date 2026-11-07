# Billetera Virtual & Pasarela de Pagos B2B (WaaS) — Backend

Backend financiero desarrollado en .NET bajo principios de **Clean Architecture** y **Domain-Driven Design (DDD)**. El sistema opera bajo un modelo híbrido que provee tanto servicios de **Billetera Virtual para Usuarios Finales (B2C)** como infraestructura de **Pasarela de Pagos para Comercios y Aplicaciones Externas (B2B - Wallet as a Service)**.

---

## 1. Arquitectura del Sistema

El proyecto está organizado en capas desacopladas orientadas al dominio:

- **`Wallet.Domain`**: Núcleo del dominio rico sin dependencias externas.
  - **Jerarquía Polimórfica de Titularidad (`WalletOwner`)**: Clase base abstracta de la que heredan `User` (personas físicas) y `ExternalClient` (comercios/APIs externas).
  - **Entidades y Agregados**: `Wallet` (asociada a un `OwnerId`), `Transfer` (`TransferStatus`), `Movement` (`MovementType`, `MovementSource`), `DevelopmentFunding`, `Address` (`AddressValidationStatus`) y `CardRequest` (`CardRequestStatus`).
  - **Value Objects**: `Money` y `MoneyRules` (inmutabilidad, validación de moneda `ARS` y precisión a 2 decimales).
- **`Wallet.Application`**: Casos de uso y orquestación transaccional (`IdentityService`, `WalletService`, `TransferService`, `MovementHistoryService`, `GatewayService`, `DevelopmentFundingService`).
- **`Wallet.Infrastructure`**: Persistencia relacional con **EF Core** y **SQL Server**.
  - Estrategia **TPT (Table-per-Type)** mapeando `[WalletOwners]`, `[Users]` y `[ExternalClients]`.
  - Repositorios SQL con control de concurrencia (`RowVersion` y `HOLDLOCK`), servicio de hashing de contraseñas (`IdentityPasswordService`) y migraciones de base de datos.
- **`Wallet.Api`**: Exposición de endpoints REST, manejo centralizado de errores (`ApiExceptionHandler`), limitación de tasa (`RateLimiter`), autenticación híbrida (`JWT Bearer` + `X-Api-Key`) y documentación interactiva en Swagger/OpenAPI con herramientas de autogeneración de claves de idempotencia (`wallet-tools.js`).
- **`Wallet.DevelopmentTools`**: Herramienta CLI (`fund-wallet`) para acreditación segura y auditable de fondos de prueba en entornos de desarrollo.
- **`Wallet.Domain.Tests` & `Wallet.Tests`**: Suites de pruebas unitarias de dominio y pruebas de integración transaccional sobre SQL Server.

---

## 2. Módulos Funcionales

### A. Módulo B2C — Billetera Virtual para Personas Físicas (`User`)
- **Identidad y Sesiones (`AuthController`, `UsersController`)**:
  - Registro e inicio de sesión de usuarios con correo normalizado y hash seguro de contraseña.
  - Autenticación mediante tokens **JWT (`Bearer`)** con control y revocación inmediata de sesiones activas mediante `SessionVersion`.
  - Gestión de domicilio (`Address`) y solicitudes de tarjeta (`CardRequest`).
- **Gestión de Billetera (`WalletsController`)**:
  - Creación de billetera personal en pesos argentinos (`ARS`) y consulta de saldo en tiempo real.
- **Transferencias entre Usuarios (`TransfersController`)**:
  - Envío atómico de fondos entre billeteras con protección contra sobregiros, validación de titular activo y soporte nativo de idempotencia (`Idempotency-Key`).
- **Libro Mayor / Historial de Movimientos (`MovementsController`)**:
  - Consulta paginada por cursor (`MovementCursorCodec`) del historial inmutable de créditos y débitos con registro de saldo posterior (`BalanceAfter`).

### B. Módulo B2B — Pasarela de Pagos / Wallet as a Service (`ExternalClient`)
- **Onboarding de Comercios (`/api/v1/gateway/clients`)**:
  - Registro de aplicaciones y comercios externos con generación atómica de su `ExternalClient`, su `Wallet` en `ARS` y una `ApiKey` criptográfica (almacenada en base de datos únicamente como hash `SHA-256`).
- **Autenticación M2M (`ApiKeyAuthenticationHandler`)**:
  - Validación por cabecera `X-Api-Key` verificando hash y estado activo del titular en `WalletOwners`.
- **Operaciones Financieras y Conciliación (`/api/v1/gateway/*`)**:
  - Consulta y aprovisionamiento de billetera comercial (`/wallet`).
  - Ejecución de pagos y transferencias hacia otras billeteras (`/payments`) con garantía de reintento seguro (`Idempotency-Key` y cabecera de respuesta `Idempotency-Replayed`).
  - Auditoría contable de movimientos (`/movements`) para conciliación en tiempo real.

### C. Módulo Omnicanal — Links de Pago, Códigos QR y Checkout Web Híbrido
- **Generación de Cobros (`/api/v1/checkout/links`)**:
  - Permite a usuarios registrados o vendedores invitados (no registrados) generar links de cobro instantáneos y códigos QR (SVG vectorial).
  - Soporta links de un solo uso (`SingleUse`) o recurrentes (`MultiUse`), con tiempo de expiración configurable (`ttlMinutes`).
- **Interfaz Web de Checkout (`/checkout/index.html?token=...`)**:
  - Pantalla responsiva alojada en los archivos estáticos de la API para visualización y liquidación del cobro.
- **Múltiples Métodos de Liquidación (`/api/v1/public/checkout/payments`)**:
  - **Saldo en Billetera Virtual (B2C)**: Débito directo de fondos con validación de saldo.
  - **Tarjetas Artificiales (Simulador Financiero)**: Motor de simulación con validación de algoritmo de Luhn, fecha de expiración y códigos de prueba predefinidos (aprobado, fondos insuficientes, tarjeta bloqueada, sospecha de fraude). Acreditación y ledger contable atómico en la billetera receptora.

---

## 3. Reglas de Negocio e Invariantes Financieras

- **Moneda y Precisión**: Operaciones exclusivas en `ARS` con precisión exacta de 2 decimales (centavos).
- **Límites por Operación**: Importe mínimo de `0,01 ARS` y tope máximo por transferencia de `780.400,00 ARS`.
- **Prohibición de Sobregiros**: Ninguna billetera puede quedar con saldo negativo (`409 INSUFFICIENT_FUNDS`).
- **Idempotencia Estricta**: Cada operación registra la `Idempotency-Key` junto al hash SHA-256 del payload (origen, destino, monto). Reintentar con el mismo payload devuelve el resultado original (`Idempotency-Replayed: true`); alterar los parámetros con la misma clave arroja `409 IDEMPOTENCY_CONFLICT`.
- **Doble Partida e Inmutabilidad**: Toda transferencia genera registros inmutables `Debit` y `Credit` en `Movements` dentro de la misma transacción SQL.

---

## 4. API por audiencia

Swagger agrupa las rutas por consumidor y muestra los nombres canónicos. Las rutas anteriores conservan compatibilidad.

| Consumidor | Rutas | Autenticación |
| --- | --- | --- |
| Registro y acceso | `/api/v1/auth/*` | Públicas |
| Clientes | `/api/v1/me/*` | JWT de cliente |
| Comercios y apps externas | `/api/v1/integrations/*` | API Key; alta pública |
| Checkout | `/api/v1/public/*` | Consulta pública; JWT para saldo o tarjeta asociada |
| Administración | `/api/v1/admin/*` | JWT Admin; alta con clave de configuración |
| Salud | `/health/*` | Públicas |

La tarjeta asociada de débito paga desde el saldo de su billetera mediante `POST /api/v1/me/payment-links/{paymentLinkId}/card-payments`. La solicitud de tarjeta física acepta el domicilio completo en `POST /api/v1/me/physical-card-orders`.

Ver [guía de pagos, domicilios y endpoints](docs/api-audiences-debit-and-card-orders.md) para ejemplos, requisitos de idempotencia, estados de validación y migraciones. Ejemplos ejecutables: [Wallet.Api.http](Wallet.Api/Wallet.Api.http).

---
## 5. Ejecución Local y Herramientas de Desarrollo

### Requisitos
- .NET SDK (según `global.json`)
- SQL Server / LocalDB (`(localdb)\MSSQLLocalDB`)

### Iniciar la API
```powershell
dotnet run --project Wallet.Api