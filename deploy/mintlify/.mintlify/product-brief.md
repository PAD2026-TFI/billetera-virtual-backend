# Producto

Padi permite a aplicaciones externas generar enlaces de cobro y consultar pagos.

## Audiencia

Desarrolladores que integran la pasarela desde el backend de su aplicación.

## Tareas

Registrar el comercio, proteger su API Key, crear enlaces y confirmar pagos.

## Alcance

Solo integración externa, según lo solicitado. Se asume checkout alojado para la primera integración. Motivación comercial no especificada.

## Recorrido verificado en la app

El 9 de noviembre de 2026 se creó una cuenta ficticia Vendedor en Netlify y un enlace de prueba por $1500, sin pagar. El vendedor usa sesión de usuario y las rutas /api/v1/me/merchant/payment-links. El alta B2B con X-Api-Key es independiente. Las pantallas de correo, DNI y rostro son simulaciones. La elección de vendedor se conserva localmente en el navegador según el frontend revisado; no se verificó su continuidad en otro dispositivo.
