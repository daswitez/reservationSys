# Frontend — HTML/CSS/JS Vanilla

Frontend sin dependencias ni build step. Se abre directo en el browser o se sirve con cualquier servidor HTTP estático.

## Cómo levantar

```bash
# Opción 1: Python (viene en Linux/macOS)
cd apps/web
python3 -m http.server 3000

# Opción 2: Node (npx)
cd apps/web
npx serve .

# Opción 3: VS Code → extensión Live Server → clic derecho en index.html → Open with Live Server
```

Luego abrir `http://localhost:3000`.

> **Importante:** los servicios .NET deben estar corriendo en los puertos 5201-5204.
> Ver `docker/` para levantar Postgres y Cassandra, y `services/` para cada microservicio.

## Estructura

```
apps/web/
├── css/
│   └── main.css          — sistema de diseño completo (variables, componentes, layouts)
├── js/
│   ├── config.js         — URLs base de los 4 microservicios
│   ├── auth.js           — token JWT, roles, helpers de auth
│   ├── api.js            — clientes tipados: identity, catalog, booking, reporting
│   ├── utils.js          — formatters, badges, mensajes de error
│   └── components.js     — toast, modal, sidebar, helpers de estado
├── index.html            — login
├── register.html         — registro de cliente
├── negocios.html         — listado público de negocios
├── reservar.html         — wizard de reserva (5 pasos)
├── mis-reservas.html     — reservas del cliente (ver + cancelar)
└── admin/
    ├── agenda.html       — agenda diaria (attend/noshow/cancel/bloqueos)
    ├── sucursales.html   — CRUD sucursales
    ├── servicios.html    — CRUD servicios
    ├── recursos.html     — CRUD recursos
    ├── horarios.html     — horarios base por recurso
    ├── bloqueos.html     — bloqueos de recursos
    └── reportes.html     — dashboard de reportes (4 tabs)
```

## Flujos principales

| Flujo | Archivos |
|-------|----------|
| Login → rol admin → panel | `index.html` → `admin/agenda.html` |
| Login → rol cliente → negocios | `index.html` → `negocios.html` |
| Reservar turno | `negocios.html` → `reservar.html` |
| Ver mis turnos | `mis-reservas.html` |
| Atender / cancelar reservas | `admin/agenda.html` |
| Ver reportes | `admin/reportes.html` (tabs: diario, servicios, ocupación, horas pico) |

## Autenticación

- JWT guardado en `localStorage` bajo clave `reservas_token`.
- Usuario guardado en `localStorage` bajo clave `reservas_user`.
- IDs de reservas propias en `reservas_ids` (array, max 50).
- Al cerrar sesión se limpian las 3 claves.
- Páginas admin verifican rol antes de cargar; redirigen a `index.html` si no tiene permiso.

## APIs backend

| Servicio | Puerto |
|----------|--------|
| IdentityService  | http://localhost:5201 |
| CatalogService   | http://localhost:5202 |
| BookingService   | http://localhost:5203 |
| ReportingService | http://localhost:5204 |

Swagger: reemplazá el puerto con `/swagger`.
