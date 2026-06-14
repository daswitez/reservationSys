# Reservas MVP

Monorepo base del sistema de reservas multi-tenant.

## Estructura

- `services/`: cuatro APIs ASP.NET Core.
- `apps/web/`: frontend Next.js pendiente de implementación.
- `database/postgres/`: scripts transaccionales de PostgreSQL.
- `database/cassandra/`: modelo de lectura para Reporting.
- `docker/`: Compose y Dockerfiles.
- `reservas_mvp_docs/`: especificación funcional y técnica.

## Requisitos

- Docker y Docker Compose.
- .NET SDK 10, o el SDK local instalado en `.tools/dotnet`.

## Inicio rapido

```bash
cp .env.example .env
./scripts/compose.sh up -d --build
./scripts/compose.sh ps
curl http://localhost:5101/health
```

Si se usa el `.env` local incluido en esta maquina, Identity responde en el puerto
`5201` en lugar de `5101`.

Compilar y probar:

```bash
./scripts/dotnet.sh build Reservas.sln --maxcpucount:1
./scripts/compose.sh up -d postgres
./scripts/dotnet.sh test tests/IdentityService.Tests/IdentityService.Tests.csproj
```

Detener:

```bash
./scripts/compose.sh down
```

Puertos estandar del proyecto (`.env.example`):

- Identity: `http://localhost:5101/health`
- Catalog: `http://localhost:5102/health`
- Booking: `http://localhost:5103/health`
- Reporting: `http://localhost:5104/health`

Esta maquina ya tenia esos puertos ocupados. El `.env` local usa:

- Identity: `http://localhost:5201/health`
- Catalog: `http://localhost:5202/health`
- Booking: `http://localhost:5203/health`
- Reporting: `http://localhost:5204/health`
- PostgreSQL: `localhost:55432`
- Cassandra: `localhost:9142`

Los puertos se pueden sobrescribir en `.env` sin cambiar el Compose.

La guia completa, incluyendo datos demo, login, logs, pruebas, reinicio de bases y
solucion de problemas, esta en
[`reservas_mvp_docs/09_DOCKER_LOCAL.md`](reservas_mvp_docs/09_DOCKER_LOCAL.md).
