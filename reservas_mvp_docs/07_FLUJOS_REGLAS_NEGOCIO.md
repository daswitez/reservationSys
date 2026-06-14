# Flujos y reglas de negocio

Este documento es la guía de comportamiento del sistema. Si hay duda al codificar, seguir estas reglas.

## Conceptos principales

### Tenant

Empresa que usa el sistema. Todo dato operativo pertenece a un tenant.

### Sucursal

Lugar físico o unidad operativa del tenant. Ejemplo: Peluquería Demo - Sucursal Centro.

### Servicio

Lo que el cliente reserva. Ejemplo: corte de cabello, manicura, consulta, masaje.

### Recurso reservable

Algo o alguien que se ocupa durante la reserva.

Ejemplos:

- Silla.
- Profesional.
- Sala.
- Equipo.

En una peluquería, si el negocio quiere controlar asientos, cada asiento puede ser un recurso. Si quiere controlar profesionales, cada profesional puede ser un recurso. Para MVP, ambos se modelan igual.

### Horario base

Rango semanal en el que un recurso podría atender.

### Slot

Unidad visual de disponibilidad. Recomendado: 15 minutos.

Aunque la UI muestre slots cada 15 minutos, la reserva real se guarda como rango `start_at` - `end_at` en PostgreSQL.

### Bloqueo

Rango de tiempo donde un recurso no puede recibir reservas.

Ejemplo:

- Silla en mantenimiento.
- Profesional ausente.
- Sala cerrada.
- Equipo fuera de uso.

## Estados de reserva

### CONFIRMED

Reserva activa y válida.

### CANCELLED

Reserva cancelada. Al estar cancelada, deja de ocupar el horario.

### ATTENDED

Cliente asistió y fue atendido.

### NO_SHOW

Cliente no asistió.

## Estados de recurso

### active

Puede recibir reservas.

### blocked

No está en uso a nivel general. Ejemplo: silla rota por varios días.

### inactive

Recurso fuera de operación o eliminado lógicamente.

## Estados de bloqueo

### ACTIVE

Bloqueo vigente. Impide reservas.

### CANCELLED

Bloqueo cancelado. Ya no impide reservas.

## Flujo 1 - Configuración inicial del negocio

1. `super_admin` crea tenant.
2. `super_admin` crea el usuario `tenant_admin` inicial.
3. `tenant_admin` crea sucursal.
4. `tenant_admin` crea servicios.
5. `tenant_admin` crea recursos.
6. `tenant_admin` asocia servicios con sucursales.
7. `tenant_admin` asocia servicios con recursos.
8. `tenant_admin` configura horarios base.

Resultado: el negocio ya puede recibir reservas.

## Flujo 2 - Cliente reserva una cita

1. Cliente crea una cuenta global o inicia sesión.
2. Cliente consulta todos los negocios activos de la plataforma.
3. Elige negocio/tenant.
4. Elige sucursal.
5. Elige servicio.
6. Elige fecha.
7. Frontend consulta disponibilidad.
8. Booking calcula recursos compatibles, horarios base, bloqueos y reservas activas.
9. Cliente elige horario.
10. Frontend llama `POST /reservations`.
11. Booking toma `client_user_id` del JWT y resuelve el tenant desde el negocio elegido.
12. Booking valida de nuevo dentro de la transacción.
13. PostgreSQL confirma o rechaza por conflicto.
14. Si todo está bien, reserva queda `CONFIRMED`.
15. Booking guarda historial y evento outbox.
16. Worker manda evento a Reporting.
17. Reporting actualiza Cassandra.

## Flujo 3 - Doble reserva

Caso:

Dos clientes intentan reservar la misma silla a las 10:00.

Regla:

- El frontend puede mostrar disponibilidad, pero no decide la verdad final.
- La verdad final está en PostgreSQL.
- La tabla de reservas tiene una restricción de exclusión por recurso y rango horario.
- Si hay conflicto, la API devuelve `409 Conflict`.

Mensaje recomendado:

> Ese horario acaba de ser reservado. Elegí otro horario.

## Flujo 4 - Bloquear recurso

1. Admin entra al panel.
2. Va a Bloqueos.
3. Elige sucursal.
4. Elige recurso.
5. Define fecha/hora inicio y fin.
6. Escribe motivo.
7. Booking crea bloqueo `ACTIVE`.
8. Ese rango deja de aparecer en disponibilidad.
9. Se registra evento para reportes si corresponde.

Ejemplo:

> Silla 2 bloqueada hoy de 13:00 a 15:00 por mantenimiento.

## Flujo 5 - Cancelar reserva

1. Cliente o admin elige cancelar reserva.
2. Booking busca la reserva por tenant y reserva ID.
3. Si está `CONFIRMED`, cambia a `CANCELLED`.
4. Se guarda historial.
5. Se emite evento para Reporting.
6. El horario queda libre porque la reserva cancelada ya no cuenta como ocupación activa.

No hay reglas de anticipación, multas, límite de horas ni aprobación.

Si se cancela, se cancela nomás.

## Flujo 6 - Marcar atendida o no-show

1. Recepcionista abre agenda.
2. Selecciona reserva.
3. Marca `ATTENDED` o `NO_SHOW`.
4. Se guarda historial.
5. Se emite evento para Reporting.

## Flujo 7 - Reportes

1. Booking genera eventos en outbox.
2. Worker procesa eventos pendientes.
3. Reporting recibe eventos por endpoint interno.
4. Reporting actualiza Cassandra.
5. Frontend consulta reportes desde Reporting Service.

Los reportes pueden tardar unos segundos en actualizarse. Esto es aceptable.

## Reglas duras

- No permitir acceso cross-tenant.
- No confiar en `tenant_id` enviado en body para operaciones privadas.
- Los clientes son globales y no reciben `tenant_id` en el JWT.
- El cliente puede consultar todos los tenants activos.
- En una reserva, el backend deriva y valida el tenant desde la sucursal y servicio elegidos.
- Un `tenant_admin` puede operar todas las sucursales de su tenant.
- Un `branch_admin` solo puede operar sucursales registradas en `user_branch_access`.
- Un usuario con rol `client` no puede acceder a operaciones administrativas.
- No permitir que un cliente reserve en una sucursal inactiva.
- No permitir que se reserve un servicio inactivo.
- No permitir que se reserve un recurso inactivo o bloqueado.
- No permitir reservas con `end_at <= start_at`.
- No permitir doble reserva sobre el mismo recurso y rango horario.
- No meter políticas de cancelación.
- No meter integraciones externas.
- No hacer que Reporting bloquee el flujo principal.
