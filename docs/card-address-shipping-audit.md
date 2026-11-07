# Auditoría de tarjetas, domicilios y preparación logística

> Actualización posterior: el ingreso de domicilio completo, el estado AwaitingAddressValidation y el débito desde billetera ya fueron implementados. Consultar [la guía actual de pagos y endpoints](api-audiences-debit-and-card-orders.md). Este documento conserva el diagnóstico de la revisión inicial.

Fecha: 2026-11-07. Proyecto Wallet / TFI_ICS, .NET 9.
Alcance: código, configuraciones EF Core y migraciones del repositorio. No se consultaron datos de la base desplegada ni se aplicaron migraciones.

## Tarjetas: hallazgos y ajustes

No hay FluentValidation en este flujo. Se usan DataAnnotations en los comandos y guardas en las entidades/servicios.

- Card almacena CardNumberLast4 y CardToken; no almacena PAN completo ni CVV. Se conserva este modelo. Un token generado localmente con Guid es una referencia de simulación, no evidencia de tokenización con un emisor real.
- CardSimulatorService aceptaba 13 a 19 dígitos, eliminaba caracteres antes de validar y admitía CVV de 3 o 4 dígitos. Ahora exige exactamente 16 dígitos ASCII y CVV de exactamente 3, sin normalización silenciosa, y mantiene Luhn. Se excluye intencionalmente el formato Amex de 15/4 conforme al requisito del proyecto.
- Card.Create validaba un año mínimo fijo 2026 y no comprobaba el mes actual. Ahora rechaza vencimientos anteriores al mes UTC de emisión, y años fuera de 1..9999. IsExpired compara año/mes sin AddMonths, evitando overflow en diciembre de 9999. La tarjeta es válida hasta finalizar el mes UTC de vencimiento.
- El simulador usa TimeProvider y requiere un año completo válido; ya no convierte automáticamente 30 en 2030. Los clientes deben enviar 2030.
- Las últimas cuatro cifras se restringen a ASCII; el DTO deja de tener el límite fijo de años 2026..2045.
- AddCard recibe últimos cuatro dígitos y token, por lo que no puede verificar el PAN original ni su CVV. La validación de PAN/CVV pertenece al flujo transitorio de autorización. Antes de producción se debe sustituir el simulador por un proveedor de tokenización/emisión y verificar sus referencias; no agregar PAN/CVV a Card.

## Direcciones: modelo real y aislamiento

Address ya existía en dominio, DbSet Addresses, configuración EF y migraciones: Street, Number, City, State, PostalCode, Country, Latitude, Longitude, ValidationStatus, ExternalPlaceId, CreatedAt y ValidatedAt. Incluye límites de longitud y validación de rangos de coordenadas.

El problema era la ausencia de relación con User. FindDefaultValidAddressAsync elegía cualquier dirección validada de toda la base, y un AddressId explícito tampoco se verificaba contra el solicitante.

Ajustes:

- Address.UserId nullable con FK a User y borrado restringido. Relación User 1 -> N Address. No es necesario agregar navegación al agregado User para mapearla.
- Todas las consultas del repositorio de direcciones reciben userId y filtran por titular. La selección implícita usa la dirección válida más reciente del usuario, con desempate estable por Id; no existe todavía una preferencia IsDefault.
- CardRequest.Create exige dirección validada y del mismo titular. La comprobación vive también en dominio.
- El endpoint devuelve 404 para domicilio inexistente o ajeno y 400 para ausencia de domicilio válido.
- UserId nulo se reserva para filas heredadas. No se asignan dueños por inferencia ni se habilitan esas filas para nuevos pedidos. La migración solo agrega columna, índice y FK. Debe hacerse un backfill verificado antes de volver obligatoria la columna y eliminar la fábrica de borradores sin dueño.

Pendientes funcionales: no hay un módulo API/Application para alta, edición y selección de domicilios; hoy la entidad y el repositorio por sí solos no completan el onboarding. Se propone alta autenticada con el UserId del contexto, sin aceptar dueño desde el cuerpo; piso, departamento, destinatario e indicaciones opcionales con límites; validación mediante IAddressGeocoder y nueva validación ante cambios de domicilio. Valid no prueba actualmente que un proveedor externo haya certificado la dirección: MarkValid es un método de dominio y aún no hay adaptador.

## Preparación para geolocalización y tracking

Se agregaron contratos sin proveedor concreto en Wallet.Application/CardShippingContracts.cs:

- IAddressGeocoder.ValidateAsync recibe un domicilio estructurado y devuelve validez, coordenadas e identificador externo.
- ICardShippingProvider.CreateShipmentAsync recibe el ID de pedido como clave estable de idempotencia y un DeliveryDestination inmutable. Devuelve proveedor, ID externo y número de seguimiento.
- GetTrackingAsync devuelve eventos con identificador externo, fecha y estado normalizado: Requested, Dispatched, InTransit, Delivered, Failed o Returned.

Los futuros adaptadores HTTP deben vivir en Infrastructure, configurarse en DI y usar opciones para URL/credenciales, Timeouts y CancellationToken. No hay llamadas externas ni adaptadores ficticios registrados en esta entrega.

### Ciclo de vida y persistencia propuestos

CardRequest YA tiene Pending -> Processing -> Issued -> InTransit -> Delivered, además de Cancelled/Rejected y guardas de transición. El endpoint GET de status devuelve estado local, no tracking del transportista. Se conserva el enum actual por compatibilidad; los estados de CardShipmentStatus son un contrato de integración y no alteran pedidos persistidos.

Para la siguiente implementación:

1. Crear CardShipment separado de Card, relacionado con CardRequest (un envío por pedido en la primera versión, índice único). Persistir snapshot de destino, Provider, ExternalShipmentId, TrackingNumber, Status, LastEventAt y control de concurrencia.
2. Persistir el snapshot al solicitar la tarjeta, antes de habilitar edición de domicilios. Actualmente solo se conserva AddressId; no hay snapshot del texto del domicilio.
3. Mapear Pending a Requested del pedido; conservar Processing/Issued como pasos de emisión. El despacho genera Dispatched en el envío; InTransit y Delivered sincronizan el pedido mediante sus métodos de dominio. Agregar Dispatched al pedido solo con una migración explícita del contrato y sus transiciones. No marcar un pedido Delivered únicamente por recibir una petición del cliente.
4. Persistir eventos de tracking con clave única (Provider, ExternalEventId), correlación con envío y OccurredAt. Duplicados son no-op; eventos fuera de orden no hacen retroceder estado. Failed/Returned requieren reglas propias, sin tratarlos como Delivered.
5. Enviar órdenes por outbox con clave idempotente, sin mantener transacciones SQL abiertas durante HTTP. Resolver reintentos con la misma clave para evitar dos despachos si hubo timeout tras aceptación del proveedor.
6. Agregar polling en worker o webhook autenticado/verificado con replay protection. Persistir evento y transición en una transacción, con concurrencia optimista. Exponer tracking solo al titular o roles administrativos autorizados.
7. Probar contrato del adaptador, duplicados, desorden, timeout con aceptación remota, dirección ajena y conservación del snapshot tras cambios de domicilio.

## Límites de la entrega y puesta en marcha

- La integración logística real, el historial persistido, el snapshot y el CRUD de domicilios quedan especificados; no están implementados.
- Los pagos con tarjeta guardada todavía reconstruyen un PAN de prueba desde las últimas cuatro cifras y usan CVV 123 cuando se omite. Esto sigue siendo simulación; debe reemplazarse por autorización con token real antes de producción. No se altera ese flujo en esta auditoría.
- GetUserCardsAsync crea automáticamente una tarjeta simulada si no hay tarjetas. No representa emisión financiera real.
- Aplicar AssociateAddressesWithUsers junto con el despliegue del código: las nuevas consultas necesitan la columna UserId. No ejecutar actualización de base sin revisar ambiente y estrategia de backfill.
- Verificar la migración y el modelo EF sin cambios pendientes; ejecutar pruebas de dominio y aplicación. Los resultados concretos se informan al finalizar la tarea.

## Verificación ejecutada

- Wallet.Domain.Tests: 219 correctas, 0 fallidas.
- Wallet.Tests (configuración Audit): 200 correctas, 0 fallidas, 28 omitidas por las condiciones de integración SQL de la suite. No se considera validada la migración contra una base real.
- dotnet ef migrations has-pending-model-changes: sin diferencias entre modelo y snapshot.
- git diff --check: sin errores.
- Se usó configuración Audit para evitar sobrescribir DLL bloqueadas por Wallet.Api en ejecución. No se detuvo ni reinició esa instancia.
