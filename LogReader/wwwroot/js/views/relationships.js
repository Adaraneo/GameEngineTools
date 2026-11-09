// public/js/views/relationships.js
import * as api from '../api.js';
import * as charts from '../charts.js';
import * as timeline from '../timeline.js';
import { state } from '../core/state.js';
import { applyBrushFilter, buildRelCell, markBrushable } from '../core/dashboard.js';
import { capitalise, fmtWt, mkDiv } from '../core/util.js';

// ── Relationships: bidirectional view + semantic memory ───────────────────────
export function renderRelBidirectional(partnerId, partnerName, selfId) {
  const row  = document.getElementById('relBidirRow');
  const hdr  = document.getElementById('relBidirHeader');
  const cont = document.getElementById('relBidirContent');
  const sem  = document.getElementById('relSemanticMemory');
  if (!row || !cont || !sem) return;

  const selfProfile    = state.npcMapCache.get(selfId.toLowerCase());
  const partnerProfile = state.npcMapCache.get(partnerId.toLowerCase());

  // Own view of partner (from self's snapshot)
  const selfToPartner    = selfProfile?.snapshot?.relationships?.[partnerId] ?? null;
  // Partner's view of self (from partner's snapshot)
  const partnerToSelf    = partnerProfile?.snapshot?.relationships?.[selfId.toLowerCase()] ?? null;

  if (!selfToPartner && !partnerToSelf && !selfProfile?.snapshot?.semanticMemory?.[partnerId]) {
    row.classList.add('d-none'); return;
  }
  row.classList.remove('d-none');
  if (hdr) hdr.textContent = `${selfProfile?.displayName ?? selfId.slice(0,8)} ↔ ${partnerName}`;

  // Bidirectional comparison
  while (cont.firstChild) cont.removeChild(cont.firstChild);
  const DIMS = [
    { key:'like', label:'Like' }, { key:'trust', label:'Trust' },
    { key:'closeness', label:'Closeness' }, { key:'comfort', label:'Comfort' },
    { key:'respect', label:'Respect' }, { key:'intimateAffinity', label:'Intimate Affinity' },
    { key:'sexualInterest', label:'Sexual Interest' }, { key:'familiarity', label:'Familiarity' },
  ];
  const table = document.createElement('table'); table.className = 'table table-sm mb-0 small';
  const thead = document.createElement('thead'); thead.className = 'table-light';
  const hr = document.createElement('tr');
  const selfName    = selfProfile?.displayName ?? selfId.slice(0,8) + '…';
  const partnerNameShort = partnerName.length > 12 ? partnerName.slice(0,12)+'…' : partnerName;
  ['Dimension', `→ ${partnerNameShort}`, `← ${selfName.split(' ')[0]}`].forEach(h => {
    const th = document.createElement('th'); th.textContent = h; hr.appendChild(th);
  });
  thead.appendChild(hr); table.appendChild(thead);
  const tbody = document.createElement('tbody');
  for (const d of DIMS) {
    const vSelf    = selfToPartner?.[d.key];
    const vPartner = partnerToSelf?.[d.key];
    if (vSelf == null && vPartner == null) continue;
    const tr = document.createElement('tr');
    const tdL = document.createElement('td'); tdL.className = 'text-muted'; tdL.textContent = d.label;
    [tdL, buildRelCell(vSelf), buildRelCell(vPartner)].forEach(td => tr.appendChild(td));
    tbody.appendChild(tr);
  }
  table.appendChild(tbody); cont.appendChild(table);

  // Semantic memory
  while (sem.firstChild) sem.removeChild(sem.firstChild);
  const beliefs = selfProfile?.snapshot?.semanticMemory?.[partnerId] ?? {};
  const BELIEF_META = {
    emotionallySafe: { label:'Emotionally Safe', cls:'success', icon:'💚' },
    warm:            { label:'Warm',              cls:'success', icon:'🌟' },
    reliable:        { label:'Reliable',          cls:'success', icon:'🤝' },
    critical:        { label:'Critical',          cls:'danger',  icon:'❌' },
    rejecting:       { label:'Rejecting',         cls:'danger',  icon:'🚫' },
  };
  const beliefEntries = Object.entries(beliefs).filter(([,v]) => v != null && v > 0);
  if (!beliefEntries.length) {
    const p = document.createElement('p'); p.className = 'text-muted small fst-italic m-3';
    p.textContent = 'Žádná přesvědčení o tomto partnerovi.'; sem.appendChild(p);
  } else {
    const btable = document.createElement('table'); btable.className = 'table table-sm mb-0 small';
    const btbody = document.createElement('tbody');
    for (const [key, strength] of beliefEntries.sort((a,b) => b[1]-a[1])) {
      const meta = BELIEF_META[key] ?? { label: key, cls:'secondary', icon:'?' };
      const tr = document.createElement('tr');
      const tdI = document.createElement('td'); tdI.textContent = meta.icon;
      const tdN = document.createElement('td'); tdN.className = 'fw-semibold'; tdN.textContent = meta.label;
      const tdB = document.createElement('td'); tdB.style.minWidth='100px';
      const wrap = mkDiv('d-flex align-items-center gap-1');
      const prog = mkDiv('progress flex-grow-1'); prog.style.height='5px';
      const bar  = mkDiv(`progress-bar bg-${meta.cls}`); bar.style.width = Math.round(strength*100)+'%';
      prog.appendChild(bar); wrap.appendChild(prog);
      const sv = document.createElement('span'); sv.className='text-muted'; sv.textContent = Math.round(strength*100)+'%';
      wrap.appendChild(sv); tdB.appendChild(wrap);
      [tdI, tdN, tdB].forEach(td => tr.appendChild(td)); btbody.appendChild(tr);
    }
    btable.appendChild(btbody); sem.appendChild(btable);
  }
}

// ── Story tab ─────────────────────────────────────────────────────────────────

export function computeRelTrend(timelineData, n = 8) {
  if (timelineData.length < 3) return {};
  const recent = timelineData.slice(-n);
  const first  = recent[0];
  const last   = recent[recent.length - 1];
  const trend  = {};
  const DIMS = ['like','trust','closeness','comfort','respect','intimateAffinity','sexualInterest','familiarity'];
  for (const key of DIMS) {
    const f = first?.[key], l = last?.[key];
    if (f == null || l == null) continue;
    const delta = l - f;
    trend[key] = delta > 1.5 ? '↑' : delta < -1.5 ? '↓' : '→';
  }
  return trend;
}

// ── Profile: key moments ──────────────────────────────────────────────────────

export async function loadRelationships() {
  state.relData = await api.getRelationships(state.currentCharId);
  timeline.renderPartnerList('partnerList', state.relData.partners, state.npcMapCache, onPartnerSelect);
  if (state.relData.partners.length > 0) {
    const firstId = state.relData.partners[0];
    const firstName = state.npcMapCache.get(firstId.toLowerCase())?.displayName ?? firstId.slice(0, 8) + '…';
    onPartnerSelect(firstId, firstName);
  } else {
    charts.renderRelationshipChart('relChart', [], null);
    renderRelEventLog([], null);
    document.getElementById('relBidirRow')?.classList.add('d-none');
  }

  await loadRelationshipPairPicker();
}

// ── Per-pair tracking + breakup-risk prediction ────────────────────────────────
// Populates the subject/target dropdowns and renders the selected pair's chart +
// risk estimate. Subject defaults to the currently-selected character; target
// defaults to the first known pair (subject's outgoing edges only — edges are
// asymmetric, so the reverse direction is a separate pair the user can pick).
export async function loadRelationshipPairPicker() {
  const subjectSel = document.getElementById('pairSubjectSelect');
  const targetSel   = document.getElementById('pairTargetSelect');
  if (!subjectSel || !targetSel) return;

  // Subject options: every known character (mirrors the main character list).
  while (subjectSel.firstChild) subjectSel.removeChild(subjectSel.firstChild);
  for (const [pid, profile] of state.npcMapCache) {
    const opt = document.createElement('option');
    opt.value = pid;
    opt.textContent = profile?.displayName ?? pid.slice(0, 8) + '…';
    subjectSel.appendChild(opt);
  }
  subjectSel.value = state.currentCharId.toLowerCase();

  subjectSel.onchange = () => loadPairTargetsFor(subjectSel.value);
  targetSel.onchange  = () => loadRelationshipPairChart(subjectSel.value, targetSel.value);

  await loadPairTargetsFor(subjectSel.value);
}

export async function loadPairTargetsFor(subjectId) {
  const targetSel = document.getElementById('pairTargetSelect');
  if (!targetSel) return;
  while (targetSel.firstChild) targetSel.removeChild(targetSel.firstChild);

  let pairs = [];
  try {
    const res = await api.getRelationshipPairs(subjectId);
    pairs = res?.pairs ?? [];
  } catch (e) { /* leave empty — chart will show "no data" */ }

  for (const { to } of pairs) {
    const opt = document.createElement('option');
    opt.value = to;
    opt.textContent = state.npcMapCache.get(to)?.displayName ?? to.slice(0, 8) + '…';
    targetSel.appendChild(opt);
  }

  if (pairs.length > 0) {
    targetSel.value = pairs[0].to;
    await loadRelationshipPairChart(subjectId, pairs[0].to);
  } else {
    charts.renderRelationshipPairChart('relPairChart', []);
    renderBreakupRisk(null);
  }
}

export async function loadRelationshipPairChart(subjectId, targetId) {
  if (!subjectId || !targetId) return;
  let result;
  try {
    result = await api.getRelationshipPair(subjectId, targetId);
  } catch (e) {
    charts.renderRelationshipPairChart('relPairChart', []);
    renderBreakupRisk(null);
    return;
  }
  charts.renderRelationshipPairChart('relPairChart', result?.series ?? []);
  renderBreakupRisk(result?.risk ?? null);
}

/** Renders the "Riziko rozchodu" (breakup risk) number/bar from computeBreakupRisk()'s output. */
export function renderBreakupRisk(risk) {
  const valEl  = document.getElementById('breakupRiskValue');
  const barEl  = document.getElementById('breakupRiskBar');
  const detEl  = document.getElementById('breakupRiskDetail');
  if (!valEl || !barEl || !detEl) return;

  if (!risk) {
    valEl.textContent = '—';
    barEl.style.width = '0%';
    barEl.className = 'progress-bar';
    detEl.textContent = '';
    return;
  }

  const pct = Math.round(risk.risk);
  valEl.textContent = `${pct}%`;
  barEl.style.width = `${pct}%`;
  barEl.className = 'progress-bar ' + (pct >= 66 ? 'bg-danger' : pct >= 33 ? 'bg-warning' : 'bg-success');

  const flags = [];
  if (risk.dissolutionConsidered) flags.push('rozchod zvažován');
  if (risk.contemptDestroyed)     flags.push('pohrdání (nevratné)');
  detEl.textContent =
    `commitment=${risk.commitment.toFixed(0)}, investice=${risk.investmentSize.toFixed(0)}, ` +
    `alternativa=${risk.alternativeQuality.toFixed(0)}, zášť=${risk.transgressionResidue.toFixed(0)}` +
    (flags.length ? ` — ${flags.join(', ')}` : '');
}

export function onPartnerSelect(pid, name) {
  timeline.setActivePartner(pid);
  const timelineData = state.relData?.timeline?.[pid] ?? [];
  const events       = state.relData?.relEvents?.[pid] ?? [];

  renderRelSummary(pid, name, timelineData, events);
  charts.renderRelationshipChart('relChart', timelineData, name);
  renderRelEventLog(events, name);
  renderRelBidirectional(pid, name, state.currentCharId);

  const titleEl = document.getElementById('relChartTitle');
  if (titleEl) titleEl.textContent = name ? `Relationship timeline — ${name}` : 'Relationship timeline';
}

// ── buildRelationshipMatrix ───────────────────────────────────────────────────
export function buildRelationshipMatrix(snap, npcCache) {
  const rels = snap.relationships ?? {};
  const sem  = snap.semanticMemory ?? {};
  const entries = Object.entries(rels);
  if (!entries.length) return null;

  // Sort: alive first, then by average of like+trust+closeness desc
  entries.sort(([, a], [, b]) => {
    if (a.isContemptuouslyDestroyed !== b.isContemptuouslyDestroyed)
      return a.isContemptuouslyDestroyed ? 1 : -1;
    const avgA = ((a.like ?? 0) + (a.trust ?? 0) + (a.closeness ?? 0)) / 3;
    const avgB = ((b.like ?? 0) + (b.trust ?? 0) + (b.closeness ?? 0)) / 3;
    return avgB - avgA;
  });

  const wrapper = mkDiv('card mt-3');
  const hdr = mkDiv('card-header fw-semibold small d-flex align-items-center gap-2');
  const htxt = document.createElement('span'); htxt.textContent = 'Relationships';
  const hbadge = document.createElement('span'); hbadge.className = 'badge text-bg-secondary';
  hbadge.textContent = entries.length + ' partners';
  hdr.appendChild(htxt); hdr.appendChild(hbadge);

  const body = mkDiv('card-body p-0');
  const tableWrap = mkDiv('table-responsive');
  const table = document.createElement('table');
  table.className = 'table table-sm table-hover mb-0 snap-rel-table';

  const thead = document.createElement('thead'); thead.className = 'table-light';
  const hr = document.createElement('tr');
  ['Partner','Like','Trust','Close','Comfort','Respect','Romantic','Sexual','Familiar','Beliefs','Last Contact'].forEach(h => {
    const th = document.createElement('th'); th.textContent = h; hr.appendChild(th);
  });
  thead.appendChild(hr); table.appendChild(thead);

  const tbody = document.createElement('tbody');
  for (const [pid, rel] of entries) {
    const tr = document.createElement('tr');
    if (rel.isContemptuouslyDestroyed) tr.className = 'rel-row-destroyed';

    // Partner name
    const td0 = document.createElement('td');
    const npc = npcCache?.get(pid.toLowerCase());
    td0.textContent = npc?.displayName ?? (pid.slice(0,8) + '…');
    tr.appendChild(td0);

    // Numeric dimension cells
    const dims = [rel.like, rel.trust, rel.closeness, rel.comfort, rel.respect, rel.intimateAffinity, rel.sexualInterest, rel.familiarity];
    for (const v of dims) tr.appendChild(buildRelCell(v));

    // Beliefs cell
    const beliefsTd = document.createElement('td');
    const semData = sem[pid.toLowerCase()] ?? {};
    const positiveBeliefs = [['emotionallySafe','Emo. Safe'],['warm','Warm'],['reliable','Reliable']];
    const negativeBeliefs = [['critical','Critical'],['rejecting','Rejecting']];
    for (const [key, label] of positiveBeliefs) {
      if (semData[key] != null) {
        const pill = mkDiv('rel-belief-pill positive'); pill.textContent = label;
        beliefsTd.appendChild(pill);
      }
    }
    for (const [key, label] of negativeBeliefs) {
      if (semData[key] != null) {
        const pill = mkDiv('rel-belief-pill negative'); pill.textContent = label;
        beliefsTd.appendChild(pill);
      }
    }
    tr.appendChild(beliefsTd);

    // Last contact
    const lcTd = document.createElement('td');
    lcTd.className = 'text-muted';
    lcTd.textContent = rel.lastContactTime ? String(rel.lastContactTime).slice(0,10) : '—';
    tr.appendChild(lcTd);

    tbody.appendChild(tr);
  }
  table.appendChild(tbody);
  tableWrap.appendChild(table); body.appendChild(tableWrap);
  wrapper.appendChild(hdr); wrapper.appendChild(body);
  return wrapper;
}

// ── Relationship summary card ─────────────────────────────────────────────────
export const REL_DIM_LABELS = {
  like:                'Like',
  trust:               'Trust',
  closeness:           'Closeness',
  comfort:             'Comfort',
  respect:             'Respect',
  intimateAffinity:    'Intimate Affinity',
  sexualInterest:      'Sexual Interest',
  familiarity:         'Familiarity',
  aestheticAttraction: 'Aesthetic Attraction',
  physicalAttraction:  'Physical Attraction',
};

export const REL_DIM_COLORS = {
  like:                '#198754',
  trust:               '#0d6efd',
  closeness:           '#6f42c1',
  comfort:             '#fd7e14',
  respect:             '#dc3545',
  intimateAffinity:    '#e83e8c',
  sexualInterest:      '#c62828',
  familiarity:         '#ffc107',
  aestheticAttraction: '#20c997',
  physicalAttraction:  '#6610f2',
};

export function renderRelSummary(pid, name, timelineData, events) {
  const card    = document.getElementById('relSummaryCard');
  const pillsEl = document.getElementById('relStatPills');
  const dimsEl  = document.getElementById('relCurrentDims');
  if (!card || !pillsEl || !dimsEl) return;

  if (!timelineData.length && !events.length) {
    card.classList.add('d-none');
    return;
  }
  card.classList.remove('d-none');

  // ── Stat pills ───────────────────────────────────────────────────────────
  while (pillsEl.firstChild) pillsEl.removeChild(pillsEl.firstChild);

  const interactionEvents = events.filter(e => e.kind === 'event' || e.kind === 'interaction_outcome');
  const accepted  = interactionEvents.filter(e => e.outcome === 'accepted' || e.result === 'accepted').length;
  const total     = interactionEvents.length;
  const acceptPct = total > 0 ? Math.round(accepted / total * 100) : null;

  const firstWt = timelineData[0]?.worldTime;
  const lastWt  = timelineData[timelineData.length - 1]?.worldTime;

  const pills = [
    total > 0 ? { label: `${total} interactions`, cls: 'primary' } : null,
    acceptPct != null ? { label: `${acceptPct}% accepted`, cls: acceptPct >= 50 ? 'success' : 'warning' } : null,
    firstWt ? { label: `from ${firstWt.slice(0, 10)}`, cls: 'secondary' } : null,
    lastWt  ? { label: `to ${lastWt.slice(0, 10)}`,   cls: 'secondary' } : null,
  ].filter(Boolean);

  for (const p of pills) {
    const b = document.createElement('span');
    b.className = `badge text-bg-${p.cls}`;
    b.textContent = p.label;
    pillsEl.appendChild(b);
  }

  // ── Current dimension bars ───────────────────────────────────────────────
  while (dimsEl.firstChild) dimsEl.removeChild(dimsEl.firstChild);

  // Get most recent non-null value for each dimension
  const latest = {};
  for (const point of timelineData) {
    for (const [key, val] of Object.entries(point)) {
      if (key === 'worldTime') continue;
      if (val != null) latest[key] = val;
    }
  }

  const dimKeys = Object.keys(REL_DIM_LABELS).filter(k => latest[k] != null);
  if (!dimKeys.length) return;

  // Compute trends from timeline
  const trend = computeRelTrend(timelineData);

  const grid = document.createElement('div');
  grid.className = 'row g-0';
  for (const key of dimKeys) {
    const val  = latest[key];
    const pct  = Math.min(100, Math.max(0, val));
    const col  = document.createElement('div');
    col.className = 'col-6';
    const row  = document.createElement('div');
    row.className = 'rel-dim-row d-flex align-items-center gap-1';

    const lbl = document.createElement('span');
    lbl.className = 'rel-dim-label';
    lbl.textContent = REL_DIM_LABELS[key] ?? key;
    // Trend arrow
    if (trend[key]) {
      const arrow = document.createElement('span');
      arrow.className = trend[key] === '↑' ? 'rel-trend-up' : trend[key] === '↓' ? 'rel-trend-down' : 'rel-trend-flat';
      arrow.textContent = trend[key];
      lbl.appendChild(arrow);
    }

    const barWrap = document.createElement('div');
    barWrap.className = 'rel-dim-bar';
    const prog = document.createElement('div'); prog.className = 'progress'; prog.style.height = '6px';
    const bar  = document.createElement('div'); bar.className = 'progress-bar';
    bar.style.width           = pct + '%';
    bar.style.backgroundColor = REL_DIM_COLORS[key] ?? '#6c757d';
    bar.title = `${val.toFixed(1)}`;
    prog.appendChild(bar); barWrap.appendChild(prog);

    const valEl = document.createElement('span');
    valEl.className = 'rel-dim-value'; valEl.textContent = val.toFixed(0);

    row.appendChild(lbl); row.appendChild(barWrap); row.appendChild(valEl);
    col.appendChild(row); grid.appendChild(col);
  }
  dimsEl.appendChild(grid);
}

export function renderRelEventLog(events, partnerName) {
  const el  = document.getElementById('relEventLog');
  const hdr = document.getElementById('relEventLogHeader');
  if (!el) return;
  if (hdr) hdr.textContent = `Interaction History${partnerName ? ' — ' + partnerName : ''}`;
  while (el.firstChild) el.removeChild(el.firstChild);

  if (!events.length) {
    const p = document.createElement('p');
    p.className = 'text-muted small fst-italic m-3';
    p.textContent = 'No relationship events found for this partner.';
    el.appendChild(p);
    return;
  }

  const table = document.createElement('table');
  table.className = 'table table-sm table-hover mb-0 small';
  const tbody = document.createElement('tbody');

  for (const ev of events) {
    const tr = document.createElement('tr');
    markBrushable(tr, ev.worldTime);

    // World time
    const tdTime = document.createElement('td');
    tdTime.className = 'text-nowrap text-muted';
    tdTime.style.width = '160px';
    tdTime.textContent = fmtWt(ev.worldTime);

    // Event kind badge
    const tdKind = document.createElement('td');
    tdKind.style.width = '160px';
    const badge = document.createElement('span');
    const { icon, label, cls } = relEventMeta(ev);
    badge.className = `badge text-bg-${cls}`;
    badge.textContent = `${icon} ${label}`;
    tdKind.appendChild(badge);

    // Outcome badge (for interactions)
    const tdOutcome = document.createElement('td');
    tdOutcome.style.width = '90px';
    if (ev.outcome) {
      const ob = document.createElement('span');
      ob.className = `badge text-bg-${ev.outcome === 'accepted' ? 'success' : 'danger'}`;
      ob.textContent = ev.outcome;
      tdOutcome.appendChild(ob);
    }
    if (ev.role) {
      const rb = document.createElement('span');
      rb.className = 'badge text-bg-secondary ms-1';
      rb.textContent = ev.role;
      tdOutcome.appendChild(rb);
    }

    // Changes / values
    const tdChanges = document.createElement('td');
    tdChanges.className = 'text-muted';
    tdChanges.textContent = formatRelChanges(ev);

    tr.appendChild(tdTime);
    tr.appendChild(tdKind);
    tr.appendChild(tdOutcome);
    tr.appendChild(tdChanges);
    tbody.appendChild(tr);
  }
  table.appendChild(tbody);
  el.appendChild(table);
  applyBrushFilter();   // honour an active brush for freshly rendered rows
}

export function relEventMeta(ev) {
  switch (ev.kind) {
    case 'event':              return { icon: ev.act ? actIcon(ev.act) : '💬', label: ev.act ?? ev.eventType ?? 'Event',   cls: 'primary'   };
    case 'apply':              return { icon: '📊',  label: 'Changes',                                                      cls: 'info'      };
    case 'new_edge':           return { icon: '🆕',  label: 'New relation',                                                  cls: 'success'   };
    case 'snapshot':           return { icon: '📷',  label: 'Snapshot',                                                      cls: 'secondary' };
    case 'decay':              return { icon: '📉',  label: 'Decay',                                                         cls: 'warning'   };
    case 'first_impression':   return { icon: '👁️', label: 'First impression',                                               cls: 'dark'      };
    case 'interaction_outcome':return { icon: ev.icon ?? '💬', label: ev.label ?? 'Interaction',  cls: ev.level ?? 'info' };
    case 'sexual_outcome':     return { icon: ev.icon ?? '💕', label: ev.label ?? 'Sexual enc.',   cls: ev.level ?? 'danger' };
    case 'decay':              return { icon: '📉', label: 'Decay detail',                          cls: 'secondary' };
    case 'milestone':          return { icon: ev.icon ?? '🏆', label: ev.label ?? 'Milestone',     cls: 'success' };
    case 'third_party':        return { icon: '👀', label: 'Third-party rep.',                      cls: 'info' };
    case 'jealousy':           return { icon: '😤', label: 'Jealousy distress',                    cls: 'danger' };
    case 'misattribution':     return { icon: '🌀', label: 'Misattribution',                       cls: 'warning' };
    case 'investment':         return { icon: ev.icon ?? '🔗', label: ev.label ?? 'Investment model', cls: ev.level ?? 'info' };
    case 'move_failed':        return { icon: ev.icon ?? '⚠️', label: ev.label ?? 'Move failed',      cls: ev.level ?? 'warning' };
    // Generic fallback: honour the parser-supplied icon/label/level before giving up.
    default:                   return { icon: ev.icon ?? '❓', label: ev.label ?? ev.kind ?? '?',     cls: ev.level ?? 'secondary' };
  }
}

export function actIcon(act) {
  const map = { SmallTalk:'💬', Question:'❓', Compliment:'🌟', Touch:'🤝',
                Humor:'😄', Gossip:'🗣️', Help:'🤲', Criticism:'😤',
                Apology:'🙏', Gift:'🎁', Flirt:'💕', Withdraw:'🚶' };
  return map[act] ?? '💬';
}

export function formatRelChanges(ev) {
  // Social comparison (2016): show esteem/mood/achievement/hostility deltas
  if (ev.kind === 'social_comparison') {
    const parts = [];
    if (ev.selfEsteemDelta     != null) parts.push(`esteem ${ev.selfEsteemDelta >= 0 ? '+' : ''}${ev.selfEsteemDelta.toFixed(3)}`);
    if (ev.moodValenceDelta    != null) parts.push(`valence ${ev.moodValenceDelta >= 0 ? '+' : ''}${ev.moodValenceDelta.toFixed(3)}`);
    if (ev.moodBaselineDelta   != null && ev.moodBaselineDelta !== 0) parts.push(`mood base ${ev.moodBaselineDelta >= 0 ? '+' : ''}${ev.moodBaselineDelta.toFixed(1)}`);
    if (ev.achievementMotivationDelta != null && ev.achievementMotivationDelta !== 0) parts.push(`achieve ${ev.achievementMotivationDelta >= 0 ? '+' : ''}${ev.achievementMotivationDelta.toFixed(1)}`);
    if (ev.targetHostilityDelta != null && ev.targetHostilityDelta !== 0) parts.push(`hostility +${ev.targetHostilityDelta.toFixed(2)}`);
    return parts.join('  ');
  }
  // Reputation prior (2017): show prior score and trust shift
  if (ev.kind === 'reputation_prior') {
    const parts = [];
    if (ev.reputationPrior != null) parts.push(`prior=${ev.reputationPrior.toFixed(3)}`);
    if (ev.trustShift      != null) parts.push(`trustShift=${ev.trustShift >= 0 ? '+' : ''}${ev.trustShift.toFixed(1)}`);
    return parts.join('  ');
  }
  if (ev.changes) {
    return Object.entries(ev.changes).map(([dim, v]) => {
      if (typeof v === 'object' && v !== null && 'from' in v) {
        const delta = v.to - v.from;
        const sign  = delta >= 0 ? '+' : '';
        return `${capitalise(dim)}: ${v.from.toFixed(1)}→${v.to.toFixed(1)} (${sign}${delta.toFixed(1)})`;
      }
      return `${capitalise(dim)}: ${v}`;
    }).join('  ');
  }
  if (ev.values) {
    return Object.entries(ev.values).map(([dim, v]) => `${capitalise(dim)}: ${v.toFixed(1)}`).join('  ');
  }
  if (ev.like != null) return `Like: ${ev.like.toFixed(1)}${ev.attraction != null ? ', Attraction: ' + ev.attraction.toFixed(1) : ''}`;
  if (ev.kind === 'investment') {
    const parts = [];
    if (ev.commitment != null) parts.push(`commitment ${ev.commitment.toFixed(1)}${ev.target != null ? '→' + ev.target.toFixed(1) : ''}`);
    if (ev.investment != null) parts.push(`investice ${ev.investment.toFixed(1)}`);
    if (ev.alternative != null) parts.push(`alternativa ${ev.alternative.toFixed(1)}`);
    return parts.join('  ');
  }
  if (ev.kind === 'move_failed') return ev.category ? `category: ${ev.category}` : (ev.type ? `type: ${ev.type}${ev.fromLocation ? ', from: ' + ev.fromLocation : ''}` : '');
  return '';
}
