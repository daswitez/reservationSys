# Sistema de Reservas - MVP funcional

Este documento define la versión actualizada del MVP del sistema de reservas.

La base del producto sigue siendo una plataforma SaaS multi-tenant donde distintas empresas pueden registrar sucursales, servicios, recursos reservables y reservas. Los clientes son usuarios globales de la plataforma: se registran sin pertenecer a una empresa, pueden ver todos los negocios activos, elegir uno y reservar una hora. El administrador de una sucursal puede administrar recursos, horarios y bloqueos. Si una reserva se cancela, simplemente se cancela y se libera el horario.

## Cambio importante de arquitectura

En esta versión ya no se usa Cassandra como base principal del sistema operativo.

La decisión queda así:

- PostgreSQL: base transaccional principal para identidad, tenants, catálogo, reservas, disponibilidad y bloqueos.
- Cassandra: base analítica para reportes y consultas agregadas de alto volumen.

La razón es simple: el flujo principal de reservas necesita consistencia fuerte, relaciones claras, transacciones, índices únicos y validaciones de concurrencia. PostgreSQL encaja mejor ahí. Cassandra queda para lo que hace mejor en este MVP: guardar datos desnormalizados y preparados para reportes rápidos por tenant, sucursal, recurso, servicio, fecha y estado.

## Objetivo del MVP

Construir un sistema donde:

1. Una empresa/tenant pueda operar de forma aislada.
2. Cada tenant pueda tener una o varias sucursales.
3. Cada sucursal tenga servicios, recursos reservables y horarios.
4. Un cliente global pueda registrarse una sola vez, buscar negocios/sucursales/servicios y reservar en cualquiera de ellos.
5. El sistema impida doble reserva sobre el mismo recurso y rango horario.
6. Un administrador pueda bloquear un recurso cuando no esté en uso, por ejemplo una silla rota, una sala cerrada o un profesional no disponible.
7. Una reserva pueda cancelarse de forma simple, sin políticas complejas.
8. El administrador pueda ver reportes básicos de operación desde un microservicio separado.

## Regla transversal de ciclo de vida

Una historia que crea una entidad administrable no se considera completa solo con
el endpoint `POST`. Cuando corresponda al dominio debe incluir listado, detalle,
edicion y cambio de estado o baja logica. El borrado fisico se reserva para datos
sin referencias ni valor de auditoria; usuarios, tenants, sucursales, servicios,
recursos y reservas conservan historial mediante estados.

## Stack definido

- Frontend: Next.js con TypeScript.
- Backend/API: .NET Web API.
- Base transaccional: PostgreSQL.
- Base de reportes: Cassandra.
- Entorno local: Docker Compose.
- Comunicación frontend-backend: REST.
- Comunicación mínima entre servicios: REST + eventos/outbox simple para reportes.

## Microservicios del MVP

Tendremos 4 microservicios .NET.

### 1. Identity & Tenancy Service

Responsable de usuarios, roles, autenticación y tenants.

Incluye:

- Login.
- Registro de usuarios internos y clientes.
- Roles.
- Tenants/empresas.
- JWT.
- Control de acceso básico.

Base de datos: PostgreSQL, esquema `identity`.

### 2. Catalog Service

Responsable de la configuración del negocio.

Incluye:

- Sucursales.
- Servicios.
- Recursos reservables.
- Relación servicio-recurso.
- Horarios base.
- Estado de recursos.

Base de datos: PostgreSQL, esquema `catalog`.

### 3. Booking & Availability Service

Responsable del flujo operativo de reservas.

Incluye:

- Consulta de disponibilidad.
- Creación de reservas.
- Prevención de doble reserva.
- Bloqueos manuales.
- Cancelación simple.
- Marcado de atendida/no-show.
- Historial de reserva.
- Emisión de eventos para reportes.

Base de datos: PostgreSQL, esquema `booking`.

### 4. Reporting Service

Responsable de reportes y métricas operativas.

Incluye:

- Reservas por día, semana o mes.
- Reservas por sucursal.
- Reservas por servicio.
- Ocupación por recurso.
- Cancelaciones y no-shows.
- Horas pico.
- Actividad por tenant.

Base de datos: Cassandra, keyspace `reservas_reports`.

Este servicio no participa en la creación de reservas. Si Reporting se cae, el flujo de reservar debe seguir funcionando.

## Lo que NO entra en el MVP

Estas cosas quedan fuera:

- Integración con Google Calendar, Outlook u otros calendarios externos.
- Políticas de cancelación.
- Reglas de reprogramación complejas.
- Pagos.
- Membresías.
- Paquetes.
- Lista de espera.
- Servicios grupales con cupos.
- Multiidioma real.
- Recomendación inteligente de horarios.
- Arquitectura con Kafka, Kubernetes o API Gateway obligatorio.

## Principio principal

Primero debe funcionar bien el flujo:

Cliente elige negocio -> elige sucursal -> elige servicio -> ve horarios disponibles -> reserva -> el sistema bloquea el horario -> admin ve la agenda -> cliente puede cancelar -> el horario se libera -> reportes se actualizan.
