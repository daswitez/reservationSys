# Contratos de API .NET

Este documento define endpoints y DTOs base para que los agentes puedan construir las APIs sin inventar flujos raros.

## Convenciones generales

Respuesta exitosa:

```json
{
  "success": true,
  "data": {},
  "error": null
}
```

Respuesta de error:

```json
{
  "success": false,
  "data": null,
  "error": {
    "code": "SLOT_ALREADY_TAKEN",
    "message": "El horario seleccionado ya no está disponible."
  }
}
```

Headers:

```txt
Authorization: Bearer <jwt>
X-Correlation-Id: <uuid opcional>
Idempotency-Key: <uuid para POST /reservations>
```

Para operaciones administrativas privadas, el `tenant_id` se toma del JWT. Un
cliente global no tiene ese claim: el negocio se elige mediante `tenantSlug` o los
identificadores de sucursal/servicio, y el backend valida que todos pertenezcan al
mismo tenant antes de crear la reserva.

## Códigos de error comunes

- `UNAUTHORIZED`
- `FORBIDDEN`
- `VALIDATION_ERROR`
- `TENANT_NOT_FOUND`
- `BRANCH_NOT_FOUND`
- `SERVICE_NOT_FOUND`
- `RESOURCE_NOT_FOUND`
- `RESOURCE_NOT_AVAILABLE`
- `SLOT_ALREADY_TAKEN`
- `RESOURCE_BLOCKED`
- `RESERVATION_NOT_FOUND`
- `REPORT_NOT_READY`

## Identity & Tenancy Service

Base URL local del entorno Docker actual: `http://localhost:5201`

### POST /auth/login

Request:

```json
{
  "email": "cliente@test.com",
  "password": "Password123"
}
```

Response:

```json
{
  "success": true,
  "data": {
    "accessToken": "jwt",
    "expiresIn": 3600,
    "user": {
      "userId": "uuid",
      "tenantId": null,
      "email": "cliente@test.com",
      "fullName": "Cliente Test",
      "roles": ["client"]
    }
  },
  "error": null
}
```

El JWT está firmado y siempre contiene `user_id` y `roles`. Incluye `tenant_id`
solo para usuarios asociados a una empresa; los clientes globales no lo reciben. Los usuarios con
estado `inactive` o `blocked` reciben `401 Unauthorized`, igual que las credenciales
incorrectas. La respuesta incluye `Cache-Control: no-store` y `Pragma: no-cache`.
En cada request autenticado, Identity vuelve a comprobar que el usuario siga activo,
que el tenant coincida y que los roles del token sigan vigentes.

### POST /auth/register-client

Registra de forma independiente un cliente global de la plataforma. No requiere
seleccionar empresa y no es una operación del administrador.

Request:

```json
{
  "firstName": "Daniel",
  "lastName": "Mercado",
  "email": "daniel@test.com",
  "phone": "+59170000000",
  "password": "Password123"
}
```

Response:

```json
{
  "success": true,
  "data": {
    "userId": "uuid",
    "email": "daniel@test.com",
    "roles": ["client"],
    "status": "active",
    "createdAt": "2026-06-13T12:00:00Z"
  },
  "error": null
}
```

El email se normaliza a minúsculas y es único globalmente. La contraseña se
almacena hasheada con BCrypt. El usuario se crea con `tenant_id = NULL` y puede
consultar todos los negocios activos antes de elegir dónde reservar.

### GET /auth/me

Devuelve el usuario autenticado. Siempre valida `user_id`. Para usuarios internos
también valida que el `tenant_id` del JWT coincida con el persistido; para clientes
globales ambos son nulos.

### GET /auth/access/branches/{branchId}

Valida acceso administrativo a una sucursal.

- `client`: `403 Forbidden`.
- `tenant_admin`: acceso a cualquier sucursal que pertenezca a su `tenant_id`.
- `branch_admin`: acceso solo cuando existe una asignación en
  `identity.user_branch_access` para su usuario, tenant y sucursal.
- Una sucursal de otro tenant siempre devuelve `403 Forbidden`.

Este endpoint toma `user_id`, `tenant_id` y `roles` exclusivamente del JWT. No se
acepta un tenant enviado por body o query string para decidir autorización.

### POST /users/admin

Crea un administrador asociado a una empresa. Solo `super_admin`.

Request:

```json
{
  "tenantId": "uuid",
  "firstName": "Ana",
  "lastName": "Perez",
  "email": "admin@empresa.com",
  "phone": "+59170000000",
  "password": "Password123"
}
```

El usuario se crea con estado `active`, contraseña hasheada y rol `tenant_admin`.
Después puede autenticarse mediante `POST /auth/login`; el JWT resultante incluye
`user_id`, `tenant_id` y `roles`.

### GET /users

Lista usuarios para administracion. Permite filtrar por `tenantId`, `role`,
`status` y `search`. La paginacion usa `offset` desde 0 y un `limit` entre 1 y 200;
la respuesta incluye `total` antes de paginar.

- `super_admin`: puede consultar todos los usuarios y filtrar por tenant.
- `tenant_admin`: siempre queda limitado al `tenant_id` del JWT.
- `client`: `403 Forbidden`.

### GET /users/{userId}

Devuelve el detalle administrativo del usuario, incluidos roles, sucursales
asignadas, estado y fechas. Un `tenant_admin` no puede consultar otro tenant.

### PUT /users/{userId}

Edita nombre, apellido, email y telefono. No permite cambiar tenant, rol, estado
ni contrasena. El email continua siendo unico globalmente.

### PATCH /users/{userId}/status

Actualiza el estado a `active`, `inactive` o `blocked`. Al desactivar o bloquear
una cuenta, sus JWT anteriores dejan de ser validos inmediatamente. Un
administrador no puede desactivar su propia cuenta.

### DELETE /users/{userId}

Realiza una baja logica: conserva al usuario y sus relaciones, cambia el estado a
`inactive` e invalida sus JWT. No se eliminan registros que puedan estar asociados
a reservas o auditoria.

### PUT /users/me

Permite que cualquier usuario activo, incluido un cliente global, edite su propio
nombre, apellido, email y telefono. Si cambia el email, debe iniciar sesion otra
vez porque los JWT anteriores se invalidan.

### PATCH /users/me/password

Request:

```json
{
  "currentPassword": "Password123",
  "newPassword": "Password456"
}
```

Valida la contrasena actual, guarda la nueva con BCrypt e invalida todos los JWT
emitidos antes del cambio.

### POST /tenants

Crea empresa/tenant. Solo `super_admin`.

Request:

```json
{
  "name": "Peluquería Demo",
  "slug": "peluqueria-demo",
  "mainCategory": "Belleza",
  "timezone": "America/La_Paz"
}
```

### GET /tenants/public

Lista todos los tenants activos para el portal público. No requiere JWT y no se
filtra por usuario, porque una cuenta `client` puede explorar todos los negocios.

### GET /tenants/public/{slug}

Devuelve el detalle público de un tenant activo por slug. No requiere JWT. Solo
incluye información pública del negocio: identificador, nombre, slug, rubro,
timezone, estado y fecha de creación. Si el slug no existe o el tenant está
inactivo, devuelve `404 TENANT_NOT_FOUND`.

## Catalog Service

Base URL local del entorno Docker actual: `http://localhost:5202`

### GET /public/tenants/{tenantSlug}/branches

Lista sucursales publicas activas de un tenant activo. No requiere JWT.

- Devuelve `404 TENANT_NOT_FOUND` si el slug no existe o el tenant esta inactivo.
- Nunca incluye sucursales con estado `inactive`.
- La respuesta usa el mismo DTO de sucursal documentado en `GET /branches/{branchId}`.

### GET /public/tenants/{tenantSlug}/branches/{branchId}/services

Lista servicios activos disponibles en una sucursal.

### GET /branches

Lista las sucursales del tenant autenticado. Solo `tenant_admin`.

Query params opcionales:

```txt
status=active|inactive
```

El `tenant_id` se toma exclusivamente del JWT. La respuesta no incluye sucursales
de otros tenants.

### GET /branches/{branchId}

Devuelve el detalle de una sucursal del tenant autenticado. Solo `tenant_admin`.
Una sucursal inexistente o perteneciente a otro tenant devuelve
`404 BRANCH_NOT_FOUND` para no exponer informacion cross-tenant.

Response:

```json
{
  "success": true,
  "data": {
    "branchId": "uuid",
    "tenantId": "uuid",
    "name": "Sucursal Centro",
    "address": "Av. Principal #123",
    "phone": "+59170000000",
    "emailContact": "centro@test.com",
    "timezone": "America/La_Paz",
    "status": "active",
    "createdAt": "2026-06-13T12:00:00Z",
    "updatedAt": "2026-06-13T12:00:00Z"
  },
  "error": null
}
```

### POST /branches

Crea una sucursal para el tenant autenticado. Solo `tenant_admin`. El request no
acepta `tenantId`; se deriva del JWT. Devuelve `201 Created` y header `Location`.

Request:

```json
{
  "name": "Sucursal Centro",
  "address": "Av. Principal #123",
  "phone": "+59170000000",
  "emailContact": "centro@test.com",
  "timezone": "America/La_Paz",
  "status": "active"
}
```

`name`, `address`, `phone` y `timezone` son obligatorios. `emailContact` es
opcional. `status` admite `active` o `inactive` y usa `active` por defecto. La
zona horaria debe existir en la base IANA disponible para .NET.

### PUT /branches/{branchId}

Actualiza todos los datos editables de una sucursal del tenant autenticado. Solo
`tenant_admin`.

Request:

```json
{
  "name": "Sucursal Norte",
  "address": "Av. Norte #456",
  "phone": "+59171111111",
  "emailContact": "norte@test.com",
  "timezone": "America/La_Paz",
  "status": "active"
}
```

No permite cambiar `branchId` ni `tenantId`. Devuelve `404 BRANCH_NOT_FOUND` si
la sucursal no pertenece al tenant del JWT.

### PATCH /branches/{branchId}/status

Activa o desactiva una sucursal. Solo `tenant_admin`.

Request:

```json
{
  "status": "inactive"
}
```

Una sucursal inactiva deja de aparecer inmediatamente en
`GET /public/tenants/{tenantSlug}/branches` y no debe aceptarse para nuevas
reservas.

### DELETE /branches/{branchId}

Realiza una baja logica. Solo `tenant_admin`. Conserva la sucursal y sus
relaciones, cambia su estado a `inactive` y devuelve el DTO actualizado. No se
realiza borrado fisico porque la sucursal puede tener recursos, horarios,
reservas o datos de auditoria asociados.

### GET /public/tenants/{tenantSlug}/services

Lista los servicios activos de un tenant activo para el portal publico. No
requiere JWT.

- Devuelve `404 TENANT_NOT_FOUND` si el slug no existe o el tenant esta inactivo.
- Nunca incluye servicios con estado `inactive`.
- Este endpoint representa el catalogo general del tenant. La disponibilidad de
  un servicio en una sucursal concreta depende de HU-009 y se consulta mediante
  `GET /public/tenants/{tenantSlug}/branches/{branchId}/services`.

### GET /services

Lista los servicios del tenant autenticado. Solo `tenant_admin`.

Query params opcionales:

```txt
status=active|inactive
```

El `tenant_id` se toma exclusivamente del JWT.

### GET /services/{serviceId}

Devuelve el detalle de un servicio del tenant autenticado. Solo `tenant_admin`.
Un servicio inexistente o de otro tenant devuelve `404 SERVICE_NOT_FOUND`.

Response:

```json
{
  "success": true,
  "data": {
    "serviceId": "uuid",
    "tenantId": "uuid",
    "name": "Corte de cabello",
    "description": "Corte clasico o moderno",
    "durationMinutes": 30,
    "referencePrice": 50.00,
    "modality": "presencial",
    "status": "active",
    "createdAt": "2026-06-13T12:00:00Z",
    "updatedAt": "2026-06-13T12:00:00Z"
  },
  "error": null
}
```

### POST /services

Crea un servicio para el tenant autenticado. Solo `tenant_admin`. El request no
acepta `tenantId`; se deriva del JWT. Devuelve `201 Created` y header `Location`.

Request:

```json
{
  "name": "Corte de cabello",
  "description": "Corte clásico o moderno",
  "durationMinutes": 30,
  "referencePrice": 50,
  "modality": "presencial",
  "status": "active"
}
```

Reglas:

- `name`, `description` y `modality` son obligatorios.
- `durationMinutes` debe ser mayor a `0`.
- `referencePrice` es opcional y, cuando se envia, debe ser mayor o igual a `0`.
- `modality` admite hasta 30 caracteres y se normaliza a minusculas.
- `status` admite `active` o `inactive` y usa `active` por defecto.

### PUT /services/{serviceId}

Actualiza todos los datos editables de un servicio del tenant autenticado. Solo
`tenant_admin`.

Request:

```json
{
  "name": "Corte premium",
  "description": "Corte y asesoramiento",
  "durationMinutes": 45,
  "referencePrice": 75.50,
  "modality": "presencial",
  "status": "active"
}
```

No permite cambiar `serviceId` ni `tenantId`. Mantiene las mismas validaciones
del alta.

### PATCH /services/{serviceId}/status

Activa o desactiva un servicio. Solo `tenant_admin`.

Request:

```json
{
  "status": "inactive"
}
```

Un servicio inactivo deja de aparecer inmediatamente en el portal publico y no
debe aceptarse para nuevas reservas.

### DELETE /services/{serviceId}

Realiza una baja logica. Solo `tenant_admin`. Conserva el servicio y sus
relaciones, cambia su estado a `inactive` y devuelve el DTO actualizado. No se
realiza borrado fisico porque puede estar asociado a sucursales, recursos,
reservas o auditoria.

### POST /branches/{branchId}/services/{serviceId}

Habilita un servicio en una sucursal. Solo `tenant_admin`. Sin body: los IDs van
en la URL. Si la relación ya existe pero está inactiva, la reactiva. Devuelve
`201 Created` al crear y `200 OK` al reactivar.

### DELETE /branches/{branchId}/services/{serviceId}

Deshabilita un servicio en una sucursal. Solo `tenant_admin`. Baja lógica: la
vinculación pasa a `inactive` y el servicio deja de aparecer en
`GET /public/tenants/{tenantSlug}/branches/{branchId}/services`. El servicio y la
sucursal no se modifican. Devuelve `204 No Content`. Si la relación no existe o
pertenece a otro tenant, devuelve `404 BRANCH_NOT_FOUND`.

### GET /resources

Lista los recursos reservables del tenant autenticado. Solo `tenant_admin`.

Query params opcionales:

```txt
branchId=uuid
status=active|blocked|inactive
```

`active` es el unico estado que debe participar en nuevas reservas. `blocked` e
`inactive` se conservan para administracion y auditoria, pero Booking debe
ignorarlos al asignar disponibilidad.

### GET /resources/{resourceId}

Devuelve el detalle de un recurso reservable del tenant autenticado. Solo
`tenant_admin`. Un recurso inexistente o perteneciente a otro tenant devuelve
`404 RESOURCE_NOT_FOUND`.

Response:

```json
{
  "success": true,
  "data": {
    "resourceId": "uuid",
    "tenantId": "uuid",
    "branchId": "uuid",
    "name": "Silla 1",
    "resourceType": "silla",
    "description": "Silla principal",
    "capacity": 1,
    "status": "active",
    "createdAt": "2026-06-16T12:00:00Z",
    "updatedAt": "2026-06-16T12:00:00Z"
  },
  "error": null
}
```

### POST /resources

Crea recurso reservable en una sucursal del tenant autenticado. Solo
`tenant_admin`. El `tenantId` se deriva del JWT y el `branchId` debe pertenecer
al mismo tenant.

Request:

```json
{
  "branchId": "uuid",
  "name": "Silla 1",
  "resourceType": "silla",
  "description": "Silla principal",
  "capacity": 1,
  "status": "active"
}
```

Reglas:

- `name`, `resourceType`, `branchId` y `capacity` son obligatorios.
- `capacity` debe ser mayor a `0`.
- `resourceType` se normaliza a minusculas. Ejemplos validos: `silla`, `sala`,
  `profesional`, `equipo`.
- `status` admite `active`, `blocked` o `inactive`; usa `active` por defecto.
- Si el `branchId` no existe en el tenant autenticado, devuelve
  `404 BRANCH_NOT_FOUND`.

### PUT /resources/{resourceId}

Actualiza todos los datos editables de un recurso reservable. Solo
`tenant_admin`. Permite mover el recurso a otra sucursal del mismo tenant, pero
no a otro tenant.

Request:

```json
{
  "branchId": "uuid",
  "name": "Sala Norte",
  "resourceType": "sala",
  "description": "Sala privada",
  "capacity": 4,
  "status": "blocked"
}
```

### PATCH /resources/{resourceId}/status

Activa, bloquea o desactiva un recurso reservable. Solo `tenant_admin`.

Request:

```json
{
  "status": "blocked"
}
```

Un recurso `blocked` o `inactive` no debe recibir nuevas reservas.

### DELETE /resources/{resourceId}

Realiza una baja logica. Solo `tenant_admin`. Conserva el recurso y sus
relaciones, cambia su estado a `inactive` y devuelve el DTO actualizado.

### GET /services/{serviceId}/resources

Lista recursos asociados a un servicio del tenant autenticado. Solo
`tenant_admin`.

Query params opcionales:

```txt
status=active|inactive
```

### GET /services/{serviceId}/compatible-resources

Lista recursos compatibles y disponibles para nuevas reservas del servicio
elegido. Solo devuelve asociaciones `active`, servicios `active` y recursos
`active`; no incluye recursos `blocked` ni `inactive`.

Query params opcionales:

```txt
branchId=uuid
```

Esta es la consulta que Booking debe usar para elegir el recurso principal del
MVP antes de calcular horarios y reservas existentes.

### POST /services/{serviceId}/resources/{resourceId}

Asocia un servicio con un recurso compatible. Solo `tenant_admin`. Si la relacion
ya existe, actualiza `required`, `priority` y `status`; devuelve `201 Created`
cuando crea y `200 OK` cuando reactiva o actualiza.

Request:

```json
{
  "required": true,
  "priority": 1,
  "status": "active"
}
```

Reglas:

- Servicio y recurso deben pertenecer al tenant autenticado.
- `priority` debe ser mayor a `0`.
- `status` admite `active` o `inactive`, y usa `active` por defecto.
- Para MVP, el recurso con menor `priority` puede tratarse como recurso principal
  si hay mas de uno compatible.

### PUT /services/{serviceId}/resources/{resourceId}

Actualiza los atributos de compatibilidad entre servicio y recurso. Solo
`tenant_admin`.

Request:

```json
{
  "required": true,
  "priority": 2,
  "status": "active"
}
```

### PATCH /services/{serviceId}/resources/{resourceId}/status

Activa o desactiva la compatibilidad entre servicio y recurso. Solo
`tenant_admin`.

Request:

```json
{
  "status": "inactive"
}
```

### DELETE /services/{serviceId}/resources/{resourceId}

Deshabilita la compatibilidad entre servicio y recurso. Solo `tenant_admin`.
Baja logica: cambia la relacion a `inactive` y devuelve `204 No Content`.

### GET /resource-schedules

Lista horarios base de recursos del tenant autenticado. Solo `tenant_admin`.

Query params opcionales:

```txt
branchId=uuid
resourceId=uuid
dayOfWeek=1
status=active|inactive
```

Booking debe usar horarios `active` del recurso principal compatible para generar
disponibilidad antes de descontar bloqueos y reservas existentes.

### GET /resource-schedules/{scheduleId}

Devuelve el detalle de un horario base del tenant autenticado. Solo
`tenant_admin`. Un horario inexistente o perteneciente a otro tenant devuelve
`404 RESOURCE_SCHEDULE_NOT_FOUND`.

Response:

```json
{
  "success": true,
  "data": {
    "scheduleId": "uuid",
    "tenantId": "uuid",
    "branchId": "uuid",
    "resourceId": "uuid",
    "dayOfWeek": 1,
    "startTime": "09:00",
    "endTime": "18:00",
    "validFrom": "2026-06-16",
    "validTo": null,
    "status": "active",
    "createdAt": "2026-06-16T12:00:00Z"
  },
  "error": null
}
```

### POST /resource-schedules

Crea horario base de recurso. Solo `tenant_admin`. `branchId` y `resourceId`
deben pertenecer al tenant autenticado, y el recurso debe pertenecer a esa
sucursal.

Request:

```json
{
  "branchId": "uuid",
  "resourceId": "uuid",
  "dayOfWeek": 1,
  "startTime": "09:00",
  "endTime": "18:00",
  "validFrom": "2026-01-01",
  "validTo": null
}
```

Reglas:

- `dayOfWeek` debe estar entre `1` y `7`.
- `startTime` y `endTime` usan formato `HH:mm`.
- `endTime` debe ser mayor a `startTime`.
- `validFrom` y `validTo` son opcionales y usan formato `yyyy-MM-dd`.
- Si ambas fechas vienen informadas, `validTo >= validFrom`.
- `status` admite `active` o `inactive` y usa `active` por defecto.

### PUT /resource-schedules/{scheduleId}

Actualiza todos los datos editables de un horario base. Solo `tenant_admin`.

Request:

```json
{
  "branchId": "uuid",
  "resourceId": "uuid",
  "dayOfWeek": 2,
  "startTime": "10:00",
  "endTime": "16:30",
  "validFrom": "2026-06-16",
  "validTo": "2026-12-31",
  "status": "active"
}
```

### PATCH /resource-schedules/{scheduleId}/status

Activa o desactiva un horario base. Solo `tenant_admin`.

Request:

```json
{
  "status": "inactive"
}
```

### DELETE /resource-schedules/{scheduleId}

Realiza baja logica del horario base. Solo `tenant_admin`. Cambia el estado a
`inactive` y devuelve el DTO actualizado.

## Booking & Availability Service

Base URL local del entorno Docker actual: `http://localhost:5203`

### GET /availability

Consulta slots disponibles. No requiere JWT.

Query params:

```txt
tenantSlug=peluqueria-demo
branchId=uuid
serviceId=uuid
date=2026-06-17
```

Response:

```json
{
  "success": true,
  "data": {
    "branchId": "uuid",
    "serviceId": "uuid",
    "date": "2026-06-17",
    "slotMinutes": 15,
    "availableSlots": [
      {
        "resourceId": "uuid",
        "resourceName": "Silla 1",
        "startAt": "2026-06-17T09:00:00-04:00",
        "endAt": "2026-06-17T09:30:00-04:00"
      }
    ]
  },
  "error": null
}
```

Reglas implementadas:

- Valida que tenant, sucursal y servicio esten `active` y pertenezcan entre si.
- Valida que el servicio este activo en la sucursal mediante `catalog.branch_services`.
- Genera slots cada `15` minutos usando `duration_minutes` del servicio.
- Usa horarios base `active` de `catalog.resource_schedules` por dia de semana.
- Solo usa recursos compatibles `active`; ignora recursos `inactive` y `blocked`.
- Excluye slots que se solapan con reservas `CONFIRMED`.
- Excluye slots que se solapan con bloqueos `ACTIVE`.
- No devuelve slots pasados segun el timezone de la sucursal.

Errores:

- `400 VALIDATION_ERROR` si falta un query param o `date` no usa `yyyy-MM-dd`.
- `404 TENANT_NOT_FOUND` si el tenant no existe o no esta activo.
- `404 BRANCH_NOT_FOUND` si la sucursal no existe, no pertenece al tenant o no esta activa.
- `404 SERVICE_NOT_FOUND` si el servicio no existe, no pertenece al tenant, no esta activo o no esta activo en la sucursal.

### POST /reservations

Crea reserva confirmada. Requiere JWT con rol `client`.

Para un usuario `client`, `client_user_id` se toma de `user_id` en el JWT. El
tenant no viene del JWT del cliente: Booking lo deriva de la sucursal y servicio
seleccionados y valida que pertenezcan al mismo negocio.

Request:

```json
{
  "branchId": "uuid",
  "serviceId": "uuid",
  "resourceId": "uuid",
  "startAt": "2026-06-12T09:00:00-04:00",
  "notes": "Quiero corte bajo"
}
```

`resourceId` puede omitirse. En ese caso Booking elige el primer recurso activo,
compatible y libre segun prioridad de `catalog.service_resources`.

Response:

```json
{
  "success": true,
  "data": {
    "reservationId": "uuid",
    "tenantId": "uuid",
    "branchId": "uuid",
    "serviceId": "uuid",
    "resourceId": "uuid",
    "clientUserId": "uuid",
    "status": "CONFIRMED",
    "startAt": "2026-06-12T09:00:00-04:00",
    "endAt": "2026-06-12T09:30:00-04:00",
    "notes": "Quiero corte bajo",
    "createdAt": "2026-06-16T12:00:00Z"
  },
  "error": null
}
```

Reglas implementadas:

- Valida tenant, sucursal, servicio y recurso activo dentro de una transaccion.
- Valida que el servicio este activo para la sucursal.
- Valida horario base activo del recurso para la fecha y hora local de la sucursal.
- Valida bloqueos `ACTIVE` y reservas `CONFIRMED` solapadas.
- Inserta reserva con estado `CONFIRMED`.
- Inserta historial `CREATED`.
- Inserta evento outbox `ReservationCreated` con estado `PENDING` para Reporting.
- PostgreSQL mantiene la defensa final con constraint de exclusion; si dos clientes
  toman el mismo slot, la API responde `409 SLOT_ALREADY_TAKEN`.

Errores posibles:

- `409 SLOT_ALREADY_TAKEN`
- `409 RESOURCE_BLOCKED`
- `409 RESOURCE_NOT_AVAILABLE`
- `404 BRANCH_NOT_FOUND`
- `404 SERVICE_NOT_FOUND`
- `404 RESOURCE_NOT_FOUND`
- `400 VALIDATION_ERROR`

### GET /reservations/{reservationId}

Devuelve el detalle de una reserva. Cualquier usuario autenticado puede consultar
por ID; la autorización por tenant o propietario no se aplica en esta lectura.

Response:

```json
{
  "success": true,
  "data": {
    "reservationId": "uuid",
    "tenantId": "uuid",
    "branchId": "uuid",
    "serviceId": "uuid",
    "resourceId": "uuid",
    "clientUserId": "uuid",
    "status": "CONFIRMED",
    "startAt": "2026-06-17T09:00:00-04:00",
    "endAt": "2026-06-17T09:30:00-04:00",
    "notes": "Quiero corte bajo",
    "createdAt": "2026-06-17T12:00:00Z"
  },
  "error": null
}
```

Errores:

- `404 RESERVATION_NOT_FOUND` si el ID no existe.

### PATCH /reservations/{reservationId}/cancel

Cancela una reserva confirmada. El cliente solo puede cancelar su propia reserva;
usuarios internos pueden cancelar cualquier reserva de su tenant.

Request (`reason` opcional):

```json
{
  "reason": "Cancelado por el cliente"
}
```

Response: mismo schema que `POST /reservations` con `status: "CANCELLED"`.

Errores:

- `404 RESERVATION_NOT_FOUND`
- `409 INVALID_STATUS_TRANSITION` si la reserva ya está cancelada, atendida o no-show.
- `403` si el cliente intenta cancelar una reserva ajena.

### PATCH /reservations/{reservationId}/attend

Marca la reserva como `ATTENDED`. Solo usuarios internos (`tenant_admin`, `branch_admin`).

Response: mismo schema que `POST /reservations` con `status: "ATTENDED"`.

Errores:

- `404 RESERVATION_NOT_FOUND`
- `409 INVALID_STATUS_TRANSITION` si la reserva no está en estado `CONFIRMED`.

### PATCH /reservations/{reservationId}/no-show

Marca la reserva como `NO_SHOW`. Solo usuarios internos.

Response: mismo schema que `POST /reservations` con `status: "NO_SHOW"`.

Errores:

- `404 RESERVATION_NOT_FOUND`
- `409 INVALID_STATUS_TRANSITION` si la reserva no está en estado `CONFIRMED`.

### GET /admin/reservations

Busca reservas con filtros. Solo usuarios internos (`client` recibe `403`).
`tenant_admin` ve todas las reservas de su tenant; `branch_admin` está restringido
a su sucursal del claim. `super_admin` ve todas. Máximo 200 resultados, ordenados
por `startAt` descendente. Cada reserva incluye su historial de estado.

Query params (todos opcionales):

```txt
clientUserId=uuid
branchId=uuid
serviceId=uuid
resourceId=uuid
status=CONFIRMED|CANCELLED|ATTENDED|NO_SHOW
dateFrom=2026-06-01
dateTo=2026-06-30
```

Response:

```json
{
  "success": true,
  "data": [
    {
      "reservationId": "uuid",
      "tenantId": "uuid",
      "branchId": "uuid",
      "serviceId": "uuid",
      "resourceId": "uuid",
      "clientUserId": "uuid",
      "status": "CONFIRMED",
      "startAt": "2026-06-17T09:00:00-04:00",
      "endAt": "2026-06-17T09:30:00-04:00",
      "notes": "Quiero corte bajo",
      "createdAt": "2026-06-17T12:00:00Z",
      "history": [
        {
          "action": "CREATED",
          "previousStatus": null,
          "newStatus": "CONFIRMED",
          "reason": null,
          "userId": "uuid",
          "createdAt": "2026-06-17T12:00:00Z"
        }
      ]
    }
  ],
  "error": null
}
```

### GET /admin/agenda

Devuelve reservas activas y bloqueos activos de una sucursal para un día completo,
respetando el timezone de la sucursal. `branch_admin` solo puede consultar su propia
sucursal (validado contra el claim `branch_id`).

Query params:

```txt
branchId=uuid        (requerido)
date=2026-06-17      (requerido, formato yyyy-MM-dd)
resourceId=uuid      (opcional)
serviceId=uuid       (opcional)
status=CONFIRMED     (opcional, filtra solo reservas)
```

Response:

```json
{
  "success": true,
  "data": {
    "date": "2026-06-17",
    "branchId": "uuid",
    "reservations": [
      {
        "reservationId": "uuid",
        "resourceId": "uuid",
        "serviceId": "uuid",
        "clientUserId": "uuid",
        "status": "CONFIRMED",
        "startAt": "2026-06-17T09:00:00-04:00",
        "endAt": "2026-06-17T09:30:00-04:00",
        "notes": null
      }
    ],
    "blocks": [
      {
        "blockId": "uuid",
        "resourceId": "uuid",
        "reason": "Mantenimiento",
        "blockType": "manual",
        "status": "ACTIVE",
        "startAt": "2026-06-17T13:00:00-04:00",
        "endAt": "2026-06-17T15:00:00-04:00"
      }
    ]
  },
  "error": null
}
```

Errores:

- `400 VALIDATION_ERROR` si falta `branchId` o `date` tiene formato incorrecto.
- `404 BRANCH_NOT_FOUND` si la sucursal no existe o no está activa.

### POST /resource-blocks

Crea un bloqueo manual. Solo usuarios internos. El bloqueo nace con estado `ACTIVE`
y excluye esa franja del motor de disponibilidad desde el momento en que se crea.

Request:

```json
{
  "branchId": "uuid",
  "resourceId": "uuid",
  "startAt": "2026-06-17T13:00:00-04:00",
  "endAt": "2026-06-17T15:00:00-04:00",
  "reason": "Silla en mantenimiento",
  "blockType": "manual"
}
```

Response:

```json
{
  "success": true,
  "data": {
    "blockId": "uuid",
    "tenantId": "uuid",
    "branchId": "uuid",
    "resourceId": "uuid",
    "startAt": "2026-06-17T13:00:00-04:00",
    "endAt": "2026-06-17T15:00:00-04:00",
    "reason": "Silla en mantenimiento",
    "blockType": "manual",
    "status": "ACTIVE",
    "createdByUserId": "uuid",
    "createdAt": "2026-06-17T12:00:00Z"
  },
  "error": null
}
```

Errores:

- `404 RESOURCE_NOT_FOUND` si el recurso no existe o no pertenece a la sucursal.
- `409 RESOURCE_BLOCKED` si ya existe un bloqueo activo en ese rango.

### GET /resource-blocks/{blockId}

Devuelve el detalle de un bloqueo. Solo usuarios internos. Response: mismo schema
que `POST /resource-blocks`.

Errores:

- `404 BLOCK_NOT_FOUND` si el ID no existe.

### PATCH /resource-blocks/{blockId}/cancel

Cancela un bloqueo activo. Solo usuarios internos.

Response: mismo schema que `POST /resource-blocks` con `status: "CANCELLED"`.

Errores:

- `404 BLOCK_NOT_FOUND`
- `409 INVALID_STATUS_TRANSITION` si el bloqueo ya está cancelado.

## Reporting Service

Base URL local del entorno Docker actual: `http://localhost:5204`

Reporting lee exclusivamente desde Cassandra (keyspace `reservas_reports`). Los datos tienen
un retraso de segundos respecto a PostgreSQL (eventual consistency). Todos los endpoints
requieren JWT de usuario interno; `client` recibe `403`. `super_admin` debe pasar `tenantId`
como query param en todos los endpoints.

Respuestas que pueden incluir `DataStatus`:

- `"OK"` — Cassandra tiene datos para el período consultado.
- `"PENDING_SYNC"` — Cassandra aún no tiene datos; el reporte puede estar desactualizado.

### GET /reports/daily-summary

Resumen de reservas del día para el tenant (o sucursal) indicados.

Query params:

```txt
date=2026-06-17          (requerido, formato yyyy-MM-dd)
branchId=uuid            (opcional; filtra por sucursal)
tenantId=uuid            (solo super_admin, requerido para ese rol)
```

Reglas de acceso:

- `client` → `403`
- `branch_admin` → si pasa `branchId`, debe coincidir con el claim `branch_id`; si no pasa `branchId`, se usa el claim automáticamente.
- `tenant_admin` → `tenantId` se toma del JWT.
- `super_admin` → debe pasar `tenantId` como query param.

Response:

```json
{
  "success": true,
  "data": {
    "tenantId": "uuid",
    "branchId": null,
    "branchName": null,
    "date": "2026-06-17",
    "totalCreated": 20,
    "totalConfirmed": 14,
    "totalCancelled": 3,
    "totalAttended": 2,
    "totalNoShow": 1,
    "totalReservedMinutes": 600,
    "updatedAt": "2026-06-17T20:00:00Z",
    "dataStatus": "OK"
  },
  "error": null
}
```

Cuando `branchId` está presente en la consulta, `branchId` y `branchName` se pueblan en
la respuesta. Si Cassandra no tiene datos, `dataStatus` es `"PENDING_SYNC"` y los conteos
son `0`.

Errores:

- `400 VALIDATION_ERROR` si falta `date` o el formato es incorrecto.
- `400 VALIDATION_ERROR` si `super_admin` no incluye `tenantId`.

### GET /reports/resources/occupancy

Ocupación por recurso para una sucursal y período. No expone datos personales del cliente
(la tabla Cassandra solo almacena conteos agregados).

Query params:

```txt
branchId=uuid            (requerido)
date=2026-06-17          (día único; alternativa a dateFrom/dateTo)
dateFrom=2026-06-01      (inicio de rango)
dateTo=2026-06-17        (fin de rango; máx 31 días desde dateFrom)
tenantId=uuid            (solo super_admin)
```

Response:

```json
{
  "success": true,
  "data": [
    {
      "resourceId": "uuid",
      "resourceName": "Silla 1",
      "resourceType": "seat",
      "date": "2026-06-17",
      "totalReservations": 8,
      "totalAttended": 6,
      "totalCancelled": 1,
      "totalNoShow": 1,
      "reservedMinutes": 240,
      "blockedMinutes": 120,
      "updatedAt": "2026-06-17T20:00:00Z"
    }
  ],
  "error": null
}
```

Errores:

- `400 VALIDATION_ERROR` si falta `branchId` o no se indica ningún parámetro de fecha.
- `400 VALIDATION_ERROR` si `dateTo` < `dateFrom` o el rango excede 31 días.
- `403` si `branch_admin` intenta acceder a una sucursal que no es la propia.

### GET /reports/services/top

Ranking de servicios por volumen de reservas. Agrega datos de múltiples meses en memoria
antes de calcular el ranking.

Query params:

```txt
month=2026-06            (mes único; alternativa a monthFrom/monthTo)
monthFrom=2026-01        (inicio de rango, formato yyyy-MM)
monthTo=2026-06          (fin de rango; máx 24 meses desde monthFrom)
tenantId=uuid            (solo super_admin)
```

Response:

```json
{
  "success": true,
  "data": {
    "periodFrom": "2026-01",
    "periodTo": "2026-06",
    "services": [
      {
        "rank": 1,
        "serviceId": "uuid",
        "serviceName": "Corte de cabello",
        "totalCreated": 120,
        "totalCancelled": 10,
        "totalAttended": 95,
        "totalNoShow": 15,
        "totalReservedMinutes": 3600
      },
      {
        "rank": 2,
        "serviceId": "uuid",
        "serviceName": "Tinte completo",
        "totalCreated": 80,
        "totalCancelled": 5,
        "totalAttended": 70,
        "totalNoShow": 5,
        "totalReservedMinutes": 4800
      }
    ]
  },
  "error": null
}
```

Ranking ordenado por `totalCreated` descendente. Si no hay datos, `services` es `[]`.

Errores:

- `400 VALIDATION_ERROR` si no se provee ningún parámetro de mes.
- `400 VALIDATION_ERROR` si el formato de mes no es `yyyy-MM`.
- `400 VALIDATION_ERROR` si `monthTo` < `monthFrom` o el rango excede 24 meses.
- `400 VALIDATION_ERROR` si `super_admin` no incluye `tenantId`.

### GET /reports/peak-hours

Distribución de reservas por hora del día. Si se consulta un rango de fechas, los conteos
se acumulan por hora del día en memoria.

Query params:

```txt
branchId=uuid            (requerido)
date=2026-06-17          (día único; alternativa a dateFrom/dateTo)
dateFrom=2026-06-01      (inicio de rango)
dateTo=2026-06-17        (fin de rango; máx 31 días)
tenantId=uuid            (solo super_admin)
```

Response:

```json
{
  "success": true,
  "data": {
    "branchId": "uuid",
    "periodFrom": "2026-06-17",
    "periodTo": "2026-06-17",
    "hours": [
      { "hourOfDay": 9,  "totalCreated": 5, "totalAttended": 4, "totalCancelled": 1 },
      { "hourOfDay": 10, "totalCreated": 8, "totalAttended": 7, "totalCancelled": 1 },
      { "hourOfDay": 11, "totalCreated": 3, "totalAttended": 3, "totalCancelled": 0 }
    ]
  },
  "error": null
}
```

Solo aparecen las horas con actividad. Si no hay datos, `hours` es `[]`.

Errores:

- `400 VALIDATION_ERROR` si falta `branchId` o ningún parámetro de fecha.
- `400 VALIDATION_ERROR` si el formato de fecha no es `yyyy-MM-dd`.
- `400 VALIDATION_ERROR` si `dateTo` < `dateFrom` o el rango excede 31 días.
- `403` si `branch_admin` intenta consultar una sucursal que no es la propia.

### POST /internal/report-events

Endpoint interno usado por el outbox worker de Booking. No requiere JWT de usuario;
la autenticación servicio-a-servicio se define al implementar el worker.

Request:

```json
{
  "eventId": "uuid",
  "eventType": "ReservationCreated",
  "occurredAt": "2026-06-17T10:00:00Z",
  "tenantId": "uuid",
  "branchId": "uuid",
  "serviceId": "uuid",
  "resourceId": "uuid",
  "reservationId": "uuid",
  "startAt": "2026-06-17T09:00:00-04:00",
  "endAt": "2026-06-17T09:30:00-04:00",
  "status": "CONFIRMED",
  "durationMinutes": 30,
  "serviceName": "Corte de cabello",
  "branchName": "Sucursal Centro",
  "resourceName": "Silla 1"
}
```

Response:

```json
{
  "accepted": true
}
```

## Guia ejecutable para curl y Postman

Esta seccion contiene una prueba para **cada endpoint documentado**. Los bloques
se pueden ejecutar en Bash o importar en Postman mediante `Import > Raw text`.

Estado al 14 de junio de 2026:

- `IMPLEMENTADO`: disponible en el entorno Docker actual.
- `PLANIFICADO`: contrato definido, pero el endpoint todavia responde `404`.

### Preparacion del entorno

Se recomienda tener `curl` y `jq`. Los datos seed se cargan desde
`database/postgres/005_seed_demo.sql`.

```bash
export IDENTITY_URL="http://localhost:5201"
export CATALOG_URL="http://localhost:5202"
export BOOKING_URL="http://localhost:5203"
export REPORTING_URL="http://localhost:5204"

export TENANT_ID="11111111-1111-1111-1111-111111111111"
export TENANT_SLUG="peluqueria-demo"
export SUPER_ADMIN_ID="00000000-0000-0000-0000-000000000001"
export TENANT_ADMIN_ID="22222222-2222-2222-2222-222222222222"
export CLIENT_ID="33333333-3333-3333-3333-333333333333"
export BRANCH_ID="44444444-4444-4444-4444-444444444444"
export SERVICE_ID="55555555-5555-5555-5555-555555555555"
export RESOURCE_ID="66666666-6666-6666-6666-666666666666"
export TEST_DATE="2026-06-15"
export RUN_ID="$(date +%s)"
```

Obtener los tres JWT seed:

```bash
export SUPER_ADMIN_TOKEN="$(curl -sS -X POST "$IDENTITY_URL/auth/login" \
  -H 'Content-Type: application/json' \
  -d '{"email":"superadmin@demo.local","password":"Password123"}' \
  | jq -r '.data.accessToken')"

export TENANT_ADMIN_TOKEN="$(curl -sS -X POST "$IDENTITY_URL/auth/login" \
  -H 'Content-Type: application/json' \
  -d '{"email":"admin@demo.local","password":"Password123"}' \
  | jq -r '.data.accessToken')"

export CLIENT_TOKEN="$(curl -sS -X POST "$IDENTITY_URL/auth/login" \
  -H 'Content-Type: application/json' \
  -d '{"email":"cliente@demo.local","password":"Password123"}' \
  | jq -r '.data.accessToken')"
```

Para Postman, crear variables de entorno equivalentes sin `$` ni llaves, por
ejemplo `identityUrl`, `tenantAdminToken` y `branchId`. Al importar un curl que
contiene variables Bash, reemplazarlas por `{{identityUrl}}`,
`{{tenantAdminToken}}` y `{{branchId}}`.

### Identity - comandos ejecutables

#### POST /auth/login

Estado: `IMPLEMENTADO`. Autenticacion: publica.

```bash
curl -sS -X POST "$IDENTITY_URL/auth/login" \
  -H 'Content-Type: application/json' \
  -d '{"email":"admin@demo.local","password":"Password123"}' | jq
```

#### POST /auth/register-client

Estado: `IMPLEMENTADO`. Autenticacion: publica. El email usa `RUN_ID` para poder
repetir la prueba.

```bash
export TEST_CLIENT_EMAIL="postman.client.$RUN_ID@example.com"
curl -sS -X POST "$IDENTITY_URL/auth/register-client" \
  -H 'Content-Type: application/json' \
  -d "{\"firstName\":\"Cliente\",\"lastName\":\"Postman\",\"email\":\"$TEST_CLIENT_EMAIL\",\"phone\":\"+59170000100\",\"password\":\"Password123\"}" | jq
```

#### GET /auth/me

Estado: `IMPLEMENTADO`. Autenticacion: cualquier JWT valido.

```bash
curl -sS "$IDENTITY_URL/auth/me" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" | jq
```

#### GET /auth/access/branches/{branchId}

Estado: `IMPLEMENTADO`. Autenticacion: usuario administrativo del tenant.

```bash
curl -sS "$IDENTITY_URL/auth/access/branches/$BRANCH_ID" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" | jq
```

#### POST /tenants

Estado: `IMPLEMENTADO`. Autenticacion: `super_admin`. Crea un tenant desechable y
guarda su ID para las pruebas de usuarios.

```bash
export TEST_TENANT_RESPONSE="$(curl -sS -X POST "$IDENTITY_URL/tenants" \
  -H "Authorization: Bearer $SUPER_ADMIN_TOKEN" \
  -H 'Content-Type: application/json' \
  -d "{\"name\":\"Empresa Postman $RUN_ID\",\"slug\":\"empresa-postman-$RUN_ID\",\"mainCategory\":\"Servicios\",\"timezone\":\"America/La_Paz\",\"status\":\"active\"}")"
echo "$TEST_TENANT_RESPONSE" | jq
export TEST_TENANT_ID="$(echo "$TEST_TENANT_RESPONSE" | jq -r '.data.tenantId')"
```

#### GET /tenants/public

Estado: `IMPLEMENTADO`. Autenticacion: publica.

```bash
curl -sS "$IDENTITY_URL/tenants/public" | jq
```

#### GET /tenants/public/{slug}

Estado: `IMPLEMENTADO`. Autenticacion: publica.

```bash
curl -sS "$IDENTITY_URL/tenants/public/$TENANT_SLUG" | jq
```

#### POST /users/admin

Estado: `IMPLEMENTADO`. Autenticacion: `super_admin`. Requiere ejecutar antes
`POST /tenants` para definir `TEST_TENANT_ID`.

```bash
export TEST_ADMIN_EMAIL="postman.admin.$RUN_ID@example.com"
export TEST_ADMIN_RESPONSE="$(curl -sS -X POST "$IDENTITY_URL/users/admin" \
  -H "Authorization: Bearer $SUPER_ADMIN_TOKEN" \
  -H 'Content-Type: application/json' \
  -d "{\"tenantId\":\"$TEST_TENANT_ID\",\"firstName\":\"Admin\",\"lastName\":\"Postman\",\"email\":\"$TEST_ADMIN_EMAIL\",\"phone\":\"+59170000101\",\"password\":\"Password123\"}")"
echo "$TEST_ADMIN_RESPONSE" | jq
export TEST_ADMIN_ID="$(echo "$TEST_ADMIN_RESPONSE" | jq -r '.data.userId')"
```

#### GET /users

Estado: `IMPLEMENTADO`. Autenticacion: `super_admin` o `tenant_admin`.

```bash
curl -sS "$IDENTITY_URL/users?tenantId=$TENANT_ID&role=tenant_admin&status=active&search=admin&offset=0&limit=20" \
  -H "Authorization: Bearer $SUPER_ADMIN_TOKEN" | jq
```

#### GET /users/{userId}

Estado: `IMPLEMENTADO`. Autenticacion: administrador autorizado.

```bash
curl -sS "$IDENTITY_URL/users/$TENANT_ADMIN_ID" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" | jq
```

#### PUT /users/{userId}

Estado: `IMPLEMENTADO`. Autenticacion: administrador autorizado. Usa el usuario
desechable creado por `POST /users/admin`.

```bash
curl -sS -X PUT "$IDENTITY_URL/users/$TEST_ADMIN_ID" \
  -H "Authorization: Bearer $SUPER_ADMIN_TOKEN" \
  -H 'Content-Type: application/json' \
  -d "{\"firstName\":\"Administrador\",\"lastName\":\"Actualizado\",\"email\":\"$TEST_ADMIN_EMAIL\",\"phone\":\"+59170000102\"}" | jq
```

#### PATCH /users/{userId}/status

Estado: `IMPLEMENTADO`. Autenticacion: administrador autorizado.

```bash
curl -sS -X PATCH "$IDENTITY_URL/users/$TEST_ADMIN_ID/status" \
  -H "Authorization: Bearer $SUPER_ADMIN_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"status":"blocked"}' | jq
```

#### DELETE /users/{userId}

Estado: `IMPLEMENTADO`. Autenticacion: administrador autorizado. Realiza baja
logica y responde `204 No Content`.

```bash
curl -i -X DELETE "$IDENTITY_URL/users/$TEST_ADMIN_ID" \
  -H "Authorization: Bearer $SUPER_ADMIN_TOKEN"
```

#### PUT /users/me

Estado: `IMPLEMENTADO`. Autenticacion: cualquier usuario activo. El ejemplo usa
el cliente desechable creado por `POST /auth/register-client`.

```bash
export TEST_CLIENT_TOKEN="$(curl -sS -X POST "$IDENTITY_URL/auth/login" \
  -H 'Content-Type: application/json' \
  -d "{\"email\":\"$TEST_CLIENT_EMAIL\",\"password\":\"Password123\"}" \
  | jq -r '.data.accessToken')"

curl -sS -X PUT "$IDENTITY_URL/users/me" \
  -H "Authorization: Bearer $TEST_CLIENT_TOKEN" \
  -H 'Content-Type: application/json' \
  -d "{\"firstName\":\"Cliente\",\"lastName\":\"Actualizado\",\"email\":\"$TEST_CLIENT_EMAIL\",\"phone\":\"+59170000103\"}" | jq
```

#### PATCH /users/me/password

Estado: `IMPLEMENTADO`. Autenticacion: cualquier usuario activo. Invalida el JWT
usado; volver a iniciar sesion con `Password456` despues de la prueba.

```bash
curl -sS -X PATCH "$IDENTITY_URL/users/me/password" \
  -H "Authorization: Bearer $TEST_CLIENT_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"currentPassword":"Password123","newPassword":"Password456"}' | jq
```

### Catalog - comandos ejecutables

#### GET /public/tenants/{tenantSlug}/branches

Estado: `IMPLEMENTADO`. Autenticacion: publica.

```bash
curl -sS "$CATALOG_URL/public/tenants/$TENANT_SLUG/branches" | jq
```

#### GET /public/tenants/{tenantSlug}/branches/{branchId}/services

Estado: `PLANIFICADO` para HU-009. Autenticacion: publica.

```bash
curl -i "$CATALOG_URL/public/tenants/$TENANT_SLUG/branches/$BRANCH_ID/services"
```

#### GET /branches

Estado: `IMPLEMENTADO`. Autenticacion: `tenant_admin`.

```bash
curl -sS "$CATALOG_URL/branches?status=active" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" | jq
```

#### GET /branches/{branchId}

Estado: `IMPLEMENTADO`. Autenticacion: `tenant_admin` del mismo tenant.

```bash
curl -sS "$CATALOG_URL/branches/$BRANCH_ID" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" | jq
```

#### POST /branches

Estado: `IMPLEMENTADO`. Autenticacion: `tenant_admin`. Guarda el ID creado para
las pruebas de edicion y baja.

```bash
export TEST_BRANCH_RESPONSE="$(curl -sS -X POST "$CATALOG_URL/branches" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" \
  -H 'Content-Type: application/json' \
  -d "{\"name\":\"Sucursal Postman $RUN_ID\",\"address\":\"Av. Pruebas 123\",\"phone\":\"+59170000200\",\"emailContact\":\"sucursal.$RUN_ID@example.com\",\"timezone\":\"America/La_Paz\",\"status\":\"active\"}")"
echo "$TEST_BRANCH_RESPONSE" | jq
export TEST_BRANCH_ID="$(echo "$TEST_BRANCH_RESPONSE" | jq -r '.data.branchId')"
```

#### PUT /branches/{branchId}

Estado: `IMPLEMENTADO`. Autenticacion: `tenant_admin` del mismo tenant.

```bash
curl -sS -X PUT "$CATALOG_URL/branches/$TEST_BRANCH_ID" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"name":"Sucursal Postman Actualizada","address":"Av. Pruebas 456","phone":"+59170000201","emailContact":"sucursal.actualizada@example.com","timezone":"America/La_Paz","status":"active"}' | jq
```

#### PATCH /branches/{branchId}/status

Estado: `IMPLEMENTADO`. Autenticacion: `tenant_admin` del mismo tenant.

```bash
curl -sS -X PATCH "$CATALOG_URL/branches/$TEST_BRANCH_ID/status" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"status":"inactive"}' | jq
```

#### DELETE /branches/{branchId}

Estado: `IMPLEMENTADO`. Autenticacion: `tenant_admin`. Realiza baja logica.

```bash
curl -sS -X DELETE "$CATALOG_URL/branches/$TEST_BRANCH_ID" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" | jq
```

#### GET /public/tenants/{tenantSlug}/services

Estado: `IMPLEMENTADO`. Autenticacion: publica.

```bash
curl -sS "$CATALOG_URL/public/tenants/$TENANT_SLUG/services" | jq
```

#### GET /services

Estado: `IMPLEMENTADO`. Autenticacion: `tenant_admin`.

```bash
curl -sS "$CATALOG_URL/services?status=active" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" | jq
```

#### GET /services/{serviceId}

Estado: `IMPLEMENTADO`. Autenticacion: `tenant_admin` del mismo tenant.

```bash
curl -sS "$CATALOG_URL/services/$SERVICE_ID" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" | jq
```

#### POST /services

Estado: `IMPLEMENTADO`. Autenticacion: `tenant_admin`. Guarda el ID creado para
las pruebas siguientes.

```bash
export TEST_SERVICE_RESPONSE="$(curl -sS -X POST "$CATALOG_URL/services" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" \
  -H 'Content-Type: application/json' \
  -d "{\"name\":\"Servicio Postman $RUN_ID\",\"description\":\"Servicio creado desde curl\",\"durationMinutes\":45,\"referencePrice\":75.50,\"modality\":\"presencial\",\"status\":\"active\"}")"
echo "$TEST_SERVICE_RESPONSE" | jq
export TEST_SERVICE_ID="$(echo "$TEST_SERVICE_RESPONSE" | jq -r '.data.serviceId')"
```

#### PUT /services/{serviceId}

Estado: `IMPLEMENTADO`. Autenticacion: `tenant_admin` del mismo tenant.

```bash
curl -sS -X PUT "$CATALOG_URL/services/$TEST_SERVICE_ID" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"name":"Servicio Postman Actualizado","description":"Servicio editado desde curl","durationMinutes":60,"referencePrice":90.00,"modality":"virtual","status":"active"}' | jq
```

#### PATCH /services/{serviceId}/status

Estado: `IMPLEMENTADO`. Autenticacion: `tenant_admin` del mismo tenant.

```bash
curl -sS -X PATCH "$CATALOG_URL/services/$TEST_SERVICE_ID/status" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"status":"inactive"}' | jq
```

#### DELETE /services/{serviceId}

Estado: `IMPLEMENTADO`. Autenticacion: `tenant_admin`. Realiza baja logica.

```bash
curl -sS -X DELETE "$CATALOG_URL/services/$TEST_SERVICE_ID" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" | jq
```

#### POST /branches/{branchId}/services/{serviceId}

Estado: `PLANIFICADO` para HU-009. Autenticacion esperada: `tenant_admin`.

```bash
curl -i -X POST "$CATALOG_URL/branches/$BRANCH_ID/services/$SERVICE_ID" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN"
```

#### DELETE /branches/{branchId}/services/{serviceId}

Estado: `PLANIFICADO` para HU-009. Autenticacion esperada: `tenant_admin`.

```bash
curl -i -X DELETE "$CATALOG_URL/branches/$BRANCH_ID/services/$SERVICE_ID" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN"
```

#### GET /resources

Estado: `IMPLEMENTADO`. Autenticacion: `tenant_admin`.

```bash
curl -sS "$CATALOG_URL/resources?branchId=$BRANCH_ID&status=active" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" | jq
```

#### GET /resources/{resourceId}

Estado: `IMPLEMENTADO`. Autenticacion: `tenant_admin` del mismo tenant.

```bash
curl -sS "$CATALOG_URL/resources/$RESOURCE_ID" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" | jq
```

#### POST /resources

Estado: `IMPLEMENTADO`. Autenticacion: `tenant_admin`. Guarda el ID creado para
las pruebas siguientes.

```bash
export TEST_RESOURCE_RESPONSE="$(curl -sS -X POST "$CATALOG_URL/resources" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" \
  -H 'Content-Type: application/json' \
  -d "{\"branchId\":\"$BRANCH_ID\",\"name\":\"Silla Postman $RUN_ID\",\"resourceType\":\"silla\",\"description\":\"Recurso creado desde curl\",\"capacity\":1,\"status\":\"active\"}")"
echo "$TEST_RESOURCE_RESPONSE" | jq
export TEST_RESOURCE_ID="$(echo "$TEST_RESOURCE_RESPONSE" | jq -r '.data.resourceId')"
```

#### PUT /resources/{resourceId}

Estado: `IMPLEMENTADO`. Autenticacion: `tenant_admin` del mismo tenant.

```bash
curl -sS -X PUT "$CATALOG_URL/resources/$TEST_RESOURCE_ID" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" \
  -H 'Content-Type: application/json' \
  -d "{\"branchId\":\"$BRANCH_ID\",\"name\":\"Sala Postman Actualizada\",\"resourceType\":\"sala\",\"description\":\"Recurso editado desde curl\",\"capacity\":4,\"status\":\"blocked\"}" | jq
```

#### PATCH /resources/{resourceId}/status

Estado: `IMPLEMENTADO`. Autenticacion: `tenant_admin` del mismo tenant.

```bash
curl -sS -X PATCH "$CATALOG_URL/resources/$TEST_RESOURCE_ID/status" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"status":"inactive"}' | jq
```

#### DELETE /resources/{resourceId}

Estado: `IMPLEMENTADO`. Autenticacion: `tenant_admin`. Realiza baja logica.

```bash
curl -sS -X DELETE "$CATALOG_URL/resources/$TEST_RESOURCE_ID" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" | jq
```

#### GET /services/{serviceId}/resources

Estado: `IMPLEMENTADO`. Autenticacion: `tenant_admin`.

```bash
curl -sS "$CATALOG_URL/services/$SERVICE_ID/resources?status=active" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" | jq
```

#### GET /services/{serviceId}/compatible-resources

Estado: `IMPLEMENTADO`. Autenticacion: `tenant_admin`. Devuelve solo recursos
activos con asociacion activa.

```bash
curl -sS "$CATALOG_URL/services/$SERVICE_ID/compatible-resources?branchId=$BRANCH_ID" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" | jq
```

#### POST /services/{serviceId}/resources/{resourceId}

Estado: `IMPLEMENTADO`. Autenticacion: `tenant_admin`.

```bash
curl -sS -X POST "$CATALOG_URL/services/$SERVICE_ID/resources/$RESOURCE_ID" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"required":true,"priority":1,"status":"active"}' | jq
```

#### PUT /services/{serviceId}/resources/{resourceId}

Estado: `IMPLEMENTADO`. Autenticacion: `tenant_admin`.

```bash
curl -sS -X PUT "$CATALOG_URL/services/$SERVICE_ID/resources/$RESOURCE_ID" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"required":true,"priority":2,"status":"active"}' | jq
```

#### PATCH /services/{serviceId}/resources/{resourceId}/status

Estado: `IMPLEMENTADO`. Autenticacion: `tenant_admin`.

```bash
curl -sS -X PATCH "$CATALOG_URL/services/$SERVICE_ID/resources/$RESOURCE_ID/status" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"status":"inactive"}' | jq
```

#### DELETE /services/{serviceId}/resources/{resourceId}

Estado: `IMPLEMENTADO`. Autenticacion: `tenant_admin`. Realiza baja logica de la
compatibilidad y responde `204 No Content`.

```bash
curl -i -X DELETE "$CATALOG_URL/services/$SERVICE_ID/resources/$RESOURCE_ID" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN"
```

#### GET /resource-schedules

Estado: `IMPLEMENTADO`. Autenticacion: `tenant_admin`.

```bash
curl -sS "$CATALOG_URL/resource-schedules?branchId=$BRANCH_ID&resourceId=$RESOURCE_ID&dayOfWeek=1&status=active" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" | jq
```

#### GET /resource-schedules/{scheduleId}

Estado: `IMPLEMENTADO`. Autenticacion: `tenant_admin`.

```bash
curl -sS "$CATALOG_URL/resource-schedules/$TEST_SCHEDULE_ID" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" | jq
```

#### POST /resource-schedules

Estado: `IMPLEMENTADO`. Autenticacion: `tenant_admin`. Guarda el ID creado para
las pruebas siguientes.

```bash
export TEST_SCHEDULE_RESPONSE="$(curl -sS -X POST "$CATALOG_URL/resource-schedules" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" \
  -H 'Content-Type: application/json' \
  -d "{\"branchId\":\"$BRANCH_ID\",\"resourceId\":\"$RESOURCE_ID\",\"dayOfWeek\":1,\"startTime\":\"09:00\",\"endTime\":\"18:00\",\"validFrom\":\"2026-06-16\",\"validTo\":null,\"status\":\"active\"}")"
echo "$TEST_SCHEDULE_RESPONSE" | jq
export TEST_SCHEDULE_ID="$(echo "$TEST_SCHEDULE_RESPONSE" | jq -r '.data.scheduleId')"
```

#### PUT /resource-schedules/{scheduleId}

Estado: `IMPLEMENTADO`. Autenticacion: `tenant_admin`.

```bash
curl -sS -X PUT "$CATALOG_URL/resource-schedules/$TEST_SCHEDULE_ID" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" \
  -H 'Content-Type: application/json' \
  -d "{\"branchId\":\"$BRANCH_ID\",\"resourceId\":\"$RESOURCE_ID\",\"dayOfWeek\":2,\"startTime\":\"10:00\",\"endTime\":\"16:30\",\"validFrom\":\"2026-06-16\",\"validTo\":\"2026-12-31\",\"status\":\"active\"}" | jq
```

#### PATCH /resource-schedules/{scheduleId}/status

Estado: `IMPLEMENTADO`. Autenticacion: `tenant_admin`.

```bash
curl -sS -X PATCH "$CATALOG_URL/resource-schedules/$TEST_SCHEDULE_ID/status" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"status":"inactive"}' | jq
```

#### DELETE /resource-schedules/{scheduleId}

Estado: `IMPLEMENTADO`. Autenticacion: `tenant_admin`. Realiza baja logica.

```bash
curl -sS -X DELETE "$CATALOG_URL/resource-schedules/$TEST_SCHEDULE_ID" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" | jq
```

### Booking - comandos de contrato

#### GET /availability

Estado: `IMPLEMENTADO` para HU-014. Autenticacion: publica. El ejemplo usa la
data seed del entorno local; `2026-06-17` es miercoles y coincide con los horarios
base seed de lunes a viernes.

```bash
export TEST_DATE="2026-06-17"

curl -sS "$BOOKING_URL/availability?tenantSlug=$TENANT_SLUG&branchId=$BRANCH_ID&serviceId=$SERVICE_ID&date=$TEST_DATE" | jq
```

Prueba de validacion:

```bash
curl -sS "$BOOKING_URL/availability?tenantSlug=$TENANT_SLUG&branchId=$BRANCH_ID&serviceId=$SERVICE_ID&date=17-06-2026" | jq
```

#### POST /reservations

Estado: `IMPLEMENTADO` para HU-015. Autenticacion: `client`. `Idempotency-Key`
debe ser unico por intento; el endpoint ya crea reserva, historial y evento outbox
en la misma transaccion.

```bash
export TEST_RESERVATION_RESPONSE="$(curl -sS -X POST "$BOOKING_URL/reservations" \
  -H "Authorization: Bearer $CLIENT_TOKEN" \
  -H "Idempotency-Key: $(cat /proc/sys/kernel/random/uuid)" \
  -H 'Content-Type: application/json' \
  -d "{\"branchId\":\"$BRANCH_ID\",\"serviceId\":\"$SERVICE_ID\",\"resourceId\":\"$RESOURCE_ID\",\"startAt\":\"2026-06-17T09:00:00-04:00\",\"notes\":\"Reserva de prueba desde curl\"}")"
echo "$TEST_RESERVATION_RESPONSE" | jq
export RESERVATION_ID="$(echo "$TEST_RESERVATION_RESPONSE" | jq -r '.data.reservationId')"
```

Prueba de conflicto ejecutando la misma reserva por segunda vez:

```bash
curl -sS -X POST "$BOOKING_URL/reservations" \
  -H "Authorization: Bearer $CLIENT_TOKEN" \
  -H "Idempotency-Key: $(cat /proc/sys/kernel/random/uuid)" \
  -H 'Content-Type: application/json' \
  -d "{\"branchId\":\"$BRANCH_ID\",\"serviceId\":\"$SERVICE_ID\",\"resourceId\":\"$RESOURCE_ID\",\"startAt\":\"2026-06-17T09:00:00-04:00\",\"notes\":\"Reserva duplicada desde curl\"}" | jq
```

#### GET /reservations/{reservationId}

Estado: `IMPLEMENTADO` para HU-017. Autenticacion: cualquier JWT valido.

```bash
curl -sS "$BOOKING_URL/reservations/$RESERVATION_ID" \
  -H "Authorization: Bearer $CLIENT_TOKEN" | jq
```

#### PATCH /reservations/{reservationId}/cancel

Estado: `IMPLEMENTADO` para HU-018. Autenticacion: cliente propietario o usuario interno.

```bash
curl -i -X PATCH "$BOOKING_URL/reservations/$RESERVATION_ID/cancel" \
  -H "Authorization: Bearer $CLIENT_TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"reason":"Cancelado desde curl"}'
```

#### PATCH /reservations/{reservationId}/attend

Estado: `IMPLEMENTADO` para HU-019. Autenticacion: usuario interno.

```bash
curl -i -X PATCH "$BOOKING_URL/reservations/$RESERVATION_ID/attend" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN"
```

#### PATCH /reservations/{reservationId}/no-show

Estado: `IMPLEMENTADO` para HU-020. Autenticacion: usuario interno.

```bash
curl -i -X PATCH "$BOOKING_URL/reservations/$RESERVATION_ID/no-show" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN"
```

#### GET /admin/reservations

Estado: `IMPLEMENTADO` para HU-022. Autenticacion: usuario interno (client → 403).
Busca reservas con filtros; incluye historial de estado por reserva.

```bash
curl -sS "$BOOKING_URL/admin/reservations?branchId=$BRANCH_ID&dateFrom=2026-06-01&dateTo=2026-06-30&status=CONFIRMED" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" | jq
```

Busqueda por cliente especifico:

```bash
curl -sS "$BOOKING_URL/admin/reservations?clientUserId=$CLIENT_ID" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" | jq
```

#### GET /admin/agenda

Estado: `IMPLEMENTADO` para HU-021 (vista agenda). Autenticacion: usuario interno.

```bash
curl -sS "$BOOKING_URL/admin/agenda?branchId=$BRANCH_ID&date=$TEST_DATE&resourceId=$RESOURCE_ID&status=CONFIRMED" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" | jq
```

#### POST /resource-blocks

Estado: `IMPLEMENTADO` para HU-021. Autenticacion: usuario interno. Guardar `data.blockId` como `BLOCK_ID`.

```bash
export BLOCK_RESPONSE="$(curl -sS -X POST "$BOOKING_URL/resource-blocks" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" \
  -H 'Content-Type: application/json' \
  -d "{\"branchId\":\"$BRANCH_ID\",\"resourceId\":\"$RESOURCE_ID\",\"startAt\":\"${TEST_DATE}T13:00:00-04:00\",\"endAt\":\"${TEST_DATE}T15:00:00-04:00\",\"reason\":\"Mantenimiento de prueba\",\"blockType\":\"manual\"}")"
echo "$BLOCK_RESPONSE" | jq
export BLOCK_ID="$(echo "$BLOCK_RESPONSE" | jq -r '.data.blockId')"
```

#### GET /resource-blocks/{blockId}

Estado: `IMPLEMENTADO`. Autenticacion: usuario interno.

```bash
curl -sS "$BOOKING_URL/resource-blocks/$BLOCK_ID" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" | jq
```

#### PATCH /resource-blocks/{blockId}/cancel

Estado: `IMPLEMENTADO` para HU-023. Autenticacion: usuario interno.

```bash
curl -i -X PATCH "$BOOKING_URL/resource-blocks/$BLOCK_ID/cancel" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN"
```

### Reporting - comandos de contrato

Todos los endpoints de esta seccion estan `IMPLEMENTADOS` (HU-024 a HU-027).
Reporting lee desde Cassandra (puerto 9142 en el entorno local Docker).

#### GET /reports/daily-summary

Estado: `IMPLEMENTADO` para HU-024. Autenticacion: usuario interno. Responde
`dataStatus: "PENDING_SYNC"` si Cassandra no tiene datos para esa fecha.

```bash
curl -sS "$REPORTING_URL/reports/daily-summary?date=$TEST_DATE" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" | jq
```

Con filtro de sucursal:

```bash
curl -sS "$REPORTING_URL/reports/daily-summary?date=$TEST_DATE&branchId=$BRANCH_ID" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" | jq
```

Como super_admin (requiere tenantId en query):

```bash
curl -sS "$REPORTING_URL/reports/daily-summary?date=$TEST_DATE&tenantId=$TENANT_ID" \
  -H "Authorization: Bearer $SUPER_ADMIN_TOKEN" | jq
```

#### GET /reports/resources/occupancy

Estado: `IMPLEMENTADO` para HU-025. Autenticacion: usuario interno. No expone
datos personales del cliente (solo conteos agregados).

```bash
curl -sS "$REPORTING_URL/reports/resources/occupancy?branchId=$BRANCH_ID&date=$TEST_DATE" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" | jq
```

Con rango de fechas (maximo 31 dias):

```bash
curl -sS "$REPORTING_URL/reports/resources/occupancy?branchId=$BRANCH_ID&dateFrom=2026-06-01&dateTo=2026-06-17" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" | jq
```

#### GET /reports/services/top

Estado: `IMPLEMENTADO` para HU-026. Autenticacion: usuario interno. Ranking
ordenado por `totalCreated` descendente.

```bash
curl -sS "$REPORTING_URL/reports/services/top?month=2026-06" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" | jq
```

Con rango mensual (maximo 24 meses, agrega en memoria):

```bash
curl -sS "$REPORTING_URL/reports/services/top?monthFrom=2026-01&monthTo=2026-06" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" | jq
```

#### GET /reports/peak-hours

Estado: `IMPLEMENTADO` para HU-027. Autenticacion: usuario interno. Devuelve
solo horas con actividad; si se usa rango los conteos se acumulan por hora del dia.

```bash
curl -sS "$REPORTING_URL/reports/peak-hours?branchId=$BRANCH_ID&date=$TEST_DATE" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" | jq
```

Con rango de fechas:

```bash
curl -sS "$REPORTING_URL/reports/peak-hours?branchId=$BRANCH_ID&dateFrom=2026-06-01&dateTo=2026-06-17" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" | jq
```

Prueba de validacion (branchId faltante → 400):

```bash
curl -sS "$REPORTING_URL/reports/peak-hours?date=$TEST_DATE" \
  -H "Authorization: Bearer $TENANT_ADMIN_TOKEN" | jq
```

#### POST /internal/report-events

Estado: endpoint disponible pero el worker outbox de Booking aun no esta implementado.
Autenticacion: llamada interna sin JWT de usuario.

```bash
curl -i -X POST "$REPORTING_URL/internal/report-events" \
  -H 'Content-Type: application/json' \
  -d "{\"eventId\":\"77777777-7777-7777-7777-777777777777\",\"eventType\":\"ReservationCreated\",\"occurredAt\":\"${TEST_DATE}T13:00:00Z\",\"tenantId\":\"$TENANT_ID\",\"branchId\":\"$BRANCH_ID\",\"serviceId\":\"$SERVICE_ID\",\"resourceId\":\"$RESOURCE_ID\",\"reservationId\":\"88888888-8888-8888-8888-888888888888\",\"startAt\":\"${TEST_DATE}T09:00:00-04:00\",\"endAt\":\"${TEST_DATE}T09:30:00-04:00\",\"status\":\"CONFIRMED\",\"durationMinutes\":30,\"serviceName\":\"Corte de cabello\",\"branchName\":\"Sucursal Centro\",\"resourceName\":\"Silla 1\"}"
```
