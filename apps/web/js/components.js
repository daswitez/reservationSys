import { clearSession, getUser, getRoles } from './auth.js';

/* ── Toast ───────────────────────────────────────────────────────────────── */
let _toastContainer = null;

function toastContainer() {
  if (!_toastContainer) {
    _toastContainer = document.createElement('div');
    _toastContainer.className = 'toast-container';
    document.body.appendChild(_toastContainer);
  }
  return _toastContainer;
}

export function toast(message, type = 'info') {
  const t = document.createElement('div');
  t.className = `toast toast-${type}`;
  t.textContent = message;
  toastContainer().appendChild(t);
  requestAnimationFrame(() => t.classList.add('show'));
  setTimeout(() => {
    t.classList.remove('show');
    setTimeout(() => t.remove(), 300);
  }, 3200);
}

/* ── Confirm modal ───────────────────────────────────────────────────────── */
export function confirmModal(message, confirmLabel = 'Confirmar', danger = true) {
  return new Promise(resolve => {
    const overlay = document.createElement('div');
    overlay.className = 'modal-overlay';
    overlay.innerHTML = `
      <div class="modal">
        <p>${message}</p>
        <div class="modal-actions">
          <button class="btn btn-secondary" id="_mc">Cancelar</button>
          <button class="btn ${danger ? 'btn-danger' : 'btn-primary'}" id="_mo">${confirmLabel}</button>
        </div>
      </div>`;
    document.body.appendChild(overlay);
    overlay.querySelector('#_mc').onclick = () => { overlay.remove(); resolve(false); };
    overlay.querySelector('#_mo').onclick = () => { overlay.remove(); resolve(true); };
    overlay.addEventListener('click', e => { if (e.target === overlay) { overlay.remove(); resolve(false); } });
  });
}

/* ── Form modal ──────────────────────────────────────────────────────────── */
export function formModal(title, bodyHtml, submitLabel = 'Guardar') {
  return new Promise(resolve => {
    const overlay = document.createElement('div');
    overlay.className = 'modal-overlay';
    overlay.innerHTML = `
      <div class="modal" style="max-width:540px">
        <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:1rem">
          <h3>${title}</h3>
          <button class="modal-close-btn" id="_mclose">&times;</button>
        </div>
        <div class="modal-body">${bodyHtml}</div>
        <div class="modal-actions">
          <button class="btn btn-secondary" id="_mcancel">Cancelar</button>
          <button class="btn btn-primary" id="_msubmit">${submitLabel}</button>
        </div>
      </div>`;
    document.body.appendChild(overlay);
    const close = (val) => { overlay.remove(); resolve(val); };
    overlay.querySelector('#_mclose').onclick  = () => close(null);
    overlay.querySelector('#_mcancel').onclick = () => close(null);
    overlay.querySelector('#_msubmit').onclick = () => {
      const form = overlay.querySelector('form');
      if (form) {
        const data = Object.fromEntries(new FormData(form).entries());
        close(data);
      } else {
        const inputs = overlay.querySelectorAll('[name]');
        const data = {};
        inputs.forEach(i => { data[i.name] = i.type === 'checkbox' ? i.checked : i.value; });
        close(data);
      }
    };
  });
}

/* ── Sidebar ─────────────────────────────────────────────────────────────── */
const NAV_LINKS = [
  { href: 'agenda.html',     label: '📅 Agenda' },
  { href: 'sucursales.html', label: '🏢 Sucursales' },
  { href: 'servicios.html',  label: '✂️ Servicios' },
  { href: 'recursos.html',   label: '🪑 Recursos' },
  { href: 'horarios.html',   label: '🕐 Horarios' },
  { href: 'bloqueos.html',   label: '🚫 Bloqueos' },
  { href: 'reportes.html',   label: '📊 Reportes' },
];

export function renderSidebar(activePage) {
  const user  = getUser();
  const roles = getRoles();
  const links = NAV_LINKS.map(l =>
    `<a href="${l.href}" class="nav-link${activePage === l.href ? ' active' : ''}">${l.label}</a>`
  ).join('');

  const el = document.createElement('aside');
  el.className = 'sidebar';
  el.innerHTML = `
    <div class="sidebar-header">
      <div class="sidebar-title">ReservasSys</div>
      <span class="sidebar-role">${roles[0] ?? ''}</span>
    </div>
    <nav class="sidebar-nav">${links}</nav>
    <div class="sidebar-footer">
      <span class="sidebar-user">${user?.email ?? ''}</span>
      <button class="btn-link" id="sidebar-logout">Cerrar sesión</button>
    </div>`;

  el.querySelector('#sidebar-logout').addEventListener('click', () => {
    clearSession();
    location.href = '/index.html';
  });
  return el;
}

/* ── Loading helpers ─────────────────────────────────────────────────────── */
export function loadingHtml() {
  return `<div class="loading-center"><div class="spinner"></div></div>`;
}

export function emptyHtml(message = 'Sin datos') {
  return `<div class="state-box"><div class="state-icon">📭</div><h4>${message}</h4></div>`;
}

export function errorHtml(message) {
  return `<div class="state-box"><div class="state-icon">⚠️</div><h4>Error</h4><p>${message}</p></div>`;
}

/* ── setLoading on button ────────────────────────────────────────────────── */
export function setLoading(btn, loading, label) {
  btn.disabled = loading;
  btn.textContent = loading ? 'Cargando...' : label;
}
