// public/js/views/locations.js
import * as api from '../api.js';
import * as worldMap from '../map.js';
import { state } from '../core/state.js';
import { fmtWt, mkDiv } from '../core/util.js';
import { renderObjectRespawnLog } from './objects.js';

export async function loadLocations() {
  const [data, respawnData] = await Promise.all([
    api.getLocations(state.currentCharId),
    api.getWorldRespawns().catch(() => ({ events: [] })),
  ]);
  // data = { sessions, byLocation, contextEvents, objectInteractions }
  renderLocStats('locStatsRow', data);
  renderLocSessions('locSessionsList', data.sessions ?? []);
  renderLocObjects('locObjectList', data.objectInteractions ?? []);
  renderLocMoves('locMoveList', data.contextEvents ?? []);

  // World respawn log (global — not character-specific)
  const respawnCard = document.getElementById('worldRespawnCard');
  const respawnEvts = respawnData.events ?? [];
  if (respawnEvts.length > 0) {
    respawnCard?.classList.remove('d-none');
    renderObjectRespawnLog('worldRespawnList', respawnEvts);
  } else {
    respawnCard?.classList.add('d-none');
  }
}

export async function loadMap() {
  const [mapData, locData] = await Promise.all([
    api.getWorldMap(),
    api.getLocations(state.currentCharId),
  ]);
  worldMap.renderMap('mapContainer', mapData, locData);

  const resetBtn = document.getElementById('mapResetView');
  if (resetBtn) resetBtn.onclick = () => worldMap.resetMapView();
}

export function renderLocStats(containerId, data) {
  const row = document.getElementById(containerId);
  if (!row) return;
  while (row.firstChild) row.removeChild(row.firstChild);

  const byLoc    = data.byLocation ?? {};
  const sessions = data.sessions   ?? [];
  const totalLoc = Object.keys(byLoc).length;
  const mostVisited = Object.entries(byLoc).sort((a, b) => b[1].visits - a[1].visits)[0];
  const totalObjInt = Object.values(byLoc).reduce((s, v) => s + v.objInteractions, 0);
  const totalMoves  = sessions.length;

  const pills = [
    { label: 'Unique locations', value: totalLoc,              cls: 'primary' },
    { label: 'Location changes', value: totalMoves,            cls: 'info'    },
    { label: 'Object interactions', value: totalObjInt,        cls: 'warning' },
    mostVisited ? { label: 'Most visited',
      value: `📍 ${mostVisited[0]} (${mostVisited[1].visits}×)`, cls: 'secondary' } : null,
  ].filter(Boolean);

  for (const p of pills) {
    const col  = mkDiv('col-auto');
    const card = mkDiv('card text-center border-0 shadow-sm');
    const body = mkDiv('card-body py-1 px-3');
    const val  = mkDiv('fw-bold'); val.textContent = p.value; val.style.color = `var(--bs-${p.cls})`;
    const lbl  = mkDiv('text-muted small'); lbl.textContent = p.label;
    body.appendChild(val); body.appendChild(lbl); card.appendChild(body); col.appendChild(card);
    row.appendChild(col);
  }
}

export function renderLocSessions(containerId, sessions) {
  const el = document.getElementById(containerId);
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);

  if (!sessions.length) {
    const p = document.createElement('p'); p.className = 'text-muted small fst-italic m-3';
    p.textContent = 'No location changes found (EventId 1200 not in logs).'; el.appendChild(p); return;
  }

  const table = document.createElement('table'); table.className = 'table table-sm table-hover mb-0 small';
  const thead = document.createElement('thead'); thead.className = 'table-light';
  const hr = document.createElement('tr');
  ['Time', 'Location', 'Noise', 'Crowd', 'Actions there', 'Objects used'].forEach(h => {
    const th = document.createElement('th'); th.textContent = h; hr.appendChild(th);
  });
  thead.appendChild(hr); table.appendChild(thead);

  const tbody = document.createElement('tbody');
  for (const s of sessions) {
    const tr = document.createElement('tr');

    const tdT = document.createElement('td'); tdT.className = 'text-nowrap text-muted';
    tdT.textContent = fmtWt(s.start, { short: true });

    const tdL = document.createElement('td'); tdL.className = 'fw-semibold';
    tdL.textContent = `📍 ${s.location}`;

    const tdN = document.createElement('td'); tdN.className = 'text-muted';
    if (s.noise != null) {
      const pct = Math.round(s.noise * 100);
      const wrap = mkDiv('d-flex align-items-center gap-1');
      const prog = mkDiv('progress flex-grow-1'); prog.style.height = '4px';
      const bar  = mkDiv('progress-bar'); bar.style.width = pct + '%';
      prog.appendChild(bar); wrap.appendChild(prog);
      const v = document.createElement('span'); v.textContent = pct + '%'; wrap.appendChild(v);
      tdN.appendChild(wrap);
    } else { tdN.textContent = '—'; }

    const tdC = document.createElement('td'); tdC.className = 'text-muted';
    if (s.crowding != null) {
      const pct = Math.round(s.crowding * 100);
      const cls = pct > 70 ? 'danger' : pct > 40 ? 'warning' : 'secondary';
      const b = document.createElement('span'); b.className = `badge text-bg-${cls}`;
      b.textContent = pct + '%'; tdC.appendChild(b);
    } else { tdC.textContent = '—'; }

    const tdA = document.createElement('td'); tdA.className = 'text-muted small';
    if (s.actions.length) {
      const grouped = {};
      for (const a of s.actions) grouped[a.action] = (grouped[a.action] ?? 0) + 1;
      tdA.textContent = Object.entries(grouped).map(([a, c]) => `${a}×${c}`).join(', ');
    } else { tdA.textContent = '—'; }

    const tdO = document.createElement('td'); tdO.className = 'text-muted small';
    if (s.objectInteractions.length) {
      tdO.textContent = s.objectInteractions.map(o => `${o.icon}${o.objectId}`).join(', ');
    } else { tdO.textContent = '—'; }

    [tdT, tdL, tdN, tdC, tdA, tdO].forEach(td => tr.appendChild(td));
    tbody.appendChild(tr);
  }
  table.appendChild(tbody); el.appendChild(table);
}

export function renderLocObjects(containerId, objs) {
  const el = document.getElementById(containerId);
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);

  if (!objs.length) {
    const p = document.createElement('p'); p.className = 'text-muted small fst-italic m-3';
    p.textContent = 'No object interactions (EventId 1205 not in logs — run simulation).'; el.appendChild(p); return;
  }

  const table = document.createElement('table'); table.className = 'table table-sm mb-0 small';
  const tbody = document.createElement('tbody');
  for (const o of objs) {
    const tr = document.createElement('tr');
    const tdT = document.createElement('td'); tdT.className = 'text-nowrap text-muted'; tdT.textContent = fmtWt(o.worldTime, {short:true});
    const tdI = document.createElement('td'); tdI.textContent = o.icon ?? '🔧';
    const tdK = document.createElement('td');
    const b = document.createElement('span'); b.className = 'badge text-bg-info'; b.textContent = o.kind; tdK.appendChild(b);
    const tdO = document.createElement('td'); tdO.className = 'fw-semibold small'; tdO.textContent = o.objectId ?? '?';
    const tdL = document.createElement('td'); tdL.className = 'text-muted small'; tdL.textContent = o.locationId ?? '';
    [tdT, tdI, tdK, tdO, tdL].forEach(td => tr.appendChild(td)); tbody.appendChild(tr);
  }
  table.appendChild(tbody); el.appendChild(table);
}

export function renderLocMoves(containerId, contextEvents) {
  const el = document.getElementById(containerId);
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);

  if (!contextEvents.length) {
    const p = document.createElement('p'); p.className = 'text-muted small fst-italic m-3';
    p.textContent = 'No context changes (EventId 1200 not in logs).'; el.appendChild(p); return;
  }

  const table = document.createElement('table'); table.className = 'table table-sm mb-0 small';
  const tbody = document.createElement('tbody');
  for (const ev of contextEvents.slice(-50)) { // show last 50
    const tr = document.createElement('tr');
    const tdT = document.createElement('td'); tdT.className = 'text-nowrap text-muted'; tdT.textContent = fmtWt(ev.worldTime, {short:true});
    const tdL = document.createElement('td'); tdL.className = 'fw-semibold'; tdL.textContent = `📍 ${ev.location}`;
    const tdN = document.createElement('td'); tdN.className = 'text-muted small';
    if (ev.noise != null) tdN.textContent = `noise ${(ev.noise*100).toFixed(0)}%  crowd ${(ev.crowding*100).toFixed(0)}%`;
    [tdT, tdL, tdN].forEach(td => tr.appendChild(td)); tbody.appendChild(tr);
  }
  table.appendChild(tbody); el.appendChild(table);
}

// ── Goals tab ─────────────────────────────────────────────────────────────────

export const GOAL_CATEGORY_COLOR = {
  existential: '#6f42c1',
  survival:    '#dc3545',
  career:      '#fd7e14',
  relational:  '#e83e8c',
  other:       '#6c757d',
};
