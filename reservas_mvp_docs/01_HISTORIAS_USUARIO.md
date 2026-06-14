# Historias de usuario y criterios de aceptación

Estas historias están pensadas para un MVP funcional, sin integraciones externas, sin pagos, sin paquetes, sin políticas de cancelación y sin arquitectura enterprise.

## Roles

- `super_admin`: administra la plataforma completa y crea tenants.
- `tenant_admin`: administra una empresa completa.
- `branch_admin`: administra una sucursal específica.
- `receptionist`: gestiona agenda y reservas de una sucursal.
- `professional`: visualiza reservas asignadas si el recurso representa una persona.
- `client`: usuario global de la plataforma que explora negocios y reserva citas.

## Epic 1 - Identidad, tenants y acceso

### HU-001 - Registrar empresa/tenant

Como `super_admin` quiero registrar una empresa para que pueda operar dentro del sistema.

Criterios de aceptación:

- Se puede crear una empresa con nombre, slug, rubro principal, timezone y estado.
- El slug debe ser único.
- La empresa queda con estado `active` por defecto.
- Ningún dato de un tenant debe ser visible para otro tenant.

### HU-002 - Crear usuario administrador de tenant

Como `super_admin` quiero crear un usuario `tenant_admin` para que una empresa pueda administrar su configuración.

Criterios de aceptación:

- El usuario queda asociado a un `tenant_id`.
- El usuario recibe rol `tenant_admin`.
- El usuario puede iniciar sesión.
- El JWT incluye `user_id`, `tenant_id` y `roles`.

### HU-003 - Registrar cliente final

Como cliente quiero crear mi cuenta para reservar citas en un negocio.

Criterios de aceptación:

- El cliente puede registrarse con nombre, email, teléfono y contraseña.
- El registro no requiere seleccionar un tenant o negocio.
- El email debe ser único globalmente.
- El password se guarda hasheado.
- El cliente queda con rol `client`.
- El cliente no queda asociado a un `tenant_id`.
- Después de iniciar sesión puede consultar todos los negocios activos y elegir en cuál reservar.

### HU-004 - Login de usuario

Como usuario quiero iniciar sesión para acceder a las funciones permitidas por mi rol.

Criterios de aceptación:

- El sistema valida email y password.
- Si las credenciales son correctas, entrega un JWT.
- Si el usuario está `inactive` o `blocked`, no puede iniciar sesión.
- El frontend guarda el token de forma segura para consumir la API.

### HU-005 - Control de acceso por rol

Como sistema quiero validar permisos por rol para proteger las operaciones internas.

Criterios de aceptación:

- Un `client` no puede acceder al panel administrativo.
- Un `branch_admin` solo administra sucursales asignadas.
- Un `tenant_admin` administra todas las sucursales de su tenant.
- Todas las consultas administrativas privadas validan `tenant_id` desde el JWT.
- Las consultas del cliente usan `user_id` del JWT y pueden abarcar reservas de varios negocios.

### HU-005A - Administrar usuarios

Como administrador quiero consultar, editar y dar de baja usuarios para mantener
actualizadas las cuentas bajo mi responsabilidad.

Criterios de aceptación:

- `super_admin` puede consultar y administrar usuarios de cualquier tenant.
- `tenant_admin` solo puede consultar y administrar usuarios de su propio tenant.
- Los clientes globales no aparecen en las consultas de un `tenant_admin`.
- Se puede editar nombre, apellido, email y telefono.
- El email sigue siendo unico globalmente.
- Se puede cambiar el estado entre `active`, `inactive` y `blocked`.
- El borrado es logico: `DELETE` cambia el estado a `inactive` y conserva el historial.
- Un administrador no puede darse de baja a si mismo.
- Un usuario autenticado puede editar su propio perfil y cambiar su contrasena.
- Cambiar email, contrasena o estado invalida los JWT emitidos anteriormente.

## Epic 2 - Catálogo operativo

### HU-006 - Crear sucursal

Como `tenant_admin` quiero crear una sucursal para ofrecer reservas en una ubicación concreta.

Criterios de aceptación:

- Se registra nombre, dirección, teléfono, timezone y estado.
- La sucursal pertenece a un tenant.
- La sucursal activa aparece en el portal público.
- La sucursal inactiva no aparece para reservar.

### HU-007 - Editar sucursal

Como `tenant_admin` quiero editar los datos de una sucursal.

Criterios de aceptación:

- Se puede editar nombre, dirección, teléfono, timezone y estado.
- No se puede mover una sucursal a otro tenant.
- Si una sucursal queda inactiva, no aparece en disponibilidad pública.

### HU-008 - Crear servicio

Como administrador quiero crear servicios para que los clientes puedan reservarlos.

Criterios de aceptación:

- Se registra nombre, descripción, duración en minutos, precio referencial, modalidad y estado.
- La duración debe ser mayor a 0.
- El servicio pertenece a un tenant.
- Un servicio inactivo no aparece en el portal público.

### HU-009 - Asociar servicio a sucursal

Como administrador quiero indicar qué servicios ofrece cada sucursal.

Criterios de aceptación:

- Una sucursal puede ofrecer muchos servicios.
- Un servicio puede estar disponible en varias sucursales.
- Si la relación está inactiva, el servicio no se muestra para esa sucursal.

### HU-010 - Crear recurso reservable

Como administrador quiero crear recursos reservables para controlar qué se ocupa durante una cita.

Criterios de aceptación:

- Se puede crear recurso con nombre, tipo, capacidad y estado.
- El recurso pertenece a una sucursal.
- Ejemplos válidos: silla, sala, profesional, equipo.
- Un recurso `inactive` o `blocked` no recibe reservas nuevas.

### HU-011 - Asociar servicio con recurso

Como administrador quiero asociar servicios a recursos para saber qué recurso puede atender cada servicio.

Criterios de aceptación:

- Un servicio puede requerir uno o varios recursos.
- Un recurso puede atender uno o varios servicios.
- Para MVP, la reserva usa un recurso principal.
- El sistema solo ofrece recursos activos compatibles con el servicio elegido.

### HU-012 - Configurar horario base de recurso

Como administrador quiero definir en qué horarios trabaja un recurso.

Criterios de aceptación:

- Se configura por día de semana, hora inicio y hora fin.
- El horario pertenece a un recurso específico.
- El sistema usa estos horarios para generar disponibilidad.
- No se aceptan horarios con hora fin menor o igual a hora inicio.

## Epic 3 - Reservas y disponibilidad

### HU-013 - Consultar negocios/locales públicos

Como cliente quiero ver los negocios registrados para elegir dónde reservar.

Criterios de aceptación:

- Se listan tenants activos.
- Se puede entrar al detalle del negocio por slug.
- Solo se muestra información pública.

### HU-014 - Consultar disponibilidad

Como cliente quiero ver horarios disponibles para un servicio en una fecha.

Criterios de aceptación:

- El cliente elige tenant, sucursal, servicio y fecha.
- El sistema devuelve slots disponibles.
- Se consideran horarios base, recursos compatibles, bloqueos y reservas existentes.
- No se muestran slots pasados.
- No se muestran recursos bloqueados o inactivos.

### HU-015 - Crear reserva

Como cliente quiero reservar una hora disponible.

Criterios de aceptación:

- El cliente elige servicio, sucursal, recurso si aplica, fecha y hora.
- La API valida disponibilidad dentro de una transacción.
- Si el slot sigue libre, se crea la reserva con estado `CONFIRMED`.
- Si el slot ya fue tomado, responde `409 Conflict`.
- Se registra historial de creación.
- Se emite evento para Reporting.

### HU-016 - Prevenir doble reserva

Como sistema quiero impedir que dos personas reserven el mismo recurso en el mismo rango horario.

Criterios de aceptación:

- PostgreSQL debe tener una restricción que impida solapamiento de horarios por recurso.
- La validación no depende solo del frontend.
- Si dos requests llegan al mismo tiempo, solo una reserva se confirma.
- El error debe ser claro para el cliente.

### HU-017 - Cancelar reserva simple

Como cliente o administrador quiero cancelar una reserva sin reglas complejas.

Criterios de aceptación:

- Una reserva `CONFIRMED` puede cambiar a `CANCELLED`.
- Al cancelar, el horario queda disponible nuevamente.
- No hay límites de tiempo, penalidades ni reglas especiales.
- Se registra historial.
- Se emite evento para Reporting.

### HU-018 - Marcar reserva como atendida

Como recepcionista quiero marcar una reserva como atendida.

Criterios de aceptación:

- Solo usuarios internos pueden marcar `ATTENDED`.
- Se registra historial.
- La reserva ya no puede volver a `CONFIRMED`.
- Se emite evento para Reporting.

### HU-019 - Marcar no-show

Como recepcionista quiero marcar que el cliente no asistió.

Criterios de aceptación:

- Solo usuarios internos pueden marcar `NO_SHOW`.
- Se registra historial.
- Se emite evento para Reporting.

## Epic 4 - Bloqueos manuales

### HU-020 - Crear bloqueo de recurso

Como administrador de sucursal quiero bloquear un recurso cuando no esté disponible.

Criterios de aceptación:

- Se bloquea un recurso por rango de fecha/hora.
- El bloqueo tiene motivo.
- El bloqueo impide nuevas reservas dentro del rango.
- No se pueden crear bloqueos solapados innecesarios para el mismo recurso.
- Se emite evento para Reporting si afecta disponibilidad.

### HU-021 - Eliminar bloqueo

Como administrador quiero eliminar un bloqueo para volver a usar el recurso.

Criterios de aceptación:

- El bloqueo puede marcarse como `cancelled` o eliminarse lógicamente.
- Los slots afectados vuelven a estar disponibles si no hay reservas.
- Se registra quién realizó la acción.

## Epic 5 - Panel administrativo

### HU-022 - Ver agenda por sucursal

Como usuario interno quiero ver la agenda de una sucursal.

Criterios de aceptación:

- Se puede filtrar por fecha, recurso, servicio y estado.
- Se muestran reservas y bloqueos.
- Un `branch_admin` solo ve su sucursal asignada.

### HU-023 - Buscar reservas

Como usuario interno quiero buscar reservas para atender consultas.

Criterios de aceptación:

- Se puede buscar por cliente, fecha, estado, servicio, sucursal o recurso.
- La búsqueda respeta tenant y permisos.
- Se muestra historial básico de cambios.

## Epic 6 - Reportes

### HU-024 - Ver resumen diario

Como administrador quiero ver un resumen diario para entender la operación.

Criterios de aceptación:

- El reporte muestra reservas creadas, confirmadas, canceladas, atendidas y no-show.
- Se puede filtrar por tenant, sucursal y fecha.
- Los datos vienen desde Cassandra.
- Si Cassandra no tiene datos actualizados, el sistema debe mostrar que el reporte puede estar pendiente de sincronización.

### HU-025 - Ver ocupación por recurso

Como administrador quiero ver la ocupación por recurso para saber qué tan usado está cada asiento, sala o profesional.

Criterios de aceptación:

- Se muestra cantidad de reservas por recurso y día.
- Se muestra minutos ocupados estimados.
- Se puede filtrar por sucursal y rango de fechas.
- No se deben exponer datos personales del cliente en reportes agregados.

### HU-026 - Ver servicios más reservados

Como administrador quiero ver los servicios más reservados.

Criterios de aceptación:

- Se muestra ranking por servicio.
- Se puede filtrar por rango mensual o diario.
- Se muestra cantidad de reservas y cancelaciones.

### HU-027 - Ver horas pico

Como administrador quiero ver horas pico para entender cuándo se reserva más.

Criterios de aceptación:

- Se agrupa por hora del día.
- Se puede filtrar por sucursal.
- Se muestra cantidad de reservas creadas o atendidas.
