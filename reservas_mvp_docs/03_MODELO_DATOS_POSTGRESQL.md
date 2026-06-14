# Modelo de datos PostgreSQL - operación principal

PostgreSQL será la base de datos principal del sistema operativo. Aquí viven tenants, usuarios, sucursales, servicios, recursos, horarios, reservas, bloqueos e historial.

Para MVP local se usará una sola instancia de PostgreSQL con esquemas separados por dominio:

- `identity`
- `catalog`
- `booking`

Esto no significa que todos los servicios puedan tocar todo. Cada microservicio debe escribir solo en su esquema.

## Extensiones recomendadas

```sql
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE EXTENSION IF NOT EXISTS btree_gist;
```

`pgcrypto` permite generar UUIDs con `gen_random_uuid()`.

`btree_gist` permite crear restricciones de exclusión combinando UUIDs y rangos de tiempo.

## Esquema identity

```sql
CREATE SCHEMA IF NOT EXISTS identity;
```

### identity.tenants

```sql
CREATE TABLE identity.tenants (
  tenant_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  name varchar(150) NOT NULL,
  slug varchar(120) NOT NULL UNIQUE,
  main_category varchar(120),
  timezone varchar(80) NOT NULL DEFAULT 'America/La_Paz',
  status varchar(30) NOT NULL DEFAULT 'active',
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);
```

### identity.roles

```sql
CREATE TABLE identity.roles (
  role_id smallserial PRIMARY KEY,
  code varchar(50) NOT NULL UNIQUE,
  name varchar(80) NOT NULL,
  description text
);
```

Seed mínimo:

```sql
INSERT INTO identity.roles (code, name) VALUES
('super_admin', 'Super administrador'),
('tenant_admin', 'Administrador de empresa'),
('branch_admin', 'Administrador de sucursal'),
('receptionist', 'Recepcionista'),
('professional', 'Profesional'),
('client', 'Cliente')
ON CONFLICT (code) DO NOTHING;
```

### identity.users

```sql
CREATE TABLE identity.users (
  user_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid REFERENCES identity.tenants(tenant_id),
  email varchar(180) NOT NULL,
  password_hash text NOT NULL,
  first_name varchar(120) NOT NULL,
  last_name varchar(120),
  phone varchar(40),
  status varchar(30) NOT NULL DEFAULT 'active',
  last_login_at timestamptz,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX ux_users_email_normalized
ON identity.users (lower(email));
```

El email identifica una cuenta de forma global. `tenant_id` es obligatorio para
usuarios internos de empresa y es `NULL` para `super_admin` y `client`. Un cliente
no pertenece al negocio donde reserva; esa relación se guarda en cada reserva
mediante `tenant_id` y `client_user_id`.

### identity.user_roles

```sql
CREATE TABLE identity.user_roles (
  user_id uuid NOT NULL REFERENCES identity.users(user_id),
  role_id smallint NOT NULL REFERENCES identity.roles(role_id),
  created_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (user_id, role_id)
);
```

### identity.user_branch_access

```sql
CREATE TABLE identity.user_branch_access (
  user_id uuid NOT NULL REFERENCES identity.users(user_id),
  tenant_id uuid NOT NULL REFERENCES identity.tenants(tenant_id),
  branch_id uuid NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (user_id, branch_id)
);
```

`branch_id` vive en `catalog.branches`, pero para evitar acoplar demasiado entre esquemas, la validación fuerte puede hacerla Catalog Service.

## Esquema catalog

```sql
CREATE SCHEMA IF NOT EXISTS catalog;
```

### catalog.branches

```sql
CREATE TABLE catalog.branches (
  branch_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  name varchar(150) NOT NULL,
  address text,
  phone varchar(40),
  email_contact varchar(180),
  timezone varchar(80) NOT NULL DEFAULT 'America/La_Paz',
  status varchar(30) NOT NULL DEFAULT 'active',
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX idx_branches_tenant_status ON catalog.branches (tenant_id, status);
```

### catalog.services

```sql
CREATE TABLE catalog.services (
  service_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  name varchar(150) NOT NULL,
  description text,
  duration_minutes int NOT NULL CHECK (duration_minutes > 0),
  reference_price numeric(10,2),
  modality varchar(30) NOT NULL DEFAULT 'presencial',
  requires_confirmation boolean NOT NULL DEFAULT false,
  status varchar(30) NOT NULL DEFAULT 'active',
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX idx_services_tenant_status ON catalog.services (tenant_id, status);
```

### catalog.branch_services

```sql
CREATE TABLE catalog.branch_services (
  tenant_id uuid NOT NULL,
  branch_id uuid NOT NULL REFERENCES catalog.branches(branch_id),
  service_id uuid NOT NULL REFERENCES catalog.services(service_id),
  status varchar(30) NOT NULL DEFAULT 'active',
  created_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (branch_id, service_id)
);

CREATE INDEX idx_branch_services_tenant ON catalog.branch_services (tenant_id, service_id);
```

### catalog.resources

```sql
CREATE TABLE catalog.resources (
  resource_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  branch_id uuid NOT NULL REFERENCES catalog.branches(branch_id),
  name varchar(150) NOT NULL,
  resource_type varchar(80) NOT NULL,
  description text,
  capacity int NOT NULL DEFAULT 1,
  status varchar(30) NOT NULL DEFAULT 'active',
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX idx_resources_branch_status ON catalog.resources (tenant_id, branch_id, status);
```

### catalog.service_resources

```sql
CREATE TABLE catalog.service_resources (
  tenant_id uuid NOT NULL,
  service_id uuid NOT NULL REFERENCES catalog.services(service_id),
  resource_id uuid NOT NULL REFERENCES catalog.resources(resource_id),
  required boolean NOT NULL DEFAULT true,
  priority int NOT NULL DEFAULT 1,
  status varchar(30) NOT NULL DEFAULT 'active',
  created_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (service_id, resource_id)
);

CREATE INDEX idx_service_resources_resource ON catalog.service_resources (tenant_id, resource_id);
```

### catalog.resource_schedules

```sql
CREATE TABLE catalog.resource_schedules (
  schedule_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  branch_id uuid NOT NULL REFERENCES catalog.branches(branch_id),
  resource_id uuid NOT NULL REFERENCES catalog.resources(resource_id),
  day_of_week smallint NOT NULL CHECK (day_of_week BETWEEN 1 AND 7),
  start_time time NOT NULL,
  end_time time NOT NULL,
  valid_from date,
  valid_to date,
  status varchar(30) NOT NULL DEFAULT 'active',
  created_at timestamptz NOT NULL DEFAULT now(),
  CHECK (end_time > start_time)
);

CREATE INDEX idx_resource_schedules_lookup
ON catalog.resource_schedules (tenant_id, branch_id, resource_id, day_of_week, status);
```

## Esquema booking

```sql
CREATE SCHEMA IF NOT EXISTS booking;
```

## Estados permitidos

Reservas:

- `CONFIRMED`
- `CANCELLED`
- `ATTENDED`
- `NO_SHOW`

Bloqueos:

- `ACTIVE`
- `CANCELLED`

### booking.reservations

```sql
CREATE TABLE booking.reservations (
  reservation_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  branch_id uuid NOT NULL,
  client_user_id uuid NOT NULL,
  service_id uuid NOT NULL,
  resource_id uuid NOT NULL,
  created_by_user_id uuid,
  start_at timestamptz NOT NULL,
  end_at timestamptz NOT NULL,
  status varchar(30) NOT NULL DEFAULT 'CONFIRMED',
  channel_origin varchar(40) NOT NULL DEFAULT 'web',
  notes text,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  CHECK (end_at > start_at)
);

CREATE INDEX idx_reservations_agenda
ON booking.reservations (tenant_id, branch_id, start_at, status);

CREATE INDEX idx_reservations_client
ON booking.reservations (client_user_id, start_at DESC);

CREATE INDEX idx_reservations_resource_day
ON booking.reservations (tenant_id, resource_id, start_at);
```

El índice por cliente no incluye `tenant_id` al inicio porque una cuenta `client`
es global y debe poder consultar su historial de reservas en todos los negocios.

### Prevención de doble reserva

La forma recomendada es crear una restricción de exclusión para que no existan reservas activas solapadas sobre el mismo recurso.

```sql
ALTER TABLE booking.reservations
ADD CONSTRAINT no_overlapping_confirmed_reservations
EXCLUDE USING gist (
  tenant_id WITH =,
  resource_id WITH =,
  tstzrange(start_at, end_at, '[)') WITH &&
)
WHERE (status = 'CONFIRMED');
```

Con esto, si dos usuarios intentan reservar el mismo recurso en horarios que se pisan, PostgreSQL rechaza una de las operaciones.

### booking.resource_blocks

```sql
CREATE TABLE booking.resource_blocks (
  block_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  branch_id uuid NOT NULL,
  resource_id uuid NOT NULL,
  start_at timestamptz NOT NULL,
  end_at timestamptz NOT NULL,
  reason text,
  block_type varchar(50) NOT NULL DEFAULT 'manual',
  status varchar(30) NOT NULL DEFAULT 'ACTIVE',
  created_by_user_id uuid NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  CHECK (end_at > start_at)
);

CREATE INDEX idx_blocks_resource_day
ON booking.resource_blocks (tenant_id, resource_id, start_at, status);
```

### Prevención de bloqueo solapado

```sql
ALTER TABLE booking.resource_blocks
ADD CONSTRAINT no_overlapping_active_blocks
EXCLUDE USING gist (
  tenant_id WITH =,
  resource_id WITH =,
  tstzrange(start_at, end_at, '[)') WITH &&
)
WHERE (status = 'ACTIVE');
```

### Validación reserva vs bloqueo

PostgreSQL no puede crear una única constraint entre dos tablas diferentes para evitar reserva contra bloqueo. Por eso Booking Service debe validar dentro de la transacción:

```sql
SELECT 1
FROM booking.resource_blocks
WHERE tenant_id = @tenant_id
  AND resource_id = @resource_id
  AND status = 'ACTIVE'
  AND tstzrange(start_at, end_at, '[)') && tstzrange(@start_at, @end_at, '[)')
LIMIT 1;
```

Si existe un bloqueo activo, se rechaza la reserva con `409 Conflict`.

### booking.reservation_history

```sql
CREATE TABLE booking.reservation_history (
  history_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  reservation_id uuid NOT NULL REFERENCES booking.reservations(reservation_id),
  user_id uuid,
  previous_status varchar(30),
  new_status varchar(30) NOT NULL,
  action varchar(80) NOT NULL,
  reason text,
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX idx_history_reservation
ON booking.reservation_history (tenant_id, reservation_id, created_at);
```

### booking.reservation_event_outbox

Esta tabla permite enviar datos a Reporting sin depender de que Cassandra esté disponible en el mismo instante.

```sql
CREATE TABLE booking.reservation_event_outbox (
  event_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  event_type varchar(80) NOT NULL,
  aggregate_id uuid NOT NULL,
  payload jsonb NOT NULL,
  status varchar(30) NOT NULL DEFAULT 'PENDING',
  attempts int NOT NULL DEFAULT 0,
  last_error text,
  created_at timestamptz NOT NULL DEFAULT now(),
  processed_at timestamptz
);

CREATE INDEX idx_outbox_pending
ON booking.reservation_event_outbox (status, created_at);
```

Eventos mínimos:

- `ReservationCreated`
- `ReservationCancelled`
- `ReservationAttended`
- `ReservationNoShow`
- `ResourceBlocked`
- `ResourceBlockCancelled`

## Flujo transaccional para crear reserva

1. Iniciar transacción.
2. Validar tenant, sucursal, servicio y recurso activo.
3. Validar horario base desde Catalog Service o una copia cacheada.
4. Validar que no exista bloqueo activo que se solape.
5. Insertar reserva `CONFIRMED`.
6. Si hay solapamiento con otra reserva activa, PostgreSQL lanza error por constraint.
7. Insertar historial.
8. Insertar evento en outbox.
9. Commit.
10. Devolver `201 Created`.

## Flujo para cancelar reserva

1. Buscar reserva por `reservation_id` y `tenant_id`.
2. Si ya está `CANCELLED`, devolver estado actual.
3. Cambiar estado a `CANCELLED`.
4. Insertar historial.
5. Insertar evento `ReservationCancelled` en outbox.
6. Commit.

La restricción de solapamiento solo aplica a reservas `CONFIRMED`, por eso al cancelar se libera el horario automáticamente.
