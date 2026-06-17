# Revision tecnica - mejoras pendientes

Fecha de revision: 2026-06-17

Este documento lista mejoras detectadas despues de la implementacion de Booking,
Reporting y sus pruebas. La suite actual pasa, pero hay riesgos funcionales y de
seguridad que conviene cerrar antes de considerar el MVP estable.

## Resumen ejecutivo

- Tests principales: `dotnet test Reservas.sln` pasa, pero no incluye Reporting.
- Tests Reporting: pasan al ejecutarse directamente.
- Riesgo principal: autorizacion incompleta en algunos endpoints de Booking.
- Brecha funcional principal: el outbox se escribe, pero no hay worker que envie
  eventos a Reporting.
- Brecha operativa: Reporting no esta documentado en OpenAPI/Swagger.

## Alta prioridad

### 1. Proteger lectura individual de reservas

Endpoint afectado:

```txt
GET /reservations/{reservationId}
```

Archivo:

```txt
services/BookingService/Controllers/ReservationsController.cs
```

Problema:

El endpoint requiere cualquier usuario autenticado, busca solo por
`reservationId` y devuelve la reserva sin validar propietario, tenant o sucursal.
Un cliente autenticado podria leer una reserva ajena si conoce o descubre el UUID.

Impacto:

- Exposicion de datos de reservas de otros clientes.
- Riesgo de privacidad.
- Inconsistencia con el resto de reglas multi-tenant.

Correccion propuesta:

- Si el usuario tiene rol `client`, permitir solo cuando
  `reservation.ClientUserId == user_id`.
- Si tiene `tenant_admin`, exigir `tenant_id` y que coincida con
  `reservation.TenantId`.
- Si tiene `branch_admin`, exigir `tenant_id` y `branch_id`, ambos coincidentes.
- Si es `super_admin`, permitir.
- Agregar tests para cada rol:
  - cliente propietario: `200`.
  - cliente ajeno: `403`.
  - tenant_admin mismo tenant: `200`.
  - tenant_admin otro tenant: `403`.
  - branch_admin misma sucursal: `200`.
  - branch_admin otra sucursal: `403`.

### 2. Validar sucursal para branch_admin en cambios de reserva

Endpoints afectados:

```txt
PATCH /reservations/{reservationId}/cancel
PATCH /reservations/{reservationId}/attend
PATCH /reservations/{reservationId}/no-show
```

Archivo:

```txt
services/BookingService/Controllers/ReservationsController.cs
```

Problema:

`branch_admin` queda validado como usuario interno, pero no siempre se valida que
la reserva pertenezca a la sucursal del claim `branch_id`. En `attend` y
`no-show` solo se valida tenant; en `cancel` no hay bloque especifico para
`branch_admin`.

Impacto:

- Un `branch_admin` podria operar reservas de otra sucursal del mismo tenant.
- Puede marcar asistencias/no-show o cancelar reservas fuera de su alcance.

Correccion propuesta:

- Centralizar una funcion privada, por ejemplo:

```txt
CanAccessReservation(reservation, user)
```

- Reglas:
  - `super_admin`: permitido.
  - `tenant_admin`: mismo `tenant_id`.
  - `branch_admin`: mismo `tenant_id` y mismo `branch_id`.
  - `client`: solo reserva propia y solo acciones permitidas para clientes.
- Aplicar esa funcion en `GetById`, `Cancel`, `Attend`, `NoShow` y busquedas si
  corresponde.
- Agregar tests negativos para `branch_admin` de otra sucursal en cada endpoint.

### 3. Validar sucursal para branch_admin en bloqueos de recursos

Endpoints afectados:

```txt



POST /resource-blocks
PATCH /resource-blocks/{blockId}/cancel
GET /resource-blocks/{blockId}
```

Archivo:

```txt
services/BookingService/Controllers/ResourceBlocksController.cs
```

Problema:

El controlador valida tenant para usuarios no `super_admin`, pero no valida
`branch_id` cuando el usuario es `branch_admin`.

Impacto:

- Un `branch_admin` podria crear o cancelar bloqueos sobre recursos de otra
  sucursal dentro del mismo tenant.
- Afecta directamente disponibilidad publica y reservas futuras.

Correccion propuesta:

- Para `branch_admin`, exigir claim `branch_id`.
- En `POST /resource-blocks`, validar que `resource.BranchId == branch_id`.
- En cancelacion/lectura, validar que `block.BranchId == branch_id`.
- Agregar tests:
  - `branch_admin` bloquea recurso de su sucursal: `201`.
  - `branch_admin` intenta bloquear recurso de otra sucursal: `403`.
  - `branch_admin` intenta cancelar bloqueo de otra sucursal: `403`.

### 4. Implementar worker outbox de Booking hacia Reporting

Archivos afectados:

```txt
services/BookingService/appsettings.json
services/BookingService/Program.cs
services/BookingService/Data/BookingDbContext.cs
services/ReportingService/Controllers/ReportsController.cs
```

Problema:

Booking escribe eventos en `booking.reservation_event_outbox`, pero no existe un
worker registrado que lea eventos `PENDING` y los envie a Reporting. El
`appsettings` tiene `Outbox.WorkerEnabled=true`, pero no hay `HostedService`.

Impacto:

- Las reservas, cancelaciones, asistencias, no-show y bloqueos no actualizan
  reportes automaticamente.
- Los endpoints de Reporting pueden devolver `PENDING_SYNC` indefinidamente si no
  se cargan datos manualmente.

Correccion propuesta:

- Crear un `BackgroundService` en Booking:
  - Lee eventos `PENDING` por lotes.
  - Cambia estado a `PROCESSING`.
  - Envia payload a `POST /internal/report-events`.
  - Si Reporting responde OK, marca `PROCESSED`.
  - Si falla, incrementa `attempts`, guarda `last_error` y deja `FAILED` o
    `PENDING` segun politica de reintentos.
- Implementar endpoint real en Reporting:

```txt
POST /internal/report-events
```

- Debe aplicar eventos a las tablas Cassandra:
  - `report_daily_summary_by_tenant`
  - `report_daily_summary_by_branch`
  - `report_service_summary_by_month`
  - `report_resource_occupancy_by_day`
  - `report_peak_hours_by_branch_day`
  - `report_processed_events`
- Agregar idempotencia usando `event_id` en `report_processed_events`.
- Agregar tests de worker o tests de servicio que simulen eventos y verifiquen
  cambios en Cassandra.

## Media prioridad

### 5. Incluir ReportingService.Tests en la solucion

Archivo:

```txt
Reservas.sln
```

Problema:

`dotnet test Reservas.sln` no ejecuta `tests/ReportingService.Tests`. Los tests
de Reporting pasan si se ejecutan directamente, pero quedan fuera de la suite
principal.

Impacto:

- CI o validacion manual podria reportar todo verde sin cubrir Reporting.
- Riesgo de regresiones no detectadas en reportes.

Correccion propuesta:

Agregar el proyecto a la solucion:

```bash
DOTNET_CLI_HOME=/tmp/dotnet-home ./scripts/dotnet.sh sln Reservas.sln add tests/ReportingService.Tests/ReportingService.Tests.csproj
```

Luego validar:

```bash
DOTNET_CLI_HOME=/tmp/dotnet-home ./scripts/dotnet.sh test Reservas.sln
```

Resultado esperado despues de incluirlo:

```txt
CatalogService.Tests: 52
IdentityService.Tests: 47
BookingService.Tests: 70
ReportingService.Tests: 43
Total esperado: 212
```

### 6. Agregar OpenAPI/Swagger a Reporting

Archivos:

```txt
services/ReportingService/Program.cs
services/ReportingService/ReportingService.csproj
```

Problema:

Reporting expone endpoints HTTP, pero no registra Swagger/OpenAPI. Esto rompe la
convencion usada en Identity, Catalog y Booking.

Impacto:

- Los endpoints de reportes no aparecen en Swagger.
- Mas dificil probar desde navegador/Postman.
- Inconsistencia con la documentacion operativa del proyecto.

Correccion propuesta:

- Agregar paquete:

```xml
<PackageReference Include="Swashbuckle.AspNetCore" Version="10.2.1" />
```

- En `Program.cs`:
  - `AddEndpointsApiExplorer()`
  - `AddSwaggerGen(...)`
  - `UseSwagger()`
  - `UseSwaggerUI(...)`
- Agregar test OpenAPI que verifique:
  - `/reports/daily-summary`
  - `/reports/resources/occupancy`
  - `/reports/services/top`
  - `/reports/peak-hours`
  - `/internal/report-events` cuando se implemente.

### 7. Revisar GET /resource-blocks/{blockId}

Endpoint afectado:

```txt
GET /resource-blocks/{blockId}
```

Archivo:

```txt
services/BookingService/Controllers/ResourceBlocksController.cs
```

Problema:

El endpoint solo exige usuario autenticado y devuelve el bloqueo por ID sin
validar rol, tenant ni sucursal.

Impacto:

- Cualquier usuario autenticado podria leer bloqueos internos si conoce el UUID.
- Un cliente no deberia acceder a datos administrativos de bloqueos.

Correccion propuesta:

- Restringir a usuarios internos.
- `tenant_admin`: mismo tenant.
- `branch_admin`: mismo tenant y sucursal.
- `super_admin`: permitido.
- Tests para cliente, tenant ajeno y branch ajeno.

## Baja prioridad / calidad

### 8. Centralizar autorizacion de Booking

Problema:

La autorizacion esta repetida en varios controladores con bloques `if` manuales.
Esto ya genero diferencias entre reservas, agenda y bloqueos.

Mejora propuesta:

- Crear helpers internos o servicios pequeños:
  - `BookingAuthorizationService`
  - `CanAccessBranch`
  - `CanAccessReservation`
  - `CanAccessResource`
  - `CanAccessBlock`
- Mantener los controladores enfocados en el flujo de negocio.
- Reutilizar los helpers en tests para cubrir matriz de roles.

### 9. Normalizar respuestas de fecha/hora

Problema:

Algunas respuestas devuelven horarios en UTC y otras en timezone local de la
sucursal. Esto puede ser correcto segun endpoint, pero debe quedar explicito.

Mejora propuesta:

- Definir criterio por contrato:
  - Portal publico y reserva creada: timezone de sucursal.
  - Agenda administrativa: idealmente timezone de sucursal o incluir ambos.
  - Persistencia interna: UTC.
- Documentar el criterio en `05_API_CONTRATOS_DOTNET.md`.
- Agregar tests que verifiquen offsets esperados.

## Verificaciones actuales

Comandos ejecutados durante la revision:

```bash
DOTNET_CLI_HOME=/tmp/dotnet-home ./scripts/dotnet.sh test Reservas.sln
```

Resultado observado:

```txt
CatalogService.Tests: 52 passed
BookingService.Tests: 70 passed
IdentityService.Tests: 47 passed
Total en solucion: 169 passed
```

Reporting ejecutado directamente:

```bash
DOTNET_CLI_HOME=/tmp/dotnet-home ./scripts/dotnet.sh test tests/ReportingService.Tests/ReportingService.Tests.csproj --no-restore
```

Resultado observado:

```txt
ReportingService.Tests: 43 passed
```

## Orden recomendado de trabajo

1. Corregir autorizacion de Booking (`GetById`, reservas, bloqueos).
2. Agregar tests faltantes de seguridad por rol.
3. Incluir `ReportingService.Tests` en `Reservas.sln`.
4. Agregar Swagger a Reporting.
5. Implementar `POST /internal/report-events`.
6. Implementar worker outbox en Booking.
7. Validar flujo end-to-end: reserva creada -> outbox -> Reporting -> reportes.
