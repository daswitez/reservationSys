# Ejecucion local del backend

Esta guia explica como compilar, levantar, verificar, probar y detener todo el
backend de Reservas MVP.

El frontend todavia no esta implementado. El entorno actual contiene cuatro APIs,
PostgreSQL y Cassandra.

## Servicios

| Servicio | Puerto por defecto | Puerto del `.env` local |
| --- | ---: | ---: |
| Identity API | 5101 | 5201 |
| Catalog API | 5102 | 5202 |
| Booking API | 5103 | 5203 |
| Reporting API | 5104 | 5204 |
| PostgreSQL | 5432 | 55432 |
| Cassandra | 9042 | 9142 |

Los puertos efectivos siempre se toman del archivo `.env`. Este archivo no se
versiona; `.env.example` contiene los valores base.

## Requisitos

- Docker con el daemon iniciado.
- Docker Compose, ya sea `docker compose` o `docker-compose`.
- .NET SDK 10.0.301 para compilar y ejecutar pruebas fuera de Docker.
- `curl` para las verificaciones manuales.
- `jq` es opcional para extraer el JWT desde la terminal.

Verificar herramientas:

```bash
docker --version
docker compose version || docker-compose --version
./scripts/dotnet.sh --version
```

## Configuracion inicial

Desde la raiz del repositorio:

```bash
cp .env.example .env
```

Si los puertos estandar ya estan ocupados, usar por ejemplo:

```dotenv
POSTGRES_PORT=55432
CASSANDRA_PORT=9142
IDENTITY_API_PORT=5201
CATALOG_API_PORT=5202
BOOKING_API_PORT=5203
REPORTING_API_PORT=5204
```

Para desarrollo local se incluyen credenciales y una clave JWT no aptas para
produccion. No reutilizar esos valores en un entorno real.

## Levantar todo con Docker

Construir las imagenes e iniciar bases y APIs:

```bash
./scripts/compose.sh up -d --build
```

La primera ejecucion puede tardar varios minutos porque descarga imagenes,
restaura paquetes .NET e inicializa Cassandra.

Consultar el estado:

```bash
./scripts/compose.sh ps
```

Estado esperado:

- `postgres`: `Up (healthy)`.
- `cassandra`: `Up (healthy)`.
- `cassandra-init`: `Exit 0`; esto es correcto, es una tarea de inicializacion.
- Las cuatro APIs: `Up`.

Seguir todos los logs:

```bash
./scripts/compose.sh logs -f --tail=100
```

Logs de un servicio especifico:

```bash
./scripts/compose.sh logs -f identity-api
./scripts/compose.sh logs -f postgres
./scripts/compose.sh logs -f cassandra
```

## Verificar salud

Con los puertos del `.env` local de este repositorio:

```bash
curl -i http://localhost:5201/health
curl -i http://localhost:5202/health
curl -i http://localhost:5203/health
curl -i http://localhost:5204/health
```

Cada endpoint debe responder HTTP `200` y `Healthy`.

Swagger de Identity:

```txt
http://localhost:5201/swagger
```

Catalog, Booking y Reporting exponen actualmente el endpoint de salud y su
estructura base; sus operaciones de negocio se agregan en historias posteriores.

## Datos demo

PostgreSQL se inicializa con una empresa, sucursal y usuarios de prueba:

| Rol | Email | Password |
| --- | --- | --- |
| `super_admin` | `superadmin@demo.local` | `Password123` |
| `tenant_admin` | `admin@demo.local` | `Password123` |
| `client` | `cliente@demo.local` | `Password123` |

El usuario `client` es global y no pertenece al tenant demo. La empresa y sucursal
se eligen al momento de consultar disponibilidad o crear una reserva.

Tenant demo:

```txt
tenant_id: 11111111-1111-1111-1111-111111111111
slug: peluqueria-demo
```

Sucursal demo:

```txt
branch_id: 44444444-4444-4444-4444-444444444444
```

## Probar autenticacion

Login del administrador del tenant:

```bash
curl -sS http://localhost:5201/auth/login \
  -H 'Content-Type: application/json' \
  -d '{
    "email": "admin@demo.local",
    "password": "Password123"
  }'
```

Con `jq`, guardar temporalmente el JWT en una variable de shell:

```bash
TOKEN="$(curl -sS http://localhost:5201/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"admin@demo.local","password":"Password123"}' \
  | jq -r '.data.accessToken')"
```

Consultar el usuario autenticado:

```bash
curl -sS http://localhost:5201/auth/me \
  -H "Authorization: Bearer $TOKEN"
```

Validar acceso a la sucursal demo:

```bash
curl -sS http://localhost:5201/auth/access/branches/44444444-4444-4444-4444-444444444444 \
  -H "Authorization: Bearer $TOKEN"
```

## Registrar un cliente

```bash
curl -sS http://localhost:5201/auth/register-client \
  -H 'Content-Type: application/json' \
  -d '{
    "firstName": "Daniel",
    "lastName": "Mercado",
    "email": "daniel@example.com",
    "phone": "+59170000000",
    "password": "Password123"
  }'
```

El registro es independiente de cualquier negocio. El email debe ser unico en toda
la plataforma.

Listar todos los negocios activos:

```bash
curl -sS http://localhost:5201/tenants/public
```

## Administrar usuarios

Con un token de `tenant_admin`, listar los usuarios del tenant propio:

```bash
curl -sS 'http://localhost:5201/users?status=active&offset=0&limit=100' \
  -H "Authorization: Bearer $TOKEN"
```

Editar un usuario:

```bash
curl -sS -X PUT http://localhost:5201/users/USER_ID \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"firstName":"Ana","lastName":"Perez","email":"ana@example.com","phone":"+59170000000"}'
```

Bloquearlo o darlo de baja logicamente:

```bash
curl -sS -X PATCH http://localhost:5201/users/USER_ID/status \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"status":"blocked"}'

curl -sS -X DELETE http://localhost:5201/users/USER_ID \
  -H "Authorization: Bearer $TOKEN"
```

## Compilar y probar

Compilar la solucion:

```bash
./scripts/dotnet.sh build Reservas.sln --maxcpucount:1
```

Las pruebas de integracion de Identity requieren PostgreSQL en el puerto `55432`,
que es el valor configurado en el `.env` local de este repositorio:

```bash
./scripts/compose.sh up -d postgres
./scripts/dotnet.sh test tests/IdentityService.Tests/IdentityService.Tests.csproj
```

Resultado actual esperado: `43` pruebas aprobadas.

## Ejecutar una API con dotnet

Para desarrollar una API fuera de Docker, iniciar al menos su base de datos. Por
ejemplo, para Identity:

```bash
./scripts/compose.sh up -d postgres
ConnectionStrings__Postgres='Host=localhost;Port=55432;Database=reservas_mvp;Username=reservas;Password=reservas_dev;Search Path=identity' \
Jwt__Secret='dev_secret_change_me_before_production' \
Jwt__Issuer='reservas-mvp' \
Jwt__Audience='reservas-mvp-web' \
./scripts/dotnet.sh run --project services/IdentityService
```

No ejecutar simultaneamente la misma API en Docker y con `dotnet run` usando el
mismo puerto.

## Detener y reiniciar

Detener contenedores sin eliminarlos:

```bash
./scripts/compose.sh stop
```

Detener y eliminar contenedores y red, conservando datos:

```bash
./scripts/compose.sh down
```

Reiniciar todo:

```bash
./scripts/compose.sh restart
```

## Reiniciar las bases desde cero

Los archivos de `database/postgres/` se ejecutan automaticamente solo cuando se
crea un volumen PostgreSQL vacio. Lo mismo aplica a la inicializacion de Cassandra.

Para eliminar todos los datos locales y volver a ejecutar los scripts:

```bash
./scripts/compose.sh down -v
./scripts/compose.sh up -d --build
```

`down -v` es destructivo: elimina usuarios, tenants, reservas y cualquier dato
creado localmente.

## Problemas comunes

### Un puerto ya esta ocupado

Cambiar el puerto correspondiente en `.env` y volver a levantar el entorno:

```bash
./scripts/compose.sh down
./scripts/compose.sh up -d
```

### Una API esta `Up`, pero falla al consultar datos

Comprobar que PostgreSQL o Cassandra tambien esten activos:

```bash
./scripts/compose.sh ps
./scripts/compose.sh logs --tail=100 postgres cassandra
```

Una API puede seguir ejecutandose aunque su base se haya detenido.

### Los cambios de codigo no aparecen

Reconstruir las imagenes:

```bash
./scripts/compose.sh up -d --build
```

Con `docker-compose` 1.29 y versiones recientes de Docker puede aparecer un error
`KeyError: 'ContainerConfig'` al recrear una API. Los volumenes no necesitan
eliminarse; recrear solamente contenedores y red:

```bash
./scripts/compose.sh down
./scripts/compose.sh up -d --build
```

### Los cambios SQL no aparecen

Los scripts de inicializacion no se reaplican sobre volumenes existentes. Si los
datos se pueden descartar, ejecutar el reinicio destructivo con `down -v`.

### Cassandra tarda en iniciar

Revisar su healthcheck y esperar a que `cassandra-init` termine con `Exit 0`:

```bash
./scripts/compose.sh ps
./scripts/compose.sh logs -f cassandra cassandra-init
```
