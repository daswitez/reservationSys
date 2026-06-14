# Modelo Cassandra - Reporting Service

Cassandra se usa solo para reportes. No debe participar en el flujo principal de crear reservas.

La idea es guardar datos ya preparados para las consultas que necesita el panel administrativo. No se hacen joins. No se usa `ALLOW FILTERING`. Cada tabla responde una pregunta concreta.

## Keyspace

```sql
CREATE KEYSPACE IF NOT EXISTS reservas_reports
WITH replication = {
  'class': 'SimpleStrategy',
  'replication_factor': 1
};
```

En producción podría usarse `NetworkTopologyStrategy`, pero para MVP local `SimpleStrategy` basta.

## Principios de modelado

1. Toda tabla incluye `tenant_id`.
2. Las particiones se diseñan por tenant, sucursal, fecha o mes.
3. Se duplican datos si ayuda a consultar rápido.
4. No se guardan datos personales sensibles del cliente en reportes agregados.
5. Los reportes pueden tener consistencia eventual.
6. El Reporting Service puede recalcular agregados desde eventos si hace falta.

## Eventos que recibe Reporting

Reporting recibe eventos desde Booking:

- `ReservationCreated`
- `ReservationCancelled`
- `ReservationAttended`
- `ReservationNoShow`
- `ResourceBlocked`
- `ResourceBlockCancelled`

Payload mínimo recomendado:

```json
{
  "eventId": "uuid",
  "eventType": "ReservationCreated",
  "occurredAt": "2026-06-12T10:00:00Z",
  "tenantId": "uuid",
  "branchId": "uuid",
  "serviceId": "uuid",
  "resourceId": "uuid",
  "reservationId": "uuid",
  "startAt": "2026-06-12T15:00:00Z",
  "endAt": "2026-06-12T15:30:00Z",
  "status": "CONFIRMED",
  "durationMinutes": 30,
  "serviceName": "Corte de cabello",
  "branchName": "Sucursal Centro",
  "resourceName": "Silla 1"
}
```

## Tabla 1 - Resumen diario por tenant

Consulta: dashboard general del tenant por día.

```sql
CREATE TABLE IF NOT EXISTS report_daily_summary_by_tenant (
  tenant_id uuid,
  report_date date,
  total_created int,
  total_confirmed int,
  total_cancelled int,
  total_attended int,
  total_no_show int,
  total_blocked_minutes int,
  total_reserved_minutes int,
  updated_at timestamp,
  PRIMARY KEY ((tenant_id), report_date)
) WITH CLUSTERING ORDER BY (report_date DESC);
```

Uso:

- Ver resumen del día.
- Ver evolución diaria.
- Mostrar cards: reservas, cancelaciones, atendidas, no-show.

## Tabla 2 - Resumen diario por sucursal

Consulta: dashboard por sucursal y fecha.

```sql
CREATE TABLE IF NOT EXISTS report_daily_summary_by_branch (
  tenant_id uuid,
  branch_id uuid,
  report_date date,
  branch_name text,
  total_created int,
  total_confirmed int,
  total_cancelled int,
  total_attended int,
  total_no_show int,
  total_reserved_minutes int,
  updated_at timestamp,
  PRIMARY KEY ((tenant_id, branch_id), report_date)
) WITH CLUSTERING ORDER BY (report_date DESC);
```

Uso:

- Comparar actividad por sucursal.
- Ver si una sucursal tiene muchas cancelaciones.

## Tabla 3 - Reservas por servicio por mes

Consulta: ranking de servicios más reservados.

```sql
CREATE TABLE IF NOT EXISTS report_service_summary_by_month (
  tenant_id uuid,
  year_month text,
  service_id uuid,
  service_name text,
  total_created int,
  total_cancelled int,
  total_attended int,
  total_no_show int,
  total_reserved_minutes int,
  updated_at timestamp,
  PRIMARY KEY ((tenant_id, year_month), service_id)
);
```

`year_month` formato recomendado: `2026-06`.

Uso:

- Servicios más vendidos/reservados.
- Servicios con más cancelaciones.
- Decidir qué servicios destacar.

## Tabla 4 - Ocupación por recurso por día

Consulta: saber qué silla, sala, profesional o equipo se usa más.

```sql
CREATE TABLE IF NOT EXISTS report_resource_occupancy_by_day (
  tenant_id uuid,
  branch_id uuid,
  resource_id uuid,
  report_date date,
  resource_name text,
  resource_type text,
  total_reservations int,
  total_attended int,
  total_cancelled int,
  total_no_show int,
  reserved_minutes int,
  blocked_minutes int,
  updated_at timestamp,
  PRIMARY KEY ((tenant_id, branch_id, report_date), resource_id)
);
```

Uso:

- Ver recursos subutilizados.
- Ver si una silla/profesional está muy cargado.
- Comparar ocupación diaria.

## Tabla 5 - Horas pico por sucursal

Consulta: saber en qué horas se concentra la demanda.

```sql
CREATE TABLE IF NOT EXISTS report_peak_hours_by_branch_day (
  tenant_id uuid,
  branch_id uuid,
  report_date date,
  hour_of_day int,
  branch_name text,
  total_created int,
  total_attended int,
  total_cancelled int,
  updated_at timestamp,
  PRIMARY KEY ((tenant_id, branch_id, report_date), hour_of_day)
);
```

Uso:

- Identificar horas con más reservas.
- Ajustar horarios y recursos.

## Tabla 6 - Estado de reservas por mes

Consulta: distribución mensual por estado.

```sql
CREATE TABLE IF NOT EXISTS report_reservation_status_by_month (
  tenant_id uuid,
  year_month text,
  status text,
  total int,
  updated_at timestamp,
  PRIMARY KEY ((tenant_id, year_month), status)
);
```

Uso:

- Gráfico de confirmadas vs canceladas vs atendidas vs no-show.

## Tabla 7 - Eventos recibidos para idempotencia

Sirve para no procesar dos veces el mismo evento.

```sql
CREATE TABLE IF NOT EXISTS report_processed_events (
  event_id uuid PRIMARY KEY,
  tenant_id uuid,
  event_type text,
  processed_at timestamp
);
```

Antes de actualizar agregados, Reporting debe verificar si el `event_id` ya fue procesado.

## Cómo actualizar contadores

Para MVP se puede hacer update incremental.

Ejemplo conceptual al recibir `ReservationCreated`:

```sql
UPDATE report_daily_summary_by_tenant
SET total_created = total_created + 1,
    total_confirmed = total_confirmed + 1,
    total_reserved_minutes = total_reserved_minutes + 30,
    updated_at = toTimestamp(now())
WHERE tenant_id = ? AND report_date = ?;
```

Nota: los contadores en Cassandra deben manejarse con cuidado. Para MVP pequeño se puede usar columnas numéricas normales con lógica de read-update-write desde el servicio. Si se espera alta concurrencia real, se puede evaluar `counter`, pero complica correcciones. Para este MVP, usar read-update-write simple es más fácil de mantener.

## Reportes que expone el servicio

- `GET /reports/daily-summary?date=YYYY-MM-DD`
- `GET /reports/branches/{branchId}/daily-summary?from=YYYY-MM-DD&to=YYYY-MM-DD`
- `GET /reports/services/top?month=YYYY-MM`
- `GET /reports/resources/occupancy?branchId=uuid&date=YYYY-MM-DD`
- `GET /reports/peak-hours?branchId=uuid&date=YYYY-MM-DD`
- `GET /reports/status-summary?month=YYYY-MM`

## Regla importante

Si Reporting está caído o Cassandra tarda, el sistema igual debe permitir crear, cancelar y atender reservas. Los reportes se actualizan después mediante outbox/reintentos.
