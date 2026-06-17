import { API } from './config.js';
import { getToken, clearSession } from './auth.js';

export class ApiError extends Error {
  constructor(code, message, status) {
    super(message);
    this.code   = code;
    this.status = status;
  }
}

export async function apiFetch(url, options = {}) {
  const token = getToken();
  const headers = { 'Content-Type': 'application/json', ...options.headers };
  if (token) headers['Authorization'] = `Bearer ${token}`;

  let res;
  try {
    res = await fetch(url, { ...options, headers });
  } catch {
    throw new ApiError('NETWORK_ERROR', 'No se pudo conectar al servidor.', 0);
  }

  if (res.status === 401) {
    clearSession();
    location.href = '/index.html';
    throw new ApiError('UNAUTHORIZED', 'Sesión expirada.', 401);
  }

  let body = null;
  try { body = await res.json(); } catch { /* no body */ }

  if (!res.ok) {
    const err = body?.error ?? {};
    throw new ApiError(err.code ?? `HTTP_${res.status}`, err.message ?? `Error ${res.status}`, res.status);
  }
  return body?.data ?? body;
}

function qs(params) {
  const clean = Object.fromEntries(Object.entries(params ?? {}).filter(([, v]) => v != null && v !== ''));
  return new URLSearchParams(clean).toString();
}

/* ── Identity ───────────────────────────────────────────────────────────── */
export const identity = {
  login:    (email, password) => apiFetch(`${API.identity}/auth/login`,    { method: 'POST', body: JSON.stringify({ email, password }) }),
  register: (data)            => apiFetch(`${API.identity}/auth/register`,  { method: 'POST', body: JSON.stringify(data) }),
  tenants:  ()                => apiFetch(`${API.identity}/tenants/public`),

  // Users
  getUsers:         (p)       => apiFetch(`${API.identity}/users?${qs(p)}`),
  updateUser:       (id, d)   => apiFetch(`${API.identity}/users/${id}`,        { method: 'PUT',   body: JSON.stringify(d) }),
  updateUserStatus: (id, s)   => apiFetch(`${API.identity}/users/${id}/status`, { method: 'PATCH', body: JSON.stringify({ status: s }) }),
  deleteUser:       (id)      => apiFetch(`${API.identity}/users/${id}`,        { method: 'DELETE' }),
  createAdmin:      (d)       => apiFetch(`${API.identity}/users/admin`,        { method: 'POST',  body: JSON.stringify(d) }),
  updateMe:         (d)       => apiFetch(`${API.identity}/users/me`,           { method: 'PUT',   body: JSON.stringify(d) }),
  changePassword:   (d)       => apiFetch(`${API.identity}/users/me/password`,  { method: 'PATCH', body: JSON.stringify(d) }),
};

/* ── Catalog ────────────────────────────────────────────────────────────── */
export const catalog = {
  // Admin branches
  getBranches:   ()       => apiFetch(`${API.catalog}/branches`),
  createBranch:  (d)      => apiFetch(`${API.catalog}/branches`,       { method: 'POST',   body: JSON.stringify(d) }),
  updateBranch:  (id, d)  => apiFetch(`${API.catalog}/branches/${id}`, { method: 'PUT',    body: JSON.stringify(d) }),
  deleteBranch:  (id)     => apiFetch(`${API.catalog}/branches/${id}`, { method: 'DELETE' }),

  // Admin services
  getServices:   ()       => apiFetch(`${API.catalog}/services`),
  createService: (d)      => apiFetch(`${API.catalog}/services`,       { method: 'POST',   body: JSON.stringify(d) }),
  updateService: (id, d)  => apiFetch(`${API.catalog}/services/${id}`, { method: 'PUT',    body: JSON.stringify(d) }),
  deleteService: (id)     => apiFetch(`${API.catalog}/services/${id}`, { method: 'DELETE' }),

  // Admin resources
  getResources:  (p)      => apiFetch(`${API.catalog}/resources?${qs(p)}`),
  createResource:(d)      => apiFetch(`${API.catalog}/resources`,        { method: 'POST',   body: JSON.stringify(d) }),
  updateResource:(id, d)  => apiFetch(`${API.catalog}/resources/${id}`,  { method: 'PUT',    body: JSON.stringify(d) }),
  deleteResource:(id)     => apiFetch(`${API.catalog}/resources/${id}`,  { method: 'DELETE' }),

  // Admin schedules
  getSchedules:  (resourceId)  => apiFetch(`${API.catalog}/resource-schedules?resourceId=${resourceId}`),
  createSchedule:(d)           => apiFetch(`${API.catalog}/resource-schedules`,       { method: 'POST',   body: JSON.stringify(d) }),
  deleteSchedule:(id)          => apiFetch(`${API.catalog}/resource-schedules/${id}`, { method: 'DELETE' }),

  // Public
  publicBranches: (slug)           => apiFetch(`${API.catalog}/public/tenants/${slug}/branches`),
  publicServices: (slug, branchId) => apiFetch(`${API.catalog}/public/tenants/${slug}/branches/${branchId}/services`),
};

/* ── Booking ────────────────────────────────────────────────────────────── */
export const booking = {
  availability:     (p)     => apiFetch(`${API.booking}/availability?${qs(p)}`),
  createReservation:(d)     => apiFetch(`${API.booking}/reservations`,           { method: 'POST',  body: JSON.stringify(d) }),
  getReservation:   (id)    => apiFetch(`${API.booking}/reservations/${id}`),
  cancel:           (id, r) => apiFetch(`${API.booking}/reservations/${id}/cancel`,  { method: 'PATCH', body: JSON.stringify({ reason: r }) }),
  attend:           (id)    => apiFetch(`${API.booking}/reservations/${id}/attend`,   { method: 'PATCH' }),
  noShow:           (id)    => apiFetch(`${API.booking}/reservations/${id}/no-show`,  { method: 'PATCH' }),
  adminReservations:(p)     => apiFetch(`${API.booking}/admin/reservations?${qs(p)}`),
  agenda:           (p)     => apiFetch(`${API.booking}/admin/agenda?${qs(p)}`),
  getBlocks:        (p)     => apiFetch(`${API.booking}/resource-blocks?${qs(p)}`),
  createBlock:      (d)     => apiFetch(`${API.booking}/resource-blocks`,          { method: 'POST',  body: JSON.stringify(d) }),
  cancelBlock:      (id)    => apiFetch(`${API.booking}/resource-blocks/${id}/cancel`, { method: 'PATCH' }),
};

/* ── Reporting ──────────────────────────────────────────────────────────── */
export const reporting = {
  dailySummary: (p) => apiFetch(`${API.reporting}/reports/daily-summary?${qs(p)}`),
  topServices:  (p) => apiFetch(`${API.reporting}/reports/services/top?${qs(p)}`),
  occupancy:    (p) => apiFetch(`${API.reporting}/reports/resources/occupancy?${qs(p)}`),
  peakHours:    (p) => apiFetch(`${API.reporting}/reports/peak-hours?${qs(p)}`),
};
