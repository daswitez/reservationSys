const TOKEN_KEY = 'reservas_token';
const USER_KEY  = 'reservas_user';
const IDS_KEY   = 'reservas_ids';

export function getToken()  { return localStorage.getItem(TOKEN_KEY); }
export function getUser()   { try { return JSON.parse(localStorage.getItem(USER_KEY)); } catch { return null; } }
export function getRoles()  { return getUser()?.roles ?? []; }
export function getTenantId() { return getUser()?.tenantId ?? null; }
export function getBranchId() { return getUser()?.branchId ?? null; }

export function setSession(token, user) {
  localStorage.setItem(TOKEN_KEY, token);
  localStorage.setItem(USER_KEY, JSON.stringify(user));
}

export function clearSession() {
  localStorage.removeItem(TOKEN_KEY);
  localStorage.removeItem(USER_KEY);
  localStorage.removeItem(IDS_KEY);
}

export function hasRole(...roles) {
  const r = getRoles();
  return roles.some(x => r.includes(x));
}

export function isAdmin() {
  return hasRole('super_admin', 'tenant_admin', 'branch_admin');
}

// Staff = admin roles + receptionist + professional (can access operational admin pages)
export function isStaff() {
  return hasRole('super_admin', 'tenant_admin', 'branch_admin', 'receptionist', 'professional');
}

export function requireAuth() {
  if (!getToken()) { location.href = '/index.html'; return false; }
  return true;
}

export function requireAdmin() {
  if (!getToken()) { location.href = '/index.html'; return false; }
  if (!isAdmin())  { location.href = '/index.html'; return false; }
  return true;
}

export function requireStaff() {
  if (!getToken()) { location.href = '/index.html'; return false; }
  if (!isStaff())  { location.href = '/index.html'; return false; }
  return true;
}

export function saveReservationId(id) {
  const ids = getSavedIds();
  if (!ids.includes(id)) ids.unshift(id);
  localStorage.setItem(IDS_KEY, JSON.stringify(ids.slice(0, 50)));
}

export function getSavedIds() {
  try { return JSON.parse(localStorage.getItem(IDS_KEY) ?? '[]'); } catch { return []; }
}

export function redirectByRole() {
  if (isStaff()) {
    location.href = '/admin/agenda.html';
  } else {
    location.href = '/negocios.html';
  }
}
