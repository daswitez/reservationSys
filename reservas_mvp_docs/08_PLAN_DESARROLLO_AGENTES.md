# Plan de desarrollo para trabajar con agentes

Este documento está pensado para dividir el trabajo entre agentes de código sin que cada uno invente su propia arquitectura.

## Reglas obligatorias para cualquier agente

Antes de codificar, cada agente debe leer:

1. `00_README_MVP.md`
2. `02_ARQUITECTURA_MICROSERVICIOS.md`
3. `03_MODELO_DATOS_POSTGRESQL.md`
4. `04_MODELO_REPORTES_CASSANDRA.md`
5. El documento específico de su tarea.

Reglas duras:

- No agregar integraciones externas.
- No agregar políticas de cancelación.
- No usar Cassandra para el flujo transaccional principal.
- No usar PostgreSQL para reportes agregados pesados del panel.
- No crear microservicios extra sin justificar.
- No poner reglas críticas solo en frontend.
- No permitir acceso cross-tenant.
- No dejar que Reporting bloquee creación/cancelación de reservas.
- No considerar completa una entidad administrable con solo su endpoint de creacion:
  implementar listado, detalle, edicion y estado o baja logica cuando aplique.

## Fase 0 - Setup del repo

Objetivo: dejar estructura base lista.

Tareas:

- Crear monorepo con carpetas `apps`, `services`, `database`, `docker` y `docs`.
- Crear solution .NET con 4 proyectos API:
  - IdentityService
  - CatalogService
  - BookingService
  - ReportingService
- Crear app Next.js con TypeScript.
- Crear docker-compose con PostgreSQL y Cassandra.
- Crear scripts SQL y CQL iniciales.

Definition of Done:

- `docker compose up` levanta PostgreSQL y Cassandra.
- Las 4 APIs levantan con `/health`.
- Web levanta en local.

## Fase 1 - PostgreSQL operativo

Objetivo: crear esquemas y tablas transaccionales.

Tareas:

- Crear `001_extensions.sql`.
- Crear `002_identity_schema.sql`.
- Crear `003_catalog_schema.sql`.
- Crear `004_booking_schema.sql`.
- Crear restricciones de solapamiento para reservas y bloqueos.
- Crear seed básico: tenant demo, sucursal demo, servicio demo, recurso demo, horario demo.

Definition of Done:

- Las tablas existen.
- Las constraints funcionan.
- No se puede crear doble reserva para el mismo recurso y rango horario.
- El seed permite consultar una sucursal, un servicio y un recurso.

Prompt recomendado para agente:

```txt
Lee los documentos del MVP. Implementa los scripts SQL de PostgreSQL en database/postgres. Usa esquemas identity, catalog y booking. Crea las constraints necesarias para evitar doble reserva por resource_id y rango horario usando tstzrange y EXCLUDE CONSTRAINT. Incluye seed demo mínimo para una peluquería con sucursal, servicio Corte 30 min, recurso Silla 1 y horario lunes a viernes 09:00-18:00.
```

## Fase 2 - Cassandra para Reporting

Objetivo: crear keyspace y tablas de reportes.

Tareas:

- Crear `001_keyspace.cql`.
- Crear `002_reports_tables.cql`.
- Crear endpoint interno de recepción de eventos.
- Implementar lógica idempotente con `report_processed_events`.

Definition of Done:

- Reporting puede recibir un evento de reserva creada.
- Reporting actualiza resumen diario, servicio mensual y ocupación por recurso.
- Si llega el mismo event_id dos veces, no duplica datos.

Prompt recomendado para agente:

```txt
Lee 04_MODELO_REPORTES_CASSANDRA.md. Implementa scripts CQL y repositorios en ReportingService para guardar reportes en Cassandra. No uses ALLOW FILTERING. Todas las consultas deben estar cubiertas por las claves primarias definidas.
```

## Fase 3 - Identity & Tenancy Service

Objetivo: login, tenants, usuarios y roles.

Tareas:

- Configurar .NET Web API.
- Conectar a PostgreSQL.
- Implementar entidades y migrations.
- Implementar hash de password.
- Implementar JWT.
- Implementar endpoints de auth.
- Implementar roles básicos.

Definition of Done:

- Se puede crear tenant.
- Se puede crear admin.
- Se puede registrar cliente.
- Se puede iniciar sesión.
- El JWT incluye claims necesarios.

## Fase 4 - Catalog Service

Objetivo: configuración operativa.

Tareas:

- Conectar a PostgreSQL.
- Implementar sucursales.
- Implementar servicios.
- Implementar recursos.
- Implementar horarios base.
- Implementar endpoints públicos para tenant/sucursal/servicios.

Definition of Done:

- El admin puede crear sucursal, servicio, recurso y horario.
- El portal público puede listar sucursales y servicios activos.

## Fase 5 - Booking & Availability Service

Objetivo: flujo completo de reserva.

Tareas:

- Conectar a PostgreSQL.
- Consultar Catalog Service para validar sucursal/servicio/recurso.
- Implementar cálculo de disponibilidad.
- Implementar creación de reserva con transacción.
- Manejar errores de constraint como `409 SLOT_ALREADY_TAKEN`.
- Implementar cancelación simple.
- Implementar bloqueos.
- Implementar historial.
- Implementar outbox.

Definition of Done:

- Cliente puede ver disponibilidad.
- Cliente puede reservar.
- No se puede doble reservar.
- Admin puede bloquear recurso.
- Cliente o admin puede cancelar.
- Se generan eventos outbox.

## Fase 6 - Outbox Worker hacia Reporting

Objetivo: enviar eventos de Booking a Reporting.

Tareas:

- Crear background worker dentro de BookingService.
- Leer eventos `PENDING`.
- Llamar `POST Reporting /internal/report-events`.
- Marcar eventos como `PROCESSED`.
- Reintentar si falla.

Definition of Done:

- Al crear reserva, aparece evento pendiente.
- Worker lo envía a Reporting.
- Reporting actualiza Cassandra.
- El evento queda procesado.

## Fase 7 - Frontend público

Objetivo: flujo de reserva del cliente.

Tareas:

- Crear páginas públicas.
- Crear login/register.
- Crear flujo de reserva.
- Manejar errores de conflicto.
- Crear mis reservas.

Definition of Done:

- Usuario puede registrarse.
- Usuario puede reservar.
- Usuario puede cancelar.
- UI muestra errores entendibles.

## Fase 8 - Frontend administrativo

Objetivo: panel de administración.

Tareas:

- CRUD sucursales.
- CRUD servicios.
- CRUD recursos.
- CRUD horarios.
- Agenda diaria.
- Bloqueos.
- Reportes.

Definition of Done:

- Admin puede configurar negocio.
- Admin puede ver agenda.
- Admin puede bloquear recurso.
- Admin puede ver reportes desde Cassandra.

## Fase 9 - Pruebas mínimas

Pruebas obligatorias:

- Login correcto/incorrecto.
- Cliente no entra al panel admin.
- Crear reserva válida.
- Crear doble reserva simultánea falla.
- Crear bloqueo impide reserva.
- Cancelar reserva libera horario.
- Reporting recibe evento y actualiza reporte.
- Reporte no rompe si Cassandra tarda.

## Orden recomendado de agentes

1. Agente setup repo + Docker.
2. Agente PostgreSQL schema.
3. Agente Cassandra reporting schema.
4. Agente Identity.
5. Agente Catalog.
6. Agente Booking.
7. Agente Reporting.
8. Agente Frontend público.
9. Agente Frontend admin.
10. Agente QA/testing.
