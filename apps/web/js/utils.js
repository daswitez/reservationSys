export function formatDateTime(iso) {
  if (!iso) return '—';
  return new Date(iso).toLocaleString('es-AR', { dateStyle: 'short', timeStyle: 'short' });
}

export function formatDate(iso) {
  if (!iso) return '—';
  return new Date(iso).toLocaleDateString('es-AR');
}

export function formatTime(iso) {
  if (!iso) return '—';
  return new Date(iso).toLocaleTimeString('es-AR', { hour: '2-digit', minute: '2-digit' });
}

export function today() {
  return new Date().toISOString().slice(0, 10);
}

export function isoDateToInput(iso) {
  if (!iso) return '';
  return iso.slice(0, 10);
}

export const DAYS_ES = ['Domingo', 'Lunes', 'Martes', 'Miércoles', 'Jueves', 'Viernes', 'Sábado'];

export const STATUS_LABELS = {
  confirmed: 'Confirmada',
  cancelled: 'Cancelada',
  attended:  'Atendida',
  no_show:   'No presentó',
  active:    'Activo',
  inactive:  'Inactivo',
  blocked:   'Bloqueado',
};

export const STATUS_CLASS = {
  confirmed: 'badge-blue',
  cancelled: 'badge-red',
  attended:  'badge-green',
  no_show:   'badge-gray',
  active:    'badge-green',
  inactive:  'badge-gray',
  blocked:   'badge-orange',
};

export function badge(status) {
  const cls   = STATUS_CLASS[status]  ?? 'badge-gray';
  const label = STATUS_LABELS[status] ?? status;
  return `<span class="badge ${cls}">${label}</span>`;
}

export const ERROR_MESSAGES = {
  SLOT_ALREADY_TAKEN: 'Ese horario acaba de ser reservado. Elegí otro.',
  RESOURCE_BLOCKED:   'Ese recurso no está disponible en ese horario.',
  RESERVATION_NOT_FOUND: 'Reserva no encontrada.',
  ALREADY_CANCELLED:  'La reserva ya está cancelada.',
  ALREADY_ATTENDED:   'La reserva ya fue marcada como atendida.',
  CANNOT_ATTEND_CANCELLED: 'No se puede atender una reserva cancelada.',
};

export function friendlyError(err) {
  return ERROR_MESSAGES[err.code] ?? err.message ?? 'Ocurrió un error.';
}

export function escHtml(str) {
  const d = document.createElement('div');
  d.appendChild(document.createTextNode(String(str)));
  return d.innerHTML;
}
