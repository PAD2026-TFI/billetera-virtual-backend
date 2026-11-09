# Revisión punta a punta de Padi y Mintlify

Fecha: 9 de noviembre de 2026. Alcance: recorrido de comercio documentado en https://padi-api.mintlify.site/ contra https://padi-billetera.netlify.app/ y el backend alojado https://padi.runasp.net/.

## Resultado: dos hallazgos que impiden aprobar el recorrido completo

### P1: el checkoutUrl de los enlaces por API devuelve 401

Se ejecutó el bloque JavaScript de `api/primer-cobro.mdx` contra la API publicada, con una clave generada desde Integraciones de una cuenta ficticia nueva. El enlace se creó y apareció en Cobros de la misma cuenta. Sin embargo, al abrir exactamente el checkoutUrl recibido, un navegador de comprador obtiene 401.

El controlador reconstruye la URL con el host de la API y `/checkout?token=...`. Ese directorio no tiene una página por defecto servida en esa ruta. `/checkout/index.html?token=...` existe, pero no es la URL que recibe la integración. El QR del mismo enlace apunta al frontend de Netlify y funciona.

Corrección incluida en este PR: devolver `created.CheckoutUrl` y `link.CheckoutUrl`, ya producidos por PaymentLinkService con la URL frontend configurada. No se modifica el contrato JSON ni la URL pública del QR. Una prueba de regresión comprueba creación y consulta con hosts frontend/API distintos.

Estado: corregido y probado localmente; no desplegado. Después de publicar el backend, repetir la creación y abrir el checkoutUrl exacto como comprador. No considerar esa comprobación alojada aprobada antes del despliegue.

### P2: el ejemplo PowerShell anida el listado de pagos

La ejecución literal del ejemplo publicado produjo `pagos.Count=1` y un primer elemento de tipo `System.Object[]`, pese a existir cuatro pagos. El `Where-Object` recibió el arreglo como un solo elemento y la consulta de paymentId terminó con varios identificadores en la URL, que no coincide con la ruta y responde 401.

Cambio propuesto: quitar `@(` y su `)` de la asignación de Invoke-RestMethod. La versión corregida se ejecutó en PowerShell 7.5.4: enumeró cuatro objetos individuales, filtró un pago y confirmó `Succeeded` por su id. Se verificó también la creación y recuperación del enlace mediante PowerShell antes de llegar a ese fallo.

La propuesta para Mintlify está en `docs/propuesta-mintlify-e2e.patch`. Incluye el arreglo de PowerShell y la actualización de los checkoutUrl ficticios del OpenAPI al dominio frontend, esta última coordinada con el despliegue de la corrección del backend. Se comprobó con `git apply --check` sobre la rama de documentación; no se aplicó ni publicó, porque esta tarea fue una revisión. El intento de dejar una sugerencia rastreada en Mintlify fue rechazado por faltar el permiso docs:write.

## Comprobaciones alojadas completadas

Se crearon dos cuentas ficticias nuevas. No se reutilizaron sus credenciales en documentación ni se cambiaron claves de las cuentas del usuario.

| Recorrido | Evidencia observada |
| --- | --- |
| Registro vendedor | Personal/Vendedor, datos, correo/código, contraseña, pantallas DNI/rostro e Ir a cobrar. Perfil merchant persistido. Las validaciones de correo/identidad siguen siendo simuladas. |
| Registro comprador | Mismo recorrido con modalidad Personal; perfil personal persistido. |
| Bonos de prueba | Ambas cuentas recibieron $50.000 iniciales. |
| Formulario de cobro | Importe cero y concepto vacío rechazados antes de crear el enlace. |
| Link y QR manual | Cobro de $1.500 creado desde Cobros. QR renderizado y decodificado: corresponde al token del enlace y apunta a Netlify. El enlace de Abrir checkout contiene el mismo token. |
| Cobro propio | La cuenta emisora ve Cobro propio y no puede pagar su cobro. |
| Pago manual | Comprador paga $1.500 con saldo; ve Pago aprobado; vendedor ve Cobrado y mensaje de éxito. Saldos $48.500 y $51.500. |
| SingleUse ya pagado | Al reabrirlo, muestra Este link ya fue pagado y no ofrece el botón de pago. |
| Integración desde app | Generación de clave, ocultamiento y metadatos de la misma cuenta. |
| Ejemplo JavaScript | Se ejecutó el bloque de la guía: creación válida y consulta por id, sin SDK supuesto ni contraseña del vendedor en las llamadas. |
| Cuatro endpoints comerciales | Crear enlace y consultar enlace/pagos funcionando con la clave de la cuenta. Errores de id inexistente confirmados como PAYMENT_INTENT_NOT_FOUND y PAYMENT_NOT_FOUND. |
| Pago del link API por QR | La alternativa QR lleva a Netlify; comprador paga otros $1.500. Consulta individual del pago devuelve Succeeded, paymentLinkId correcto, ARS e importe 1500. Saldos $47.000 y $53.000. No confundir este éxito con el fallo del checkoutUrl directo. |
| Entradas inválidas API | Importe cero, más de dos decimales, concepto vacío y campo desconocido devuelven 400 sin crear cobros. |
| MultiUse | Un enlace de $100 admite dos pagos distintos; ambos Succeeded y con IDs diferentes. El enlace permanece Active. |
| Vencimiento | Link con ttlMinutes=1 deja de ser pagable y la consulta pública devuelve Expired. |
| Aislamiento | Otra clave de comercio válida recibe 404 al consultar un enlace y pago ajenos. |
| Permisos de API Key | Sin clave o con clave inválida: 401. Intento de transferir mediante la clave de cobros: 403. |
| Cancelar reemplazo | La clave actual sigue funcionando. |
| Reemplazo y revocación | La clave reemplazada y la revocada devuelven 401. |
| Cambio a Personal | Revoca la clave; volver a Vendedor no la reactiva. No cambia billetera ni saldo. |
| Limpieza de claves QA | Las claves temporales de las cuentas nuevas usadas para la revisión quedaron revocadas. |

Se completaron cuatro pagos simulados: $1.500 + $1.500 + $100 + $100 = $3.200. Saldo final del comprador: $46.800. Saldo final del vendedor: $53.200. Se crearon además enlaces de pruebas de parámetros/PowerShell que pueden permanecer pendientes hasta su vencimiento; no se pagaron ni generan débitos por existir.

## Comprobaciones de la documentación publicada

- Las 11 páginas cargan con HTTP 200 y su título esperado; no se detectaron errores JavaScript ni páginas sin renderizar.
- La referencia muestra autorización, campos, restricciones y respuestas con ejemplos ficticios.
- Las cuatro pestañas de capturas de portada se seleccionan correctamente.
- La búsqueda de API Key ofrece seis resultados pertinentes.
- Las 11 páginas se probaron a 390 px de ancho: sin desbordamiento horizontal del documento.
- Portada revisada en escritorio, móvil y modo oscuro; el selector de tema requiere elegir Dark en su menú.
- Redirecciones de autenticacion, cobros y confirmacion-de-pagos llegan a las guías actuales.
- La revisión no modificó páginas, navegación, configuración ni credenciales publicadas.

## Límites de esta revisión

No se probaron cobros con tarjetas externas, pruebas de carga, concurrencia real de SQL ni todos los códigos de error de proveedores. El recorrido principal publicado usa saldo y el entorno académico no utiliza dinero real. No se crearon administradores ni se acreditaron fondos adicionales fuera del bono y los pagos entre las cuentas ficticias.

La suite local de la corrección de checkout aprobó 450 pruebas (221 dominio + 229 API/aplicación); 40 pruebas que necesitan SQL permanecieron omitidas. No hay migración ni cambios de esquema en este PR. La corrección y las propuestas de documentación no se publicaron automáticamente.
