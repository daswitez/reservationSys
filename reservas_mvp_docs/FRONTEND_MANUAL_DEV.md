# Manual de desarrollo Frontend — Next.js 15

Sistema de reservas MVP. Este documento es la referencia completa para implementar
el frontend: setup, tipos TypeScript exactos del backend, capa API, autenticación,
rutas, formularios y componentes.

---

## 1. Stack

| Herramienta | Versión | Rol |
|------------|---------|-----|
| Next.js | 15 | Framework (App Router) |
| TypeScript | 5 | Lenguaje |
| Tailwind CSS | 3 | Estilos |
| shadcn/ui | latest | Componentes base |
| TanStack Query | v5 | Server state / caché |
| React Hook Form | 7 | Formularios |
| Zod | 3 | Validación de schemas |
| Zustand | 4 | Client state (auth) |
| date-fns | 3 | Manejo de fechas |
| lucide-react | latest | Iconos |

---

## 2. Inicialización del proyecto

```bash
npx create-next-app@latest reservas-web \
  --typescript \
  --tailwind \
  --app \
  --src-dir \
  --import-alias "@/*"

cd reservas-web

# Dependencias de producción
npm install \
  @tanstack/react-query \
  @tanstack/react-query-devtools \
  react-hook-form \
  @hookform/resolvers \
  zod \
  zustand \
  date-fns \
  lucide-react

# shadcn/ui — instalar componentes uno a uno según se necesiten
npx shadcn@latest init
# Cuando pregunte el tema, elegir "default" / "slate"

# Componentes shadcn usados en este proyecto
npx shadcn@latest add button card input label select \
  badge dialog alert toast table tabs skeleton \
  form dropdown-menu avatar sheet separator
```

---

## 3. Variables de entorno

Crear `.env.local` en la raíz del proyecto Next.js:

```env
# Backends — puertos del entorno Docker local
NEXT_PUBLIC_IDENTITY_URL=http://localhost:5201
NEXT_PUBLIC_CATALOG_URL=http://localhost:5202
NEXT_PUBLIC_BOOKING_URL=http://localhost:5203
NEXT_PUBLIC_REPORTING_URL=http://localhost:5204
```

> `NEXT_PUBLIC_` hace que las variables sean visibles en el browser. Si en el futuro
> se agregan llamadas desde Server Components o Route Handlers, crear las mismas
> variables sin el prefijo para uso exclusivo de servidor.

---

## 4. Estructura de carpetas

```
src/
├── app/
│   ├── layout.tsx                   # Root layout: Providers
│   ├── page.tsx                     # / → redirect a /negocios
│   │
│   ├── login/
│   │   └── page.tsx
│   ├── registro/
│   │   └── page.tsx
│   │
│   ├── negocios/
│   │   └── page.tsx                 # Lista tenants públicos
│   │
│   ├── [tenantSlug]/
│   │   ├── page.tsx                 # Landing del negocio
│   │   └── reservar/
│   │       └── page.tsx             # Flujo de reserva (wizard)
│   │
│   ├── mis-reservas/
│   │   └── page.tsx                 # Solo rol client
│   │
│   └── admin/
│       ├── layout.tsx               # Sidebar admin
│       ├── page.tsx                 # Redirect a /admin/agenda
│       ├── agenda/
│       │   └── page.tsx
│       ├── reservas/
│       │   └── page.tsx
│       ├── bloqueos/
│       │   └── page.tsx
│       ├── sucursales/
│       │   └── page.tsx
│       ├── servicios/
│       │   └── page.tsx
│       ├── recursos/
│       │   └── page.tsx
│       ├── horarios/
│       │   └── page.tsx
│       └── reportes/
│           └── page.tsx
│
├── components/
│   ├── ui/                          # Re-exports de shadcn
│   ├── layout/
│   │   ├── Navbar.tsx
│   │   ├── AdminSidebar.tsx
│   │   └── RoleGuard.tsx
│   ├── auth/
│   │   ├── LoginForm.tsx
│   │   └── RegisterForm.tsx
│   ├── booking/
│   │   ├── AvailabilityPicker.tsx
│   │   ├── ReservationCard.tsx
│   │   ├── StatusBadge.tsx
│   │   └── BookingWizard.tsx
│   ├── agenda/
│   │   ├── AgendaDay.tsx
│   │   ├── AgendaReservationRow.tsx
│   │   └── AgendaBlockRow.tsx
│   ├── admin/
│   │   ├── BranchForm.tsx
│   │   ├── ServiceForm.tsx
│   │   ├── ResourceForm.tsx
│   │   ├── ScheduleForm.tsx
│   │   └── BlockForm.tsx
│   └── reports/
│       ├── DailySummaryCards.tsx
│       ├── ServicesRankingTable.tsx
│       ├── OccupancyTable.tsx
│       └── PeakHoursChart.tsx
│
├── lib/
│   ├── api/
│   │   ├── fetcher.ts               # Base HTTP client
│   │   ├── identity.ts
│   │   ├── catalog.ts
│   │   ├── booking.ts
│   │   └── reporting.ts
│   ├── auth-store.ts                # Zustand store
│   ├── query-client.ts              # TanStack Query config
│   └── utils.ts                    # cn(), formatDate(), etc.
│
└── types/
    ├── api.ts                       # ApiResponse<T>, ApiError
    ├── identity.ts
    ├── catalog.ts
    ├── booking.ts
    └── reporting.ts
```

---

## 5. Tipos TypeScript

Estos tipos mapean exactamente los DTOs del backend.

### `src/types/api.ts`

```ts
export interface ApiResponse<T> {
  success: boolean;
  data: T | null;
  error: ApiError | null;
}

export interface ApiError {
  code: string;
  message: string;
  details?: unknown;
}
```

### `src/types/identity.ts`

```ts
export interface LoginResponse {
  accessToken: string;
  userId: string;
  email: string;
  role: UserRole;
  tenantId: string | null;
  branchId: string | null;
}

export type UserRole = 'super_admin' | 'tenant_admin' | 'branch_admin' | 'client';

export interface User {
  userId: string;
  tenantId: string | null;
  firstName: string;
  lastName: string;
  email: string;
  phone: string | null;
  role: UserRole;
  status: 'active' | 'inactive' | 'blocked';
  createdAt: string;
}

export interface Tenant {
  tenantId: string;
  name: string;
  slug: string;
  mainCategory: string;
  timezone: string;
  status: 'active' | 'inactive';
}
```

### `src/types/catalog.ts`

```ts
export interface Branch {
  branchId: string;
  tenantId: string;
  name: string;
  address: string;
  phone: string | null;
  emailContact: string | null;
  timezone: string;
  status: 'active' | 'inactive';
}

export interface Service {
  serviceId: string;
  tenantId: string;
  name: string;
  description: string | null;
  durationMinutes: number;
  referencePrice: number;
  modality: string;
  status: 'active' | 'inactive';
}

export interface Resource {
  resourceId: string;
  tenantId: string;
  branchId: string;
  name: string;
  resourceType: string;
  status: 'active' | 'inactive' | 'blocked';
}

export interface ResourceSchedule {
  scheduleId: string;
  resourceId: string;
  dayOfWeek: number;        // 0 = lunes … 6 = domingo
  startTime: string;        // "HH:mm:ss"
  endTime: string;
  status: 'active' | 'inactive';
}
```

### `src/types/booking.ts`

```ts
export type ReservationStatus = 'CONFIRMED' | 'CANCELLED' | 'ATTENDED' | 'NO_SHOW';
export type BlockStatus = 'ACTIVE' | 'CANCELLED';

export interface AvailabilitySlot {
  resourceId: string;
  resourceName: string;
  startAt: string;          // ISO 8601 con timezone
  endAt: string;
}

export interface AvailabilityResponse {
  branchId: string;
  serviceId: string;
  date: string;
  slotMinutes: number;
  availableSlots: AvailabilitySlot[];
}

export interface Reservation {
  reservationId: string;
  tenantId: string;
  branchId: string;
  serviceId: string;
  resourceId: string;
  clientUserId: string;
  status: ReservationStatus;
  startAt: string;
  endAt: string;
  notes: string | null;
  createdAt: string;
}

export interface ReservationHistoryItem {
  action: string;
  previousStatus: string | null;
  newStatus: string | null;
  reason: string | null;
  userId: string | null;
  createdAt: string;
}

export interface ReservationSearchItem extends Reservation {
  history: ReservationHistoryItem[];
}

export interface ResourceBlock {
  blockId: string;
  tenantId: string;
  branchId: string;
  resourceId: string;
  startAt: string;
  endAt: string;
  reason: string | null;
  blockType: string;
  status: BlockStatus;
  createdByUserId: string;
  createdAt: string;
}

export interface AgendaReservationItem {
  reservationId: string;
  resourceId: string;
  serviceId: string;
  clientUserId: string;
  status: ReservationStatus;
  startAt: string;
  endAt: string;
  notes: string | null;
}

export interface AgendaBlockItem {
  blockId: string;
  resourceId: string;
  reason: string | null;
  blockType: string;
  status: BlockStatus;
  startAt: string;
  endAt: string;
}

export interface AgendaResponse {
  date: string;
  branchId: string;
  reservations: AgendaReservationItem[];
  blocks: AgendaBlockItem[];
}
```

### `src/types/reporting.ts`

```ts
export type DataStatus = 'OK' | 'PENDING_SYNC';

export interface DailySummary {
  tenantId: string;
  branchId: string | null;
  branchName: string | null;
  date: string;
  totalCreated: number;
  totalConfirmed: number;
  totalCancelled: number;
  totalAttended: number;
  totalNoShow: number;
  totalReservedMinutes: number;
  updatedAt: string | null;
  dataStatus: DataStatus;
}

export interface ResourceOccupancyItem {
  resourceId: string;
  resourceName: string;
  resourceType: string;
  date: string;
  totalReservations: number;
  totalAttended: number;
  totalCancelled: number;
  totalNoShow: number;
  reservedMinutes: number;
  blockedMinutes: number;
  updatedAt: string | null;
}

export interface ServiceSummaryItem {
  rank: number;
  serviceId: string;
  serviceName: string;
  totalCreated: number;
  totalCancelled: number;
  totalAttended: number;
  totalNoShow: number;
  totalReservedMinutes: number;
}

export interface ServiceRankingResponse {
  periodFrom: string;
  periodTo: string;
  services: ServiceSummaryItem[];
}

export interface PeakHourItem {
  hourOfDay: number;
  totalCreated: number;
  totalAttended: number;
  totalCancelled: number;
}

export interface PeakHoursResponse {
  branchId: string;
  periodFrom: string;
  periodTo: string;
  hours: PeakHourItem[];
}
```

---

## 6. Capa API

### `src/lib/api/fetcher.ts`

```ts
import { ApiResponse } from '@/types/api';

type FetchOptions = RequestInit & {
  token?: string;
  idempotencyKey?: string;
};

export class ApiError extends Error {
  constructor(
    public status: number,
    public code: string,
    message: string,
    public details?: unknown,
  ) {
    super(message);
  }
}

export async function apiFetch<T>(
  url: string,
  options: FetchOptions = {},
): Promise<T> {
  const { token, idempotencyKey, ...init } = options;

  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    ...(init.headers as Record<string, string>),
  };

  if (token) headers['Authorization'] = `Bearer ${token}`;
  if (idempotencyKey) headers['Idempotency-Key'] = idempotencyKey;

  const res = await fetch(url, { ...init, headers });

  // 204 No Content
  if (res.status === 204) return undefined as T;

  const body: ApiResponse<T> = await res.json();

  if (!res.ok || !body.success) {
    throw new ApiError(
      res.status,
      body.error?.code ?? 'UNKNOWN_ERROR',
      body.error?.message ?? 'Error desconocido',
      body.error?.details,
    );
  }

  return body.data as T;
}
```

### `src/lib/api/identity.ts`

```ts
import { apiFetch } from './fetcher';
import { LoginResponse, User, Tenant } from '@/types/identity';

const BASE = process.env.NEXT_PUBLIC_IDENTITY_URL;

export const identityApi = {
  login: (email: string, password: string) =>
    apiFetch<LoginResponse>(`${BASE}/auth/login`, {
      method: 'POST',
      body: JSON.stringify({ email, password }),
    }),

  registerClient: (data: {
    firstName: string; lastName: string;
    email: string; phone: string; password: string;
  }) =>
    apiFetch<User>(`${BASE}/auth/register-client`, {
      method: 'POST',
      body: JSON.stringify(data),
    }),

  me: (token: string) =>
    apiFetch<User>(`${BASE}/auth/me`, { token }),

  getPublicTenants: () =>
    apiFetch<Tenant[]>(`${BASE}/tenants/public`),

  getUsers: (token: string, params: Record<string, string> = {}) => {
    const qs = new URLSearchParams(params).toString();
    return apiFetch<User[]>(`${BASE}/users?${qs}`, { token });
  },

  updateUserStatus: (token: string, userId: string, status: string) =>
    apiFetch<User>(`${BASE}/users/${userId}/status`, {
      method: 'PATCH',
      token,
      body: JSON.stringify({ status }),
    }),
};
```

### `src/lib/api/catalog.ts`

```ts
import { apiFetch } from './fetcher';
import { Branch, Service, Resource, ResourceSchedule } from '@/types/catalog';

const BASE = process.env.NEXT_PUBLIC_CATALOG_URL;

export const catalogApi = {
  // ── Público ──────────────────────────────────────────────────────────────
  getPublicBranches: (tenantSlug: string) =>
    apiFetch<Branch[]>(`${BASE}/public/tenants/${tenantSlug}/branches`),

  getPublicBranchServices: (tenantSlug: string, branchId: string) =>
    apiFetch<Service[]>(
      `${BASE}/public/tenants/${tenantSlug}/branches/${branchId}/services`,
    ),

  getPublicServices: (tenantSlug: string) =>
    apiFetch<Service[]>(`${BASE}/public/tenants/${tenantSlug}/services`),

  // ── Sucursales ────────────────────────────────────────────────────────────
  getBranches: (token: string, params: Record<string, string> = {}) => {
    const qs = new URLSearchParams(params).toString();
    return apiFetch<Branch[]>(`${BASE}/branches?${qs}`, { token });
  },

  getBranch: (token: string, branchId: string) =>
    apiFetch<Branch>(`${BASE}/branches/${branchId}`, { token }),

  createBranch: (token: string, data: Partial<Branch>) =>
    apiFetch<Branch>(`${BASE}/branches`, {
      method: 'POST', token, body: JSON.stringify(data),
    }),

  updateBranch: (token: string, branchId: string, data: Partial<Branch>) =>
    apiFetch<Branch>(`${BASE}/branches/${branchId}`, {
      method: 'PUT', token, body: JSON.stringify(data),
    }),

  updateBranchStatus: (token: string, branchId: string, status: string) =>
    apiFetch<Branch>(`${BASE}/branches/${branchId}/status`, {
      method: 'PATCH', token, body: JSON.stringify({ status }),
    }),

  // ── Servicios ─────────────────────────────────────────────────────────────
  getServices: (token: string, params: Record<string, string> = {}) => {
    const qs = new URLSearchParams(params).toString();
    return apiFetch<Service[]>(`${BASE}/services?${qs}`, { token });
  },

  createService: (token: string, data: Partial<Service>) =>
    apiFetch<Service>(`${BASE}/services`, {
      method: 'POST', token, body: JSON.stringify(data),
    }),

  updateService: (token: string, serviceId: string, data: Partial<Service>) =>
    apiFetch<Service>(`${BASE}/services/${serviceId}`, {
      method: 'PUT', token, body: JSON.stringify(data),
    }),

  linkServiceToBranch: (token: string, branchId: string, serviceId: string) =>
    apiFetch<void>(
      `${BASE}/branches/${branchId}/services/${serviceId}`,
      { method: 'POST', token },
    ),

  // ── Recursos ──────────────────────────────────────────────────────────────
  getResources: (token: string, params: Record<string, string> = {}) => {
    const qs = new URLSearchParams(params).toString();
    return apiFetch<Resource[]>(`${BASE}/resources?${qs}`, { token });
  },

  createResource: (token: string, data: Partial<Resource>) =>
    apiFetch<Resource>(`${BASE}/resources`, {
      method: 'POST', token, body: JSON.stringify(data),
    }),

  updateResourceStatus: (token: string, resourceId: string, status: string) =>
    apiFetch<Resource>(`${BASE}/resources/${resourceId}/status`, {
      method: 'PATCH', token, body: JSON.stringify({ status }),
    }),

  // ── Horarios ──────────────────────────────────────────────────────────────
  getSchedules: (token: string, resourceId: string) =>
    apiFetch<ResourceSchedule[]>(
      `${BASE}/resource-schedules?resourceId=${resourceId}`, { token },
    ),

  createSchedule: (token: string, data: Partial<ResourceSchedule>) =>
    apiFetch<ResourceSchedule>(`${BASE}/resource-schedules`, {
      method: 'POST', token, body: JSON.stringify(data),
    }),

  updateScheduleStatus: (token: string, scheduleId: string, status: string) =>
    apiFetch<ResourceSchedule>(
      `${BASE}/resource-schedules/${scheduleId}/status`,
      { method: 'PATCH', token, body: JSON.stringify({ status }) },
    ),
};
```

### `src/lib/api/booking.ts`

```ts
import { apiFetch } from './fetcher';
import {
  AvailabilityResponse, Reservation, ReservationSearchItem,
  ResourceBlock, AgendaResponse,
} from '@/types/booking';

const BASE = process.env.NEXT_PUBLIC_BOOKING_URL;

export const bookingApi = {
  getAvailability: (params: {
    tenantSlug: string; branchId: string;
    serviceId: string; date: string;
  }) => {
    const qs = new URLSearchParams(params).toString();
    return apiFetch<AvailabilityResponse>(`${BASE}/availability?${qs}`);
  },

  createReservation: (
    token: string,
    data: {
      branchId: string; serviceId: string; resourceId?: string;
      startAt: string; notes?: string;
    },
  ) =>
    apiFetch<Reservation>(`${BASE}/reservations`, {
      method: 'POST',
      token,
      idempotencyKey: crypto.randomUUID(),
      body: JSON.stringify(data),
    }),

  getReservation: (token: string, reservationId: string) =>
    apiFetch<Reservation>(`${BASE}/reservations/${reservationId}`, { token }),

  cancelReservation: (token: string, reservationId: string, reason?: string) =>
    apiFetch<Reservation>(
      `${BASE}/reservations/${reservationId}/cancel`,
      { method: 'PATCH', token, body: JSON.stringify({ reason }) },
    ),

  attendReservation: (token: string, reservationId: string) =>
    apiFetch<Reservation>(
      `${BASE}/reservations/${reservationId}/attend`,
      { method: 'PATCH', token },
    ),

  noShowReservation: (token: string, reservationId: string) =>
    apiFetch<Reservation>(
      `${BASE}/reservations/${reservationId}/no-show`,
      { method: 'PATCH', token },
    ),

  searchReservations: (token: string, params: Record<string, string> = {}) => {
    const qs = new URLSearchParams(params).toString();
    return apiFetch<ReservationSearchItem[]>(
      `${BASE}/admin/reservations?${qs}`, { token },
    );
  },

  getAgenda: (
    token: string,
    params: { branchId: string; date: string; resourceId?: string; status?: string },
  ) => {
    const qs = new URLSearchParams(
      Object.fromEntries(
        Object.entries(params).filter(([, v]) => v !== undefined),
      ) as Record<string, string>,
    ).toString();
    return apiFetch<AgendaResponse>(`${BASE}/admin/agenda?${qs}`, { token });
  },

  createBlock: (
    token: string,
    data: {
      branchId: string; resourceId: string;
      startAt: string; endAt: string;
      reason?: string; blockType: string;
    },
  ) =>
    apiFetch<ResourceBlock>(`${BASE}/resource-blocks`, {
      method: 'POST', token, body: JSON.stringify(data),
    }),

  cancelBlock: (token: string, blockId: string) =>
    apiFetch<ResourceBlock>(
      `${BASE}/resource-blocks/${blockId}/cancel`,
      { method: 'PATCH', token },
    ),
};
```

### `src/lib/api/reporting.ts`

```ts
import { apiFetch } from './fetcher';
import {
  DailySummary, ResourceOccupancyItem,
  ServiceRankingResponse, PeakHoursResponse,
} from '@/types/reporting';

const BASE = process.env.NEXT_PUBLIC_REPORTING_URL;

export const reportingApi = {
  getDailySummary: (token: string, params: Record<string, string>) => {
    const qs = new URLSearchParams(params).toString();
    return apiFetch<DailySummary>(`${BASE}/reports/daily-summary?${qs}`, { token });
  },

  getResourceOccupancy: (token: string, params: Record<string, string>) => {
    const qs = new URLSearchParams(params).toString();
    return apiFetch<ResourceOccupancyItem[]>(
      `${BASE}/reports/resources/occupancy?${qs}`, { token },
    );
  },

  getTopServices: (token: string, params: Record<string, string>) => {
    const qs = new URLSearchParams(params).toString();
    return apiFetch<ServiceRankingResponse>(
      `${BASE}/reports/services/top?${qs}`, { token },
    );
  },

  getPeakHours: (token: string, params: Record<string, string>) => {
    const qs = new URLSearchParams(params).toString();
    return apiFetch<PeakHoursResponse>(
      `${BASE}/reports/peak-hours?${qs}`, { token },
    );
  },
};
```

---

## 7. Autenticación

### `src/lib/auth-store.ts` — Zustand

```ts
import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import { UserRole } from '@/types/identity';

interface AuthState {
  token: string | null;
  userId: string | null;
  email: string | null;
  role: UserRole | null;
  tenantId: string | null;
  branchId: string | null;

  setAuth: (payload: {
    token: string; userId: string; email: string;
    role: UserRole; tenantId: string | null; branchId: string | null;
  }) => void;

  clearAuth: () => void;
  isAuthenticated: () => boolean;
  isAdmin: () => boolean;
}

export const useAuth = create<AuthState>()(
  persist(
    (set, get) => ({
      token: null,
      userId: null,
      email: null,
      role: null,
      tenantId: null,
      branchId: null,

      setAuth: (payload) => set(payload),

      clearAuth: () =>
        set({
          token: null, userId: null, email: null,
          role: null, tenantId: null, branchId: null,
        }),

      isAuthenticated: () => get().token !== null,

      isAdmin: () =>
        ['super_admin', 'tenant_admin', 'branch_admin'].includes(
          get().role ?? '',
        ),
    }),
    {
      name: 'reservas-auth',   // clave en localStorage
      partialize: (state) => ({
        // NO persistir el token en producción — usar httpOnly cookie
        // Para MVP está bien en localStorage
        token: state.token,
        userId: state.userId,
        email: state.email,
        role: state.role,
        tenantId: state.tenantId,
        branchId: state.branchId,
      }),
    },
  ),
);
```

### `src/middleware.ts` — Protección de rutas

```ts
import { NextResponse } from 'next/server';
import type { NextRequest } from 'next/server';

// Rutas que necesitan autenticación
const AUTH_ROUTES = ['/mis-reservas'];
const ADMIN_ROUTES = ['/admin'];

export function middleware(request: NextRequest) {
  const { pathname } = request.nextUrl;

  // El token se lee desde cookie si se implementa SSR auth
  // Para MVP con localStorage el guard se hace en el componente
  // Este middleware solo redirige si no hay cookie de sesión
  const token = request.cookies.get('reservas-token')?.value;

  const needsAuth =
    AUTH_ROUTES.some((r) => pathname.startsWith(r)) ||
    ADMIN_ROUTES.some((r) => pathname.startsWith(r));

  if (needsAuth && !token) {
    return NextResponse.redirect(new URL('/login', request.url));
  }

  return NextResponse.next();
}

export const config = {
  matcher: ['/mis-reservas/:path*', '/admin/:path*'],
};
```

### `src/components/layout/RoleGuard.tsx`

Componente client-side que valida rol. Úsalo cuando el middleware de cookies no
sea suficiente (usuarios con token pero rol incorrecto).

```tsx
'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useAuth } from '@/lib/auth-store';
import { UserRole } from '@/types/identity';

interface Props {
  allowedRoles: UserRole[];
  children: React.ReactNode;
  redirectTo?: string;
}

export function RoleGuard({ allowedRoles, children, redirectTo = '/login' }: Props) {
  const { role, isAuthenticated } = useAuth();
  const router = useRouter();

  useEffect(() => {
    if (!isAuthenticated() || !role || !allowedRoles.includes(role)) {
      router.replace(redirectTo);
    }
  }, [role, isAuthenticated, allowedRoles, redirectTo, router]);

  if (!isAuthenticated() || !role || !allowedRoles.includes(role)) {
    return null;
  }

  return <>{children}</>;
}
```

---

## 8. Providers (root layout)

### `src/lib/query-client.ts`

```ts
import { QueryClient } from '@tanstack/react-query';

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,       // 30 segundos en caché antes de refetch
      retry: 1,
      refetchOnWindowFocus: false,
    },
  },
});
```

### `src/app/layout.tsx`

```tsx
import type { Metadata } from 'next';
import { Inter } from 'next/font/google';
import { Providers } from './providers';
import './globals.css';

const inter = Inter({ subsets: ['latin'] });

export const metadata: Metadata = {
  title: 'Reservas MVP',
  description: 'Sistema de reservas',
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="es">
      <body className={inter.className}>
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
```

### `src/app/providers.tsx`

```tsx
'use client';

import { QueryClientProvider } from '@tanstack/react-query';
import { ReactQueryDevtools } from '@tanstack/react-query-devtools';
import { queryClient } from '@/lib/query-client';
import { Toaster } from '@/components/ui/toaster';

export function Providers({ children }: { children: React.ReactNode }) {
  return (
    <QueryClientProvider client={queryClient}>
      {children}
      <Toaster />
      <ReactQueryDevtools initialIsOpen={false} />
    </QueryClientProvider>
  );
}
```

---

## 9. Páginas — detalle completo

### 9.1 `/login`

**Acceso:** público  
**Componente principal:** `LoginForm`  
**API:** `POST /auth/login` → Identity

```tsx
// src/app/login/page.tsx
import { LoginForm } from '@/components/auth/LoginForm';

export default function LoginPage() {
  return (
    <main className="flex min-h-screen items-center justify-center">
      <LoginForm />
    </main>
  );
}
```

```tsx
// src/components/auth/LoginForm.tsx
'use client';

import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useRouter } from 'next/navigation';
import { useAuth } from '@/lib/auth-store';
import { identityApi } from '@/lib/api/identity';
import { ApiError } from '@/lib/api/fetcher';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { useToast } from '@/components/ui/use-toast';

const schema = z.object({
  email: z.string().email('Email inválido'),
  password: z.string().min(1, 'Contraseña requerida'),
});

type FormValues = z.infer<typeof schema>;

export function LoginForm() {
  const { setAuth } = useAuth();
  const router = useRouter();
  const { toast } = useToast();

  const form = useForm<FormValues>({ resolver: zodResolver(schema) });

  const onSubmit = async (values: FormValues) => {
    try {
      const data = await identityApi.login(values.email, values.password);
      setAuth({
        token: data.accessToken,
        userId: data.userId,
        email: data.email,
        role: data.role,
        tenantId: data.tenantId,
        branchId: data.branchId,
      });
      // Redirigir según rol
      if (data.role === 'client') router.push('/mis-reservas');
      else router.push('/admin/agenda');
    } catch (err) {
      const message =
        err instanceof ApiError ? err.message : 'Error al iniciar sesión';
      toast({ title: 'Error', description: message, variant: 'destructive' });
    }
  };

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="w-full max-w-sm space-y-4">
      <h1 className="text-2xl font-bold">Iniciar sesión</h1>

      <div className="space-y-1">
        <Label>Email</Label>
        <Input type="email" {...form.register('email')} />
        {form.formState.errors.email && (
          <p className="text-sm text-red-500">{form.formState.errors.email.message}</p>
        )}
      </div>

      <div className="space-y-1">
        <Label>Contraseña</Label>
        <Input type="password" {...form.register('password')} />
      </div>

      <Button type="submit" className="w-full" disabled={form.formState.isSubmitting}>
        {form.formState.isSubmitting ? 'Ingresando…' : 'Ingresar'}
      </Button>
    </form>
  );
}
```

---

### 9.2 `/registro`

**Acceso:** público  
**API:** `POST /auth/register-client` → Identity  
**Schema Zod:**

```ts
const registerSchema = z.object({
  firstName: z.string().min(1),
  lastName: z.string().min(1),
  email: z.string().email(),
  phone: z.string().min(8),
  password: z.string().min(8, 'Mínimo 8 caracteres'),
  confirmPassword: z.string(),
}).refine((d) => d.password === d.confirmPassword, {
  message: 'Las contraseñas no coinciden',
  path: ['confirmPassword'],
});
```

Después de registrar: redirigir a `/login` con mensaje de éxito en toast.

---

### 9.3 `/negocios`

**Acceso:** público  
**API:** `GET /tenants/public` → Identity

```tsx
// src/app/negocios/page.tsx
import { identityApi } from '@/lib/api/identity';
import Link from 'next/link';

// Server Component — se puede hacer fetch directo
export default async function NegociosPage() {
  const tenants = await identityApi.getPublicTenants();

  return (
    <main className="container py-8">
      <h1 className="text-2xl font-bold mb-6">Negocios disponibles</h1>
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
        {tenants.map((t) => (
          <Link key={t.tenantId} href={`/${t.slug}`}>
            <div className="border rounded-lg p-4 hover:shadow-md transition-shadow">
              <h2 className="font-semibold text-lg">{t.name}</h2>
              <p className="text-sm text-muted-foreground">{t.mainCategory}</p>
            </div>
          </Link>
        ))}
      </div>
    </main>
  );
}
```

---

### 9.4 `/[tenantSlug]/reservar` — Wizard de reserva

**Acceso:** requiere rol `client`  
**Flujo de 4 pasos:**

```
PASO 1: Elegir sucursal
  └─ GET /public/tenants/{slug}/branches → Catalog

PASO 2: Elegir servicio
  └─ GET /public/tenants/{slug}/branches/{branchId}/services → Catalog

PASO 3: Elegir fecha y horario
  └─ GET /availability?tenantSlug&branchId&serviceId&date → Booking

PASO 4: Confirmar y crear reserva
  └─ POST /reservations → Booking
```

```tsx
// src/components/booking/BookingWizard.tsx
'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { useAuth } from '@/lib/auth-store';
import { catalogApi } from '@/lib/api/catalog';
import { bookingApi } from '@/lib/api/booking';
import { useQuery, useMutation } from '@tanstack/react-query';
import { ApiError } from '@/lib/api/fetcher';
import { format } from 'date-fns';
import { Branch, Service } from '@/types/catalog';
import { AvailabilitySlot } from '@/types/booking';

interface WizardState {
  step: 1 | 2 | 3 | 4;
  branch: Branch | null;
  service: Service | null;
  date: string;                    // yyyy-MM-dd
  slot: AvailabilitySlot | null;
}

export function BookingWizard({ tenantSlug }: { tenantSlug: string }) {
  const { token } = useAuth();
  const router = useRouter();
  const [state, setState] = useState<WizardState>({
    step: 1, branch: null, service: null, date: '', slot: null,
  });
  const [error, setError] = useState<string | null>(null);

  // Paso 1 — Sucursales
  const branchesQuery = useQuery({
    queryKey: ['public-branches', tenantSlug],
    queryFn: () => catalogApi.getPublicBranches(tenantSlug),
  });

  // Paso 2 — Servicios de la sucursal
  const servicesQuery = useQuery({
    queryKey: ['public-branch-services', tenantSlug, state.branch?.branchId],
    queryFn: () =>
      catalogApi.getPublicBranchServices(tenantSlug, state.branch!.branchId),
    enabled: !!state.branch,
  });

  // Paso 3 — Disponibilidad
  const availabilityQuery = useQuery({
    queryKey: ['availability', state.branch?.branchId, state.service?.serviceId, state.date],
    queryFn: () =>
      bookingApi.getAvailability({
        tenantSlug,
        branchId: state.branch!.branchId,
        serviceId: state.service!.serviceId,
        date: state.date,
      }),
    enabled: !!state.branch && !!state.service && !!state.date,
  });

  // Paso 4 — Crear reserva
  const createMutation = useMutation({
    mutationFn: () =>
      bookingApi.createReservation(token!, {
        branchId: state.branch!.branchId,
        serviceId: state.service!.serviceId,
        resourceId: state.slot!.resourceId,
        startAt: state.slot!.startAt,
      }),
    onSuccess: (reservation) => {
      router.push(`/mis-reservas?nuevo=${reservation.reservationId}`);
    },
    onError: (err) => {
      if (err instanceof ApiError) {
        if (err.code === 'SLOT_ALREADY_TAKEN')
          setError('Ese horario acaba de ser reservado. Elegí otro.');
        else if (err.code === 'RESOURCE_BLOCKED')
          setError('Ese recurso no está disponible en ese horario.');
        else
          setError(err.message);
      }
    },
  });

  // UI simplificada — cada paso renderiza su sección
  // ... (ver implementación completa en el paso a paso de cada sección)

  return (
    <div className="max-w-lg mx-auto space-y-6">
      {/* Progress indicator */}
      <div className="flex gap-2">
        {[1, 2, 3, 4].map((s) => (
          <div
            key={s}
            className={`h-1 flex-1 rounded ${state.step >= s ? 'bg-primary' : 'bg-muted'}`}
          />
        ))}
      </div>

      {error && (
        <div className="bg-red-50 border border-red-200 rounded p-3 text-sm text-red-700">
          {error}
        </div>
      )}

      {/* Paso 1 */}
      {state.step === 1 && (
        <div>
          <h2 className="font-semibold mb-3">¿En qué sucursal?</h2>
          {branchesQuery.isLoading && <p>Cargando…</p>}
          <div className="grid gap-2">
            {branchesQuery.data?.map((b) => (
              <button
                key={b.branchId}
                onClick={() => setState((s) => ({ ...s, branch: b, step: 2 }))}
                className="text-left border rounded p-3 hover:bg-muted"
              >
                <p className="font-medium">{b.name}</p>
                <p className="text-sm text-muted-foreground">{b.address}</p>
              </button>
            ))}
          </div>
        </div>
      )}

      {/* Paso 2 */}
      {state.step === 2 && (
        <div>
          <h2 className="font-semibold mb-3">¿Qué servicio?</h2>
          {servicesQuery.isLoading && <p>Cargando…</p>}
          <div className="grid gap-2">
            {servicesQuery.data?.map((s) => (
              <button
                key={s.serviceId}
                onClick={() => setState((prev) => ({ ...prev, service: s, step: 3 }))}
                className="text-left border rounded p-3 hover:bg-muted"
              >
                <p className="font-medium">{s.name}</p>
                <p className="text-sm text-muted-foreground">
                  {s.durationMinutes} min · ${s.referencePrice}
                </p>
              </button>
            ))}
          </div>
        </div>
      )}

      {/* Paso 3 */}
      {state.step === 3 && (
        <div>
          <h2 className="font-semibold mb-3">Elegí fecha y horario</h2>
          <input
            type="date"
            min={format(new Date(), 'yyyy-MM-dd')}
            value={state.date}
            onChange={(e) => setState((s) => ({ ...s, date: e.target.value }))}
            className="border rounded p-2 w-full mb-4"
          />
          {availabilityQuery.isLoading && <p>Buscando horarios disponibles…</p>}
          {availabilityQuery.data?.availableSlots.length === 0 && (
            <p className="text-muted-foreground">No hay horarios disponibles para esa fecha.</p>
          )}
          <div className="grid grid-cols-3 gap-2">
            {availabilityQuery.data?.availableSlots.map((slot) => (
              <button
                key={slot.startAt}
                onClick={() => setState((s) => ({ ...s, slot, step: 4 }))}
                className="border rounded p-2 text-sm hover:bg-primary hover:text-white"
              >
                {format(new Date(slot.startAt), 'HH:mm')}
              </button>
            ))}
          </div>
        </div>
      )}

      {/* Paso 4 */}
      {state.step === 4 && (
        <div>
          <h2 className="font-semibold mb-3">Confirmá tu reserva</h2>
          <div className="bg-muted rounded p-4 space-y-2 text-sm mb-4">
            <p><strong>Sucursal:</strong> {state.branch?.name}</p>
            <p><strong>Servicio:</strong> {state.service?.name}</p>
            <p><strong>Horario:</strong> {format(new Date(state.slot!.startAt), 'dd/MM/yyyy HH:mm')}</p>
            <p><strong>Recurso:</strong> {state.slot?.resourceName}</p>
          </div>
          <button
            onClick={() => createMutation.mutate()}
            disabled={createMutation.isPending}
            className="w-full bg-primary text-white rounded p-3 font-medium disabled:opacity-50"
          >
            {createMutation.isPending ? 'Reservando…' : 'Confirmar reserva'}
          </button>
        </div>
      )}
    </div>
  );
}
```

---

### 9.5 `/mis-reservas`

**Acceso:** solo `client`  
**Nota:** el backend no tiene `GET /reservations/my`. El MVP guarda el `reservationId`
tras cada reserva exitosa (en localStorage/estado local) y permite al cliente buscar
cada una por ID con `GET /reservations/{id}`. Si se necesita un listado completo se
debe agregar el endpoint en BookingService.

```tsx
// Comportamiento mínimo MVP:
// - Al llegar con ?nuevo=<id>, mostrar la reserva recién creada
// - Permitir cancelarla
// - Guardar IDs en localStorage bajo la clave "mis-reservas"

'use client';

import { useEffect, useState } from 'react';
import { useSearchParams } from 'next/navigation';
import { useAuth } from '@/lib/auth-store';
import { bookingApi } from '@/lib/api/booking';
import { Reservation } from '@/types/booking';
import { ReservationCard } from '@/components/booking/ReservationCard';

const STORAGE_KEY = 'reservas-ids';

function getStoredIds(): string[] {
  try {
    return JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '[]');
  } catch {
    return [];
  }
}

function addStoredId(id: string) {
  const ids = getStoredIds();
  if (!ids.includes(id)) {
    localStorage.setItem(STORAGE_KEY, JSON.stringify([id, ...ids]));
  }
}

export default function MisReservasPage() {
  const { token } = useAuth();
  const searchParams = useSearchParams();
  const nuevoId = searchParams.get('nuevo');
  const [reservations, setReservations] = useState<Reservation[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (nuevoId) addStoredId(nuevoId);

    async function loadAll() {
      const ids = getStoredIds();
      const results = await Promise.all(
        ids.map((id) => bookingApi.getReservation(token!, id).catch(() => null)),
      );
      setReservations(results.filter(Boolean) as Reservation[]);
      setLoading(false);
    }

    loadAll();
  }, [nuevoId, token]);

  const handleCancel = async (reservationId: string) => {
    await bookingApi.cancelReservation(token!, reservationId, 'Cancelado por el cliente');
    setReservations((prev) =>
      prev.map((r) =>
        r.reservationId === reservationId ? { ...r, status: 'CANCELLED' } : r,
      ),
    );
  };

  if (loading) return <p>Cargando…</p>;

  if (reservations.length === 0) {
    return (
      <main className="container py-8">
        <h1 className="text-2xl font-bold mb-4">Mis reservas</h1>
        <p className="text-muted-foreground">No tenés reservas registradas.</p>
      </main>
    );
  }

  return (
    <main className="container py-8">
      <h1 className="text-2xl font-bold mb-6">Mis reservas</h1>
      <div className="space-y-3">
        {reservations.map((r) => (
          <ReservationCard key={r.reservationId} reservation={r} onCancel={handleCancel} />
        ))}
      </div>
    </main>
  );
}
```

---

### 9.6 `/admin/agenda`

**Acceso:** `tenant_admin`, `branch_admin`  
**Es la pantalla más importante del panel admin.**

```tsx
'use client';

import { useState } from 'react';
import { format } from 'date-fns';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useAuth } from '@/lib/auth-store';
import { bookingApi } from '@/lib/api/booking';
import { AgendaReservationItem, AgendaBlockItem } from '@/types/booking';
import { RoleGuard } from '@/components/layout/RoleGuard';
import { StatusBadge } from '@/components/booking/StatusBadge';

export default function AgendaPage() {
  const { token, branchId: claimBranchId, role } = useAuth();
  const qc = useQueryClient();

  const today = format(new Date(), 'yyyy-MM-dd');
  const [date, setDate] = useState(today);
  // branch_admin usa su propio branchId del claim
  const [branchId, setBranchId] = useState(claimBranchId ?? '');
  const [statusFilter, setStatusFilter] = useState('');

  const agendaQuery = useQuery({
    queryKey: ['agenda', branchId, date, statusFilter],
    queryFn: () =>
      bookingApi.getAgenda(token!, {
        branchId,
        date,
        status: statusFilter || undefined,
      }),
    enabled: !!branchId && !!date,
  });

  const attendMutation = useMutation({
    mutationFn: (reservationId: string) =>
      bookingApi.attendReservation(token!, reservationId),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['agenda'] }),
  });

  const noShowMutation = useMutation({
    mutationFn: (reservationId: string) =>
      bookingApi.noShowReservation(token!, reservationId),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['agenda'] }),
  });

  const cancelReservationMutation = useMutation({
    mutationFn: (reservationId: string) =>
      bookingApi.cancelReservation(token!, reservationId),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['agenda'] }),
  });

  const cancelBlockMutation = useMutation({
    mutationFn: (blockId: string) => bookingApi.cancelBlock(token!, blockId),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['agenda'] }),
  });

  return (
    <RoleGuard allowedRoles={['tenant_admin', 'branch_admin', 'super_admin']}>
      <div className="space-y-4">
        <h1 className="text-xl font-bold">Agenda del día</h1>

        {/* Filtros */}
        <div className="flex gap-3 flex-wrap">
          <input
            type="date"
            value={date}
            onChange={(e) => setDate(e.target.value)}
            className="border rounded p-2"
          />
          {role !== 'branch_admin' && (
            <input
              placeholder="Branch ID"
              value={branchId}
              onChange={(e) => setBranchId(e.target.value)}
              className="border rounded p-2"
            />
          )}
          <select
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value)}
            className="border rounded p-2"
          >
            <option value="">Todos los estados</option>
            <option value="CONFIRMED">Confirmadas</option>
            <option value="ATTENDED">Atendidas</option>
            <option value="CANCELLED">Canceladas</option>
            <option value="NO_SHOW">No show</option>
          </select>
        </div>

        {agendaQuery.isLoading && <p>Cargando agenda…</p>}

        {/* Reservas */}
        {agendaQuery.data && (
          <>
            <section>
              <h2 className="font-semibold mb-2">
                Reservas ({agendaQuery.data.reservations.length})
              </h2>
              <div className="space-y-2">
                {agendaQuery.data.reservations.map((r) => (
                  <AgendaReservationRow
                    key={r.reservationId}
                    item={r}
                    onAttend={() => attendMutation.mutate(r.reservationId)}
                    onNoShow={() => noShowMutation.mutate(r.reservationId)}
                    onCancel={() => cancelReservationMutation.mutate(r.reservationId)}
                  />
                ))}
              </div>
            </section>

            <section>
              <h2 className="font-semibold mb-2">
                Bloqueos ({agendaQuery.data.blocks.length})
              </h2>
              <div className="space-y-2">
                {agendaQuery.data.blocks.map((b) => (
                  <AgendaBlockRow
                    key={b.blockId}
                    item={b}
                    onCancel={() => cancelBlockMutation.mutate(b.blockId)}
                  />
                ))}
              </div>
            </section>
          </>
        )}
      </div>
    </RoleGuard>
  );
}

// Sub-componentes internos de la agenda
function AgendaReservationRow({
  item, onAttend, onNoShow, onCancel,
}: {
  item: AgendaReservationItem;
  onAttend: () => void;
  onNoShow: () => void;
  onCancel: () => void;
}) {
  const isConfirmed = item.status === 'CONFIRMED';
  return (
    <div className="border rounded p-3 flex items-center justify-between gap-2">
      <div className="text-sm">
        <p className="font-medium">
          {format(new Date(item.startAt), 'HH:mm')} –{' '}
          {format(new Date(item.endAt), 'HH:mm')}
        </p>
        <p className="text-muted-foreground">
          Recurso: {item.resourceId.slice(0, 8)}…
        </p>
        {item.notes && <p className="italic">{item.notes}</p>}
      </div>
      <div className="flex items-center gap-2">
        <StatusBadge status={item.status} />
        {isConfirmed && (
          <>
            <button
              onClick={onAttend}
              className="text-xs bg-green-100 text-green-800 rounded px-2 py-1"
            >
              Atendida
            </button>
            <button
              onClick={onNoShow}
              className="text-xs bg-yellow-100 text-yellow-800 rounded px-2 py-1"
            >
              No-show
            </button>
            <button
              onClick={onCancel}
              className="text-xs bg-red-100 text-red-800 rounded px-2 py-1"
            >
              Cancelar
            </button>
          </>
        )}
      </div>
    </div>
  );
}

function AgendaBlockRow({
  item, onCancel,
}: { item: AgendaBlockItem; onCancel: () => void }) {
  return (
    <div className="border rounded p-3 flex items-center justify-between gap-2 bg-orange-50">
      <div className="text-sm">
        <p className="font-medium">
          {format(new Date(item.startAt), 'HH:mm')} –{' '}
          {format(new Date(item.endAt), 'HH:mm')}
        </p>
        <p className="text-muted-foreground">{item.reason ?? 'Bloqueo manual'}</p>
      </div>
      {item.status === 'ACTIVE' && (
        <button
          onClick={onCancel}
          className="text-xs bg-red-100 text-red-800 rounded px-2 py-1"
        >
          Cancelar bloqueo
        </button>
      )}
    </div>
  );
}
```

---

### 9.7 `/admin/reservas`

**Acceso:** `tenant_admin`, `branch_admin`  
**API:** `GET /admin/reservations` con filtros

Mostrar tabla con columnas: Fecha/hora, Sucursal, Servicio, Recurso, Cliente, Estado, Historial (expandible). Filtros en la parte superior: `dateFrom`, `dateTo`, `status`, `clientUserId`.

```tsx
// Hook TanStack Query reutilizable
function useReservationsSearch(token: string, params: Record<string, string>) {
  return useQuery({
    queryKey: ['reservations-search', params],
    queryFn: () => bookingApi.searchReservations(token, params),
    enabled: !!token,
  });
}
```

---

### 9.8 `/admin/sucursales` — CRUD

**Acceso:** `tenant_admin`  
**APIs:** `GET /branches`, `POST /branches`, `PUT /branches/{id}`, `PATCH /branches/{id}/status`

**Schema Zod para el formulario:**

```ts
const branchSchema = z.object({
  name: z.string().min(1, 'Nombre requerido'),
  address: z.string().min(1, 'Dirección requerida'),
  phone: z.string().nullable().optional(),
  emailContact: z.string().email().nullable().optional(),
  timezone: z.string().min(1, 'Timezone requerido'),
  status: z.enum(['active', 'inactive']),
});
```

**Patrón de lista + modal de edición:**

```
┌─────────────────────────────────────┐
│  Sucursales           [+ Nueva]     │
├─────────────────────────────────────┤
│ Sucursal Norte  active  [Editar]    │
│ Sucursal Sur    inactive [Activar]  │
└─────────────────────────────────────┘
      ↓ click Editar
┌──────── Dialog ─────────────────────┐
│  Editar Sucursal                    │
│  [Form: nombre, dirección, etc.]    │
│              [Cancelar] [Guardar]   │
└─────────────────────────────────────┘
```

Después de crear o editar: `queryClient.invalidateQueries({ queryKey: ['branches'] })`.

---

### 9.9 `/admin/servicios` — CRUD

**Acceso:** `tenant_admin`  
**APIs:** `GET /services`, `POST /services`, `PUT /services/{id}`, `PATCH /services/{id}/status`

**Schema Zod:**

```ts
const serviceSchema = z.object({
  name: z.string().min(1),
  description: z.string().nullable().optional(),
  durationMinutes: z.number().int().min(1, 'Duración mínima 1 minuto'),
  referencePrice: z.number().min(0),
  modality: z.enum(['presencial', 'virtual', 'a_domicilio']),
  status: z.enum(['active', 'inactive']),
});
```

Extra: incluir sección para **vincular servicio a sucursal** con `POST /branches/{branchId}/services/{serviceId}`.

---

### 9.10 `/admin/recursos` — CRUD

**Acceso:** `tenant_admin`  
**APIs:** `GET /resources?branchId=...`, `POST /resources`, `PATCH /resources/{id}/status`

**Schema Zod:**

```ts
const resourceSchema = z.object({
  branchId: z.string().uuid('Seleccioná una sucursal'),
  name: z.string().min(1),
  resourceType: z.string().min(1),  // ej: "seat", "professional", "room"
  status: z.enum(['active', 'inactive', 'blocked']),
});
```

---

### 9.11 `/admin/horarios`

**Acceso:** `tenant_admin`  
**Flujo:** elegir recurso → ver horarios → agregar/desactivar

```
Recurso: [Select: Silla 1 ▼]

Día         Inicio   Fin       Estado
Lunes       09:00    18:00     active   [Desactivar]
Martes      09:00    18:00     active   [Desactivar]
…

[+ Agregar horario]
```

**Schema Zod para nuevo horario:**

```ts
const scheduleSchema = z.object({
  resourceId: z.string().uuid(),
  dayOfWeek: z.number().int().min(0).max(6),
  startTime: z.string().regex(/^\d{2}:\d{2}$/),
  endTime: z.string().regex(/^\d{2}:\d{2}$/),
}).refine((d) => d.startTime < d.endTime, {
  message: 'La hora de inicio debe ser anterior a la de fin',
  path: ['endTime'],
});
```

Los días de semana en el backend son: 0=Lunes, 1=Martes, …, 6=Domingo.

---

### 9.12 `/admin/bloqueos`

**Acceso:** `tenant_admin`, `branch_admin`  
**APIs:** `POST /resource-blocks`, `PATCH /resource-blocks/{id}/cancel`

**Schema Zod:**

```ts
const blockSchema = z.object({
  branchId: z.string().uuid(),
  resourceId: z.string().uuid(),
  startAt: z.string().min(1, 'Fecha/hora inicio requerida'),
  endAt: z.string().min(1, 'Fecha/hora fin requerida'),
  reason: z.string().optional(),
  blockType: z.enum(['manual']),
}).refine(
  (d) => new Date(d.startAt) < new Date(d.endAt),
  { message: 'El inicio debe ser antes del fin', path: ['endAt'] },
);
```

Los `startAt`/`endAt` se envían como ISO 8601 con timezone de la sucursal.
Usar `<input type="datetime-local">` y convertir con date-fns:

```ts
import { formatISO, parseISO } from 'date-fns';
import { toZonedTime, fromZonedTime } from 'date-fns-tz';

// Convertir datetime-local a ISO con timezone de la sucursal
function toLocalISO(datetimeLocal: string, tz: string): string {
  const date = parseISO(datetimeLocal);
  return fromZonedTime(date, tz).toISOString();
}
```

---

### 9.13 `/admin/reportes`

**Acceso:** `tenant_admin`, `branch_admin`

Esta es la pantalla de análisis. Organizar en pestañas:

```
[Resumen diario] [Servicios] [Recursos] [Horas pico]
```

#### Pestaña: Resumen diario

```tsx
function DailySummaryTab() {
  const { token, branchId } = useAuth();
  const [date, setDate] = useState(format(new Date(), 'yyyy-MM-dd'));

  const summaryQuery = useQuery({
    queryKey: ['daily-summary', date, branchId],
    queryFn: () =>
      reportingApi.getDailySummary(token!, {
        date,
        ...(branchId ? { branchId } : {}),
      }),
  });

  const data = summaryQuery.data;

  return (
    <div>
      <div className="flex gap-2 mb-4">
        <input type="date" value={date} onChange={(e) => setDate(e.target.value)}
          className="border rounded p-2" />
      </div>

      {data?.dataStatus === 'PENDING_SYNC' && (
        <div className="bg-yellow-50 border border-yellow-200 rounded p-3 text-sm mb-4">
          Los reportes pueden tardar unos segundos en actualizarse.
        </div>
      )}

      <div className="grid grid-cols-2 md:grid-cols-3 gap-4">
        <StatCard title="Creadas" value={data?.totalCreated ?? 0} color="blue" />
        <StatCard title="Confirmadas" value={data?.totalConfirmed ?? 0} color="green" />
        <StatCard title="Canceladas" value={data?.totalCancelled ?? 0} color="red" />
        <StatCard title="Atendidas" value={data?.totalAttended ?? 0} color="emerald" />
        <StatCard title="No-show" value={data?.totalNoShow ?? 0} color="orange" />
        <StatCard title="Minutos reservados" value={data?.totalReservedMinutes ?? 0} />
      </div>
    </div>
  );
}

function StatCard({ title, value, color = 'gray' }: {
  title: string; value: number; color?: string;
}) {
  return (
    <div className="border rounded-lg p-4">
      <p className="text-sm text-muted-foreground">{title}</p>
      <p className="text-3xl font-bold mt-1">{value}</p>
    </div>
  );
}
```

#### Pestaña: Servicios más reservados

```tsx
function ServicesTab() {
  const { token } = useAuth();
  const [month, setMonth] = useState(format(new Date(), 'yyyy-MM'));

  const query = useQuery({
    queryKey: ['top-services', month],
    queryFn: () => reportingApi.getTopServices(token!, { month }),
  });

  return (
    <div>
      <input type="month" value={month} onChange={(e) => setMonth(e.target.value)}
        className="border rounded p-2 mb-4" />

      <table className="w-full text-sm">
        <thead>
          <tr className="text-left border-b">
            <th className="py-2">#</th>
            <th>Servicio</th>
            <th>Creadas</th>
            <th>Atendidas</th>
            <th>Canceladas</th>
            <th>No-show</th>
          </tr>
        </thead>
        <tbody>
          {query.data?.services.map((s) => (
            <tr key={s.serviceId} className="border-b">
              <td className="py-2 font-bold text-muted-foreground">{s.rank}</td>
              <td className="font-medium">{s.serviceName}</td>
              <td>{s.totalCreated}</td>
              <td>{s.totalAttended}</td>
              <td>{s.totalCancelled}</td>
              <td>{s.totalNoShow}</td>
            </tr>
          ))}
        </tbody>
      </table>

      {query.data?.services.length === 0 && (
        <p className="text-muted-foreground mt-4">No hay datos para el período.</p>
      )}
    </div>
  );
}
```

#### Pestaña: Horas pico

```tsx
function PeakHoursTab() {
  const { token, branchId: claimBranchId } = useAuth();
  const [date, setDate] = useState(format(new Date(), 'yyyy-MM-dd'));
  const [branchId, setBranchId] = useState(claimBranchId ?? '');

  const query = useQuery({
    queryKey: ['peak-hours', branchId, date],
    queryFn: () => reportingApi.getPeakHours(token!, { branchId, date }),
    enabled: !!branchId,
  });

  // Representar como barras simples CSS
  const maxCreated = Math.max(...(query.data?.hours.map((h) => h.totalCreated) ?? [1]));

  return (
    <div>
      <input type="date" value={date} onChange={(e) => setDate(e.target.value)}
        className="border rounded p-2 mb-4" />

      <div className="space-y-1">
        {query.data?.hours.map((h) => (
          <div key={h.hourOfDay} className="flex items-center gap-2">
            <span className="w-12 text-sm text-right text-muted-foreground">
              {String(h.hourOfDay).padStart(2, '0')}:00
            </span>
            <div
              className="h-6 bg-primary rounded"
              style={{ width: `${(h.totalCreated / maxCreated) * 100}%` }}
            />
            <span className="text-sm">{h.totalCreated}</span>
          </div>
        ))}

        {query.data?.hours.length === 0 && (
          <p className="text-muted-foreground">No hay datos para esa fecha.</p>
        )}
      </div>
    </div>
  );
}
```

---

## 10. Componentes compartidos

### `StatusBadge`

```tsx
import { Badge } from '@/components/ui/badge';
import { ReservationStatus } from '@/types/booking';

const CONFIG: Record<ReservationStatus, { label: string; variant: 'default'|'secondary'|'destructive'|'outline' }> = {
  CONFIRMED:  { label: 'Confirmada',  variant: 'default' },
  ATTENDED:   { label: 'Atendida',    variant: 'secondary' },
  CANCELLED:  { label: 'Cancelada',   variant: 'destructive' },
  NO_SHOW:    { label: 'No-show',     variant: 'outline' },
};

export function StatusBadge({ status }: { status: ReservationStatus }) {
  const { label, variant } = CONFIG[status] ?? { label: status, variant: 'outline' };
  return <Badge variant={variant}>{label}</Badge>;
}
```

### `AdminSidebar`

```tsx
'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { useAuth } from '@/lib/auth-store';
import {
  CalendarDays, List, Lock, Building2,
  Scissors, Wrench, Clock, BarChart3, LogOut,
} from 'lucide-react';

const NAV = [
  { href: '/admin/agenda',    label: 'Agenda',     icon: CalendarDays },
  { href: '/admin/reservas',  label: 'Reservas',   icon: List },
  { href: '/admin/bloqueos',  label: 'Bloqueos',   icon: Lock },
  { href: '/admin/sucursales',label: 'Sucursales', icon: Building2 },
  { href: '/admin/servicios', label: 'Servicios',  icon: Scissors },
  { href: '/admin/recursos',  label: 'Recursos',   icon: Wrench },
  { href: '/admin/horarios',  label: 'Horarios',   icon: Clock },
  { href: '/admin/reportes',  label: 'Reportes',   icon: BarChart3 },
];

export function AdminSidebar() {
  const pathname = usePathname();
  const { clearAuth, email, role } = useAuth();

  return (
    <aside className="w-56 h-screen border-r flex flex-col">
      <div className="p-4 border-b">
        <p className="font-semibold text-sm truncate">{email}</p>
        <p className="text-xs text-muted-foreground">{role}</p>
      </div>

      <nav className="flex-1 p-2 space-y-1">
        {NAV.map(({ href, label, icon: Icon }) => (
          <Link
            key={href}
            href={href}
            className={`flex items-center gap-2 px-3 py-2 rounded text-sm transition-colors ${
              pathname.startsWith(href)
                ? 'bg-primary text-primary-foreground'
                : 'hover:bg-muted'
            }`}
          >
            <Icon className="w-4 h-4" />
            {label}
          </Link>
        ))}
      </nav>

      <button
        onClick={clearAuth}
        className="flex items-center gap-2 p-4 text-sm text-muted-foreground hover:text-foreground"
      >
        <LogOut className="w-4 h-4" />
        Cerrar sesión
      </button>
    </aside>
  );
}
```

### `src/app/admin/layout.tsx`

```tsx
import { AdminSidebar } from '@/components/layout/AdminSidebar';

export default function AdminLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex h-screen">
      <AdminSidebar />
      <main className="flex-1 overflow-auto p-6">{children}</main>
    </div>
  );
}
```

---

## 11. Manejo de errores — patrón unificado

### Mapeo de códigos HTTP a mensajes de usuario

```ts
// src/lib/error-messages.ts
export function getErrorMessage(err: unknown): string {
  if (!(err instanceof Error)) return 'Error desconocido';

  // ApiError del fetcher
  const code = (err as any).code as string | undefined;

  const messages: Record<string, string> = {
    SLOT_ALREADY_TAKEN:    'Ese horario acaba de ser reservado. Elegí otro.',
    RESOURCE_BLOCKED:      'El recurso no está disponible en ese horario.',
    RESOURCE_NOT_AVAILABLE:'El recurso no tiene horario en ese momento.',
    RESERVATION_NOT_FOUND: 'La reserva no existe.',
    BRANCH_NOT_FOUND:      'La sucursal no existe o está inactiva.',
    SERVICE_NOT_FOUND:     'El servicio no existe o no está activo.',
    VALIDATION_ERROR:      err.message,
    UNAUTHORIZED:          'Sesión expirada. Volvé a iniciar sesión.',
  };

  return messages[code ?? ''] ?? err.message ?? 'Error inesperado';
}
```

### Hook reutilizable para mutaciones

```ts
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useToast } from '@/components/ui/use-toast';
import { getErrorMessage } from '@/lib/error-messages';

export function useApiMutation<TData, TVariables>(
  mutationFn: (vars: TVariables) => Promise<TData>,
  options?: {
    onSuccess?: (data: TData) => void;
    invalidateKeys?: string[][];
    successMessage?: string;
  },
) {
  const qc = useQueryClient();
  const { toast } = useToast();

  return useMutation({
    mutationFn,
    onSuccess: (data) => {
      if (options?.successMessage) {
        toast({ title: options.successMessage });
      }
      options?.invalidateKeys?.forEach((key) =>
        qc.invalidateQueries({ queryKey: key }),
      );
      options?.onSuccess?.(data);
    },
    onError: (err) => {
      toast({
        title: 'Error',
        description: getErrorMessage(err),
        variant: 'destructive',
      });
    },
  });
}
```

---

## 12. Utilities

### `src/lib/utils.ts`

```ts
import { clsx, type ClassValue } from 'clsx';
import { twMerge } from 'tailwind-merge';
import { format, parseISO } from 'date-fns';
import { es } from 'date-fns/locale';

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}

export function formatDateTime(iso: string): string {
  return format(parseISO(iso), "d 'de' MMMM, HH:mm", { locale: es });
}

export function formatDate(iso: string): string {
  return format(parseISO(iso), "d 'de' MMMM yyyy", { locale: es });
}

export function formatTime(iso: string): string {
  return format(parseISO(iso), 'HH:mm');
}

export function today(): string {
  return format(new Date(), 'yyyy-MM-dd');
}
```

---

## 13. Checklist de implementación

Seguir este orden ya que hay dependencias entre pantallas:

- [ ] Proyecto Next.js inicializado y dependencias instaladas
- [ ] Variables de entorno `.env.local` con los 4 puertos correctos
- [ ] Tipos TypeScript (`/types/*.ts`)
- [ ] Fetcher base y error class (`/lib/api/fetcher.ts`)
- [ ] Auth store Zustand (`/lib/auth-store.ts`)
- [ ] Clients de API por servicio (`/lib/api/*.ts`)
- [ ] Providers en `app/layout.tsx`
- [ ] `LoginForm` + `/login` — validar que el JWT llega y se guarda
- [ ] `AdminSidebar` + `admin/layout.tsx`
- [ ] `RoleGuard` component
- [ ] `/negocios` — verificar que lista tenants
- [ ] `/[slug]/reservar` wizard — paso por paso
- [ ] `/admin/agenda` — es la pantalla principal, completarla antes que el CRUD
- [ ] `/admin/reservas` — buscar por filtros
- [ ] `/admin/bloqueos` — crear y cancelar
- [ ] `/admin/sucursales` — CRUD completo
- [ ] `/admin/servicios` — CRUD + vincular a sucursal
- [ ] `/admin/recursos` — CRUD
- [ ] `/admin/horarios` — por recurso
- [ ] `/admin/reportes` — 4 pestañas en orden: resumen, servicios, recursos, horas pico
- [ ] `/mis-reservas` — cliente ve su reserva post-creación
- [ ] `/registro` — cliente nuevo

---

## 14. Notas sobre gaps del backend

**`GET /reservations/my` no existe.** El backend no tiene un endpoint para que el cliente
liste sus propias reservas. El MVP lo resuelve guardando IDs en localStorage.
Para una implementación completa se debe agregar este endpoint a BookingService:

```
GET /reservations/my
→ retorna las reservas del clientUserId del JWT
→ filtrado por status opcional
→ ordenado por startAt DESC
```

**Reporting necesita datos en Cassandra.** Los endpoints de reporte retornan
`dataStatus: "PENDING_SYNC"` si el worker outbox de Booking aún no procesó los
eventos. Mientras el worker no esté implementado, los reportes mostrarán conteos en 0.
La UI ya maneja este caso con el mensaje de advertencia.
