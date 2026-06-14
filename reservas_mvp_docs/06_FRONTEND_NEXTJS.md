# Frontend Next.js TypeScript

El frontend debe ser simple y claro. La prioridad del MVP es que el flujo de reserva funcione perfecto.

## Estructura sugerida

```txt
apps/web/
  app/
    page.tsx
    login/
      page.tsx
    register/
      page.tsx
    negocios/
      page.tsx
    [tenantSlug]/
      page.tsx
      sucursales/
        page.tsx
      sucursales/[branchId]/
        page.tsx
      reservar/
        page.tsx
    mis-reservas/
      page.tsx
    admin/
      page.tsx
      sucursales/
        page.tsx
      servicios/
        page.tsx
      recursos/
        page.tsx
      horarios/
        page.tsx
      agenda/
        page.tsx
      bloqueos/
        page.tsx
      reportes/
        page.tsx
  components/
    layout/
    forms/
    reservation/
    admin/
    reports/
    ui/
  lib/
    api-client.ts
    auth.ts
    dates.ts
    permissions.ts
  types/
    api.ts
```

## Variables de entorno

```txt
NEXT_PUBLIC_IDENTITY_API_URL=http://localhost:5101
NEXT_PUBLIC_CATALOG_API_URL=http://localhost:5102
NEXT_PUBLIC_BOOKING_API_URL=http://localhost:5103
NEXT_PUBLIC_REPORTING_API_URL=http://localhost:5104
```

## Rutas públicas

La cuenta `client` es global. El registro no solicita negocio y la pantalla de
negocios no se filtra por el tenant del usuario.

### /negocios

Lista empresas/locales activos.

Debe mostrar:

- Nombre del negocio.
- Rubro.
- Link para ver sucursales o reservar.

Consume:

- `GET Identity /tenants/public`

### /[tenantSlug]

Landing simple del negocio.

Debe mostrar:

- Nombre de empresa.
- Sucursales activas.
- Botón Reservar.

### /[tenantSlug]/sucursales

Lista sucursales activas del tenant.

Consume:

- `GET Catalog /public/tenants/{tenantSlug}/branches`

### /[tenantSlug]/sucursales/[branchId]

Muestra detalle de sucursal y servicios disponibles.

Consume:

- `GET Catalog /public/tenants/{tenantSlug}/branches/{branchId}/services`

### /[tenantSlug]/reservar

Flujo de reserva.

Pasos:

1. Elegir sucursal.
2. Elegir servicio.
3. Elegir fecha.
4. Ver horarios disponibles.
5. Confirmar reserva.
6. Mostrar confirmación.

Consume:

- `GET Booking /availability`
- `POST Booking /reservations`

Manejo importante:

- Si la API responde `409 SLOT_ALREADY_TAKEN`, mostrar: “Ese horario acaba de ser reservado. Elegí otro horario.”
- Si responde `409 RESOURCE_BLOCKED`, mostrar: “Ese recurso no está disponible en ese horario.”

## Rutas autenticadas de cliente

### /mis-reservas

Muestra reservas del cliente.

Acciones:

- Ver próximas reservas.
- Cancelar reserva.

Consume:

- `GET Booking /my-reservations`
- `PATCH Booking /reservations/{reservationId}/cancel`

No mostrar políticas de cancelación porque no existen en el MVP.

## Panel administrativo

### /admin/sucursales

CRUD simple de sucursales.

Consume Catalog Service.

### /admin/servicios

CRUD simple de servicios.

Consume Catalog Service.

### /admin/recursos

CRUD simple de recursos reservables.

Estados visibles:

- active
- blocked
- inactive

### /admin/horarios

Configura horarios base por recurso.

Debe permitir:

- Elegir recurso.
- Elegir día de semana.
- Hora inicio.
- Hora fin.
- Guardar.

### /admin/agenda

Vista operativa diaria.

Debe mostrar:

- Reservas.
- Bloqueos.
- Filtro por recurso.
- Filtro por estado.

Acciones:

- Marcar atendida.
- Marcar no-show.
- Cancelar reserva.
- Crear bloqueo.
- Cancelar bloqueo.

Consume Booking Service.

### /admin/bloqueos

Pantalla para crear y administrar bloqueos manuales.

Caso típico:

> La silla 1 no está en uso de 13:00 a 15:00 porque está en mantenimiento.

### /admin/reportes

Pantalla que consume Reporting Service.

Cards mínimas:

- Reservas creadas hoy.
- Confirmadas.
- Canceladas.
- Atendidas.
- No-show.
- Minutos reservados.

Gráficos/tablas útiles:

- Ranking de servicios más reservados.
- Ocupación por recurso.
- Horas pico.
- Resumen por sucursal.

Consume:

- `GET Reporting /reports/daily-summary`
- `GET Reporting /reports/services/top`
- `GET Reporting /reports/resources/occupancy`
- `GET Reporting /reports/peak-hours`

Mensaje si el reporte está retrasado:

> Los reportes pueden tardar unos segundos en actualizarse.

## Estados de UI

Toda pantalla que llame APIs debe manejar:

- Loading.
- Empty state.
- Error.
- Unauthorized.
- Forbidden.
- Success toast.

## Reglas para agentes frontend

- No meter lógica crítica de disponibilidad solo en frontend.
- No confiar en slots guardados localmente.
- Después de reservar, volver a consultar o invalidar caché.
- No mostrar pantallas enterprise innecesarias.
- No agregar pagos, membresías, políticas ni integraciones externas.
