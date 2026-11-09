// public/js/views/goals.js
import * as api from '../api.js';
import * as charts from '../charts.js';
import { state } from '../core/state.js';
import { fmtWt, mkDiv } from '../core/util.js';
import { GOAL_CATEGORY_COLOR } from './locations.js';

export async function loadGoals() {
  const data = await api.getGoals(state.currentCharId);
  // data = { events, timelines, activeGoals: { active, resolved } }

  renderGoalsActive('goalsActiveRow', data.activeGoals?.active ?? []);
  await new Promise(r => requestAnimationFrame(r));
  charts.renderGoalsChart('goalsChart', data.timelines ?? {});
  renderGoalsEventLog('goalsEventLog', data.events ?? []);
  renderGoalsResolved('goalsResolved', data.activeGoals?.resolved ?? []);
}

export function renderGoalsActive(containerId, activeGoals) {
  const row = document.getElementById(containerId);
  if (!row) return;
  while (row.firstChild) row.removeChild(row.firstChild);

  if (!activeGoals.length) {
    const p = document.createElement('p');
    p.className = 'col-12 text-muted small fst-italic';
    p.textContent = 'No active goals found (EventId 1300 not in logs or all goals resolved).';
    row.appendChild(p); return;
  }

  for (const g of activeGoals) {
    const col  = mkDiv('col-md-4 col-lg-3');
    const card = mkDiv('card h-100');
    card.style.borderLeft = `4px solid ${GOAL_CATEGORY_COLOR[g.category] ?? '#6c757d'}`;

    const body = mkDiv('card-body py-2 px-3');

    // Header: icon + kind + origin badge
    const hdr = mkDiv('d-flex align-items-center gap-2 mb-2');
    const iconEl = document.createElement('span'); iconEl.style.fontSize = '1.4rem'; iconEl.textContent = g.icon ?? '🎯';
    const kindEl = mkDiv('fw-semibold'); kindEl.textContent = g.label ?? g.kind;
    const originB = document.createElement('span');
    originB.className = 'badge text-bg-secondary ms-auto'; originB.style.fontSize = '0.6rem';
    originB.textContent = g.origin ?? '';
    hdr.appendChild(iconEl); hdr.appendChild(kindEl); hdr.appendChild(originB);
    body.appendChild(hdr);

    // Salience bar
    if (g.salience != null) {
      const sl = mkDiv('mb-1');
      const slLbl = document.createElement('small'); slLbl.className = 'text-muted'; slLbl.textContent = `Salience: ${(g.salience * 100).toFixed(0)}%`;
      const prog = mkDiv('progress mt-1'); prog.style.height = '6px';
      const bar  = mkDiv('progress-bar'); bar.style.width = (g.salience * 100) + '%';
      bar.style.backgroundColor = GOAL_CATEGORY_COLOR[g.category] ?? '#6c757d';
      prog.appendChild(bar); sl.appendChild(slLbl); sl.appendChild(prog); body.appendChild(sl);
    }

    // Progress bar
    if (g.progress != null) {
      const pr = mkDiv('mb-1');
      const prLbl = document.createElement('small'); prLbl.className = 'text-muted'; prLbl.textContent = `Progress: ${(g.progress * 100).toFixed(0)}%`;
      const prog = mkDiv('progress mt-1'); prog.style.height = '6px';
      const bar  = mkDiv('progress-bar bg-success'); bar.style.width = (g.progress * 100) + '%';
      prog.appendChild(bar); pr.appendChild(prLbl); pr.appendChild(prog); body.appendChild(pr);
    }

    // Since
    if (g.activatedAt) {
      const since = document.createElement('small'); since.className = 'text-muted d-block mt-1';
      since.textContent = `Since: ${fmtWt(g.activatedAt)}`;
      body.appendChild(since);
    }

    card.appendChild(body); col.appendChild(card); row.appendChild(col);
  }
}

export function renderGoalsEventLog(containerId, events) {
  const el = document.getElementById(containerId);
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);

  const relevant = events.filter(e => ['activated','seeded','progressed','resolved','bias'].includes(e.type));
  if (!relevant.length) {
    const p = document.createElement('p');
    p.className = 'text-muted small fst-italic m-3';
    p.textContent = 'No goal events found (EventId 1300-1304 not in logs).';
    el.appendChild(p); return;
  }

  const TYPE_CFG = {
    activated:  { badge: 'success',   label: 'Activated'  },
    seeded:     { badge: 'info',      label: 'Seeded'     },
    progressed: { badge: 'primary',   label: 'Progressed' },
    resolved:   { badge: 'secondary', label: 'Resolved'   },
    bias:       { badge: 'warning',   label: 'Bias'       },
  };

  const table = document.createElement('table');
  table.className = 'table table-sm table-hover mb-0 small';
  const tbody = document.createElement('tbody');

  for (const ev of relevant) {
    const cfg = TYPE_CFG[ev.type] ?? { badge:'secondary', label: ev.type };
    const tr  = document.createElement('tr');

    const tdT = document.createElement('td'); tdT.className = 'text-nowrap text-muted';
    tdT.textContent = fmtWt(ev.worldTime);

    const tdB = document.createElement('td'); tdB.style.width = '90px';
    const b = document.createElement('span'); b.className = `badge text-bg-${cfg.badge}`;
    b.textContent = cfg.label; tdB.appendChild(b);

    const tdK = document.createElement('td'); tdK.className = 'fw-semibold';
    tdK.textContent = `${ev.icon ?? ''} ${ev.label ?? ev.kind}`;

    const tdD = document.createElement('td'); tdD.className = 'text-muted small';
    const parts = [];
    if (ev.type === 'activated' || ev.type === 'seeded')
      parts.push(`salience ${(ev.salience ?? 0).toFixed(2)}`);
    if (ev.type === 'progressed')
      parts.push(`salience ${ev.salienceOld?.toFixed(2)}→${ev.salienceNew?.toFixed(2)}`, `progress ${ev.progressOld?.toFixed(2)}→${ev.progressNew?.toFixed(2)}`);
    if (ev.type === 'resolved')
      parts.push(`${ev.resolutionIcon ?? ''} ${ev.resolution}`, `progress ${ev.progress?.toFixed(2)}`, `frustration ${ev.frustration?.toFixed(2)}`);
    if (ev.type === 'bias')
      parts.push(`+${ev.bias?.toFixed(2)} on ${ev.action}`);
    tdD.textContent = parts.join(' · ');

    tr.appendChild(tdT); tr.appendChild(tdB); tr.appendChild(tdK); tr.appendChild(tdD);
    tbody.appendChild(tr);
  }
  table.appendChild(tbody); el.appendChild(table);
}

export function renderGoalsResolved(containerId, resolvedGoals) {
  const el = document.getElementById(containerId);
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);

  if (!resolvedGoals.length) {
    const p = document.createElement('p');
    p.className = 'text-muted small fst-italic m-3';
    p.textContent = 'No resolved goals yet.';
    el.appendChild(p); return;
  }

  const table = document.createElement('table');
  table.className = 'table table-sm table-hover mb-0 small';
  const tbody = document.createElement('tbody');

  for (const g of resolvedGoals) {
    const tr = document.createElement('tr');

    const tdK = document.createElement('td'); tdK.className = 'fw-semibold';
    tdK.textContent = `${g.icon ?? ''} ${g.label ?? g.kind}`;

    const tdR = document.createElement('td');
    const rb = document.createElement('span');
    rb.className = `badge text-bg-${g.resolutionCls ?? 'secondary'}`;
    rb.textContent = `${g.resolutionIcon ?? ''} ${g.resolution ?? ''}`;
    tdR.appendChild(rb);

    const tdP = document.createElement('td'); tdP.className = 'text-muted small';
    const parts = [];
    if (g.progress    != null) parts.push(`progress ${(g.progress * 100).toFixed(0)}%`);
    if (g.frustration != null) parts.push(`frustration ${(g.frustration * 100).toFixed(0)}%`);
    tdP.textContent = parts.join(' · ');

    const tdT = document.createElement('td'); tdT.className = 'text-nowrap text-muted small';
    tdT.textContent = fmtWt(g.resolvedAt, {dateOnly:true});

    tr.appendChild(tdK); tr.appendChild(tdR); tr.appendChild(tdP); tr.appendChild(tdT);
    tbody.appendChild(tr);
  }
  table.appendChild(tbody); el.appendChild(table);
}
