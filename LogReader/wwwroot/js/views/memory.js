// public/js/views/memory.js
import * as api from '../api.js';
import { state } from '../core/state.js';
import { applyBrushFilter, markBrushable } from '../core/dashboard.js';
import { fmtWt } from '../core/util.js';
import { renderPhysioEventList } from './physio.js';

// ── Memory tab ────────────────────────────────────────────────────────────────
export async function loadMemory() {
  const data = await api.getMemory(state.currentCharId);
  renderMemoryEncoded('memEncodedList',       data.encoded        ?? []);
  renderPhysioEventList('memConsolidatedList',data.consolidated   ?? [],
    'No consolidation events (EventId 3001 not in logs).');
  // New: reconsolidation drifts and belief updates
  renderMemoryReconsolidated('memReconsolidatedList', data.reconsolidated ?? []);
  renderBeliefUpdates('memBeliefList', data.beliefUpdates ?? []);
}

export function renderMemoryReconsolidated(containerId, events) {
  const el = document.getElementById(containerId);
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);
  if (!events.length) {
    const p = document.createElement('p'); p.className = 'text-muted small fst-italic m-3';
    p.textContent = 'No reconsolidation events (EventId 3002 not in logs).'; el.appendChild(p); return;
  }
  const table = document.createElement('table'); table.className = 'table table-sm table-hover mb-0 small';
  const tbody = document.createElement('tbody');
  for (const ev of events) {
    const tr = document.createElement('tr');
    const tdT = document.createElement('td'); tdT.className = 'text-nowrap text-muted'; tdT.textContent = fmtWt(ev.worldTime, {short:true});
    const tdI = document.createElement('td'); tdI.textContent = ev.icon ?? '🔄';
    const tdTag = document.createElement('td'); tdTag.className = 'text-muted small'; tdTag.textContent = ev.tag ?? '?';
    const tdEmo = document.createElement('td');
    const b = document.createElement('span'); b.className = 'small';
    b.textContent = `${ev.fromEmotion} → ${ev.toEmotion}  drift ${(ev.drift * 100).toFixed(1)}%`;
    tdEmo.appendChild(b);
    [tdT, tdI, tdTag, tdEmo].forEach(td => tr.appendChild(td)); tbody.appendChild(tr);
  }
  table.appendChild(tbody); el.appendChild(table);
}

export function renderBeliefUpdates(containerId, events) {
  const el = document.getElementById(containerId);
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);
  if (!events.length) {
    const p = document.createElement('p'); p.className = 'text-muted small fst-italic m-3';
    p.textContent = 'No belief update events (EventId 3003 not in logs).'; el.appendChild(p); return;
  }
  const table = document.createElement('table'); table.className = 'table table-sm table-hover mb-0 small';
  const tbody = document.createElement('tbody');
  for (const ev of events) {
    const tr = document.createElement('tr');
    const tdT = document.createElement('td'); tdT.className = 'text-nowrap text-muted'; tdT.textContent = fmtWt(ev.worldTime, {short:true});
    const tdI = document.createElement('td'); tdI.textContent = ev.icon ?? '?';
    const tdP = document.createElement('td'); tdP.className = 'fw-semibold small';
    tdP.textContent = state.npcMapCache.get(ev.partnerId)?.displayName ?? (ev.partnerId?.slice(0,8) + '…');
    const tdK = document.createElement('td');
    const badge = document.createElement('span'); badge.className = `badge text-bg-${ev.level ?? 'secondary'}`;
    badge.textContent = ev.beliefKind ?? '?'; tdK.appendChild(badge);
    const tdV = document.createElement('td'); tdV.className = 'text-muted small';
    const delta = ev.delta;
    tdV.textContent = `${ev.oldStrength?.toFixed(2)} → ${ev.newStrength?.toFixed(2)} (${delta >= 0 ? '+' : ''}${delta?.toFixed(2)}) ev:${ev.evidenceCount}`;
    [tdT, tdI, tdP, tdK, tdV].forEach(td => tr.appendChild(td)); tbody.appendChild(tr);
  }
  table.appendChild(tbody); el.appendChild(table);
}

export function renderMemoryEncoded(containerId, episodes) {
  const el = document.getElementById(containerId);
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);

  if (!episodes.length) {
    const p = document.createElement('p');
    p.className = 'text-muted small fst-italic m-3';
    p.textContent = 'No encoded episodes found (EventId 3000 not in logs).';
    el.appendChild(p);
    return;
  }

  const table = document.createElement('table');
  table.className = 'table table-sm table-hover mb-0 small';
  const tbody = document.createElement('tbody');

  for (const ep of episodes) {
    const tr = document.createElement('tr');
    markBrushable(tr, ep.worldTime);

    // Time
    const tdTime = document.createElement('td');
    tdTime.className = 'text-nowrap text-muted'; tdTime.style.width = '160px';
    tdTime.textContent = fmtWt(ep.worldTime);

    // Emotion badge
    const tdEmo = document.createElement('td'); tdEmo.style.width = '110px';
    const badge = document.createElement('span');
    badge.className = `badge text-bg-${ep.level ?? 'secondary'}`;
    badge.textContent = `${ep.icon ?? '💭'} ${ep.emotion ?? '?'}`;
    tdEmo.appendChild(badge);

    // Salience bar
    const tdSal = document.createElement('td'); tdSal.style.width = '80px';
    const pct = Math.round((ep.salience ?? 0) * 100);
    const prog = document.createElement('div'); prog.className = 'progress'; prog.style.height = '6px';
    const bar  = document.createElement('div'); bar.className = 'progress-bar';
    bar.style.width = pct + '%'; bar.title = `salience ${ep.salience?.toFixed(2)}`;
    prog.appendChild(bar); tdSal.appendChild(prog);

    // Tag — actual vs. perceived when the encoding was distorted
    const tdTag = document.createElement('td'); tdTag.className = 'text-muted';
    if (ep.distorted) {
      const actual = document.createElement('span');
      actual.className = 'text-decoration-line-through opacity-75';
      actual.textContent = ep.tag ?? '';
      const arrow = document.createElement('span'); arrow.textContent = ' → ';
      const perc = document.createElement('span');
      perc.className = 'fw-semibold';
      perc.textContent = ep.perceivedWhat ?? ep.tag ?? '';
      const kindColor = { Threat: 'danger', Slight: 'warning', Warmth: 'success' }[ep.distortionKind] ?? 'secondary';
      const dist = document.createElement('span');
      dist.className = `badge text-bg-${kindColor} ms-2`;
      dist.textContent = `⚠ ${ep.distortionKind}${ep.distortion != null ? ' ' + ep.distortion.toFixed(2) : ''}`;
      dist.title = 'Encoding distorted by stress × emotional weight — the remembered version diverges from what actually happened.';
      tdTag.append(actual, arrow, perc, dist);
    } else {
      tdTag.textContent = ep.tag ?? '';
    }

    tr.appendChild(tdTime); tr.appendChild(tdEmo);
    tr.appendChild(tdSal);  tr.appendChild(tdTag);
    tbody.appendChild(tr);
  }
  table.appendChild(tbody);
  el.appendChild(table);
  applyBrushFilter();   // honour an active brush for freshly rendered rows
}

// ── Interactions tab ──────────────────────────────────────────────────────────
