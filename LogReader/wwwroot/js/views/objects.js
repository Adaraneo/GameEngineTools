// public/js/views/objects.js
import { fmtWt, mkDiv } from '../core/util.js';

// ── Object usage log (EventId 1500) ──────────────────────────────────────────
export function renderObjectUsedLog(containerId, events) {
  const el = document.getElementById(containerId);
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);

  if (!events?.length) {
    const p = document.createElement('p'); p.className = 'text-muted small fst-italic m-3';
    p.textContent = 'No object usage events (EventId 1500 not in logs).'; el.appendChild(p); return;
  }

  // Summary row: counts per affordance type
  const byAffordance = {};
  for (const ev of events) {
    const key = ev.affordances ?? 'unknown';
    byAffordance[key] = (byAffordance[key] ?? 0) + 1;
  }
  const summaryRow = mkDiv('d-flex flex-wrap gap-2 p-2 border-bottom');
  for (const [aff, cnt] of Object.entries(byAffordance).sort((a, b) => b[1] - a[1])) {
    const b = document.createElement('span');
    b.className = 'badge text-bg-secondary';
    b.textContent = `${aff}: ${cnt}×`;
    summaryRow.appendChild(b);
  }
  el.appendChild(summaryRow);

  const table = document.createElement('table');
  table.className = 'table table-sm table-hover mb-0 small';
  const tbody = document.createElement('tbody');

  for (const ev of events) {
    const tr = document.createElement('tr');

    const tdT = document.createElement('td'); tdT.className = 'text-nowrap text-muted'; tdT.style.width = '155px';
    tdT.textContent = fmtWt(ev.worldTime);

    const tdB = document.createElement('td'); tdB.style.width = '180px';
    const badge = document.createElement('span');
    badge.className = `badge text-bg-${ev.level ?? 'secondary'}`;
    badge.textContent = `${ev.icon ?? '📦'} ${ev.displayName ?? ev.objectId}`;
    tdB.appendChild(badge);

    const tdD = document.createElement('td'); tdD.className = 'text-muted small';
    const parts = [`sat ${ev.satisfaction?.toFixed(2) ?? '?'}`];
    if (ev.consumed) parts.push('💨 consumed');
    if (ev.locationId) parts.push(`@ ${ev.locationId}`);
    tdD.textContent = parts.join(' · ');

    tr.appendChild(tdT); tr.appendChild(tdB); tr.appendChild(tdD);
    tbody.appendChild(tr);
  }
  table.appendChild(tbody); el.appendChild(table);
}

// ── Object respawn log (EventId 1501) ─────────────────────────────────────────
export function renderObjectRespawnLog(containerId, events) {
  const el = document.getElementById(containerId);
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);

  if (!events?.length) {
    const p = document.createElement('p'); p.className = 'text-muted small fst-italic m-3';
    p.textContent = 'No respawn events (EventId 1501 not in logs).'; el.appendChild(p); return;
  }

  const table = document.createElement('table');
  table.className = 'table table-sm table-hover mb-0 small';
  const tbody = document.createElement('tbody');

  for (const ev of events) {
    const tr = document.createElement('tr');

    const tdT = document.createElement('td'); tdT.className = 'text-nowrap text-muted'; tdT.style.width = '155px';
    tdT.textContent = fmtWt(ev.worldTime);

    const tdB = document.createElement('td'); tdB.style.width = '180px';
    const badge = document.createElement('span');
    badge.className = 'badge text-bg-secondary';
    badge.textContent = `♻️ ${ev.displayName ?? ev.objectId}`;
    tdB.appendChild(badge);

    const tdD = document.createElement('td'); tdD.className = 'text-muted small';
    const parts = [];
    if (ev.elapsedMinutes != null) {
      const h = Math.floor(ev.elapsedMinutes / 60);
      const m = Math.round(ev.elapsedMinutes % 60);
      parts.push(`after ${h > 0 ? h + 'h ' : ''}${m}min`);
    }
    if (ev.locationId) parts.push(`@ ${ev.locationId}`);
    tdD.textContent = parts.join(' · ');

    tr.appendChild(tdT); tr.appendChild(tdB); tr.appendChild(tdD);
    tbody.appendChild(tr);
  }
  table.appendChild(tbody); el.appendChild(table);
}
