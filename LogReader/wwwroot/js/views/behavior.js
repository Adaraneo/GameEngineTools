// public/js/views/behavior.js
import * as api from '../api.js';
import * as charts from '../charts.js';
import { state } from '../core/state.js';
import { applyBrushFilter, markBrushable } from '../core/dashboard.js';
import { fmtWt, mkDiv } from '../core/util.js';

export async function loadBehavior() {
  const [data, goalsData, schedData] = await Promise.all([
    api.getBehavior(state.currentCharId),
    api.getGoals(state.currentCharId).catch(() => ({ events: [] })),
    api.getSchedule(state.currentCharId).catch(() => ({ events: [] })),
  ]);
  const snaps   = data.snapshots ?? [];
  const actions = data.actions   ?? [];
  const habits  = data.habits    ?? [];
  const intents = data.intents   ?? [];

  // Build goal-bias index: action → [{worldTime, kind, bias, salience}]
  const biasByAction = {};
  for (const ev of (goalsData.events ?? []).filter(e => e.type === 'bias')) {
    if (!ev.action) continue;
    (biasByAction[ev.action] ??= []).push(ev);
  }

  // ── Driving forces chart ─────────────────────────────────────────────────
  await new Promise(r => requestAnimationFrame(r));
  charts.renderBehaviorChart('behavChart', snaps);

  // ── Stats ────────────────────────────────────────────────────────────────
  renderBehaviorStats('behavStatsRow', actions, intents, habits, snaps);

  // ── Action log with need context + goal bias ──────────────────────────────
  renderBehaviorActionLog('behavActionLog', 'behavActionDistribution', actions, snaps, biasByAction);

  // ── Intent history ───────────────────────────────────────────────────────
  renderBehaviorIntentLog('behavIntentLog', intents);

  // ── Habit analysis (log events) ───────────────────────────────────────────
  renderBehaviorHabits('behavHabitLog', habits);

  // ── Daily schedule ────────────────────────────────────────────────────────
  renderScheduleLog('behavScheduleLog', schedData.events ?? []);

  // ── Behavioral profile from snapshot ─────────────────────────────────────
  const profile = state.npcMapCache.get(state.currentCharId.toLowerCase());
  renderBehaviorHabitTraces('behavHabitTraces', profile?.snapshot?.behavior?.habitTraces ?? []);
  renderBehaviorCooldowns('behavCooldowns', profile?.snapshot?.behavior?.cooldowns ?? null);

  // ── Values & Guilt events ─────────────────────────────────────────────────
  const valuesEvts = data.valuesEvents ?? [];
  const valuesCard = document.getElementById('valuesCard');
  if (valuesEvts.length > 0) {
    valuesCard?.classList.remove('d-none');
    renderValuesEventLog('valuesEventLog', valuesEvts);
  } else {
    valuesCard?.classList.add('d-none');
  }

  // ── Dark Core modifier events ─────────────────────────────────────────────
  const darkCoreEvts = data.darkCoreEvents ?? [];
  const darkCoreCard = document.getElementById('darkCoreCard');
  if (darkCoreEvts.length > 0) {
    darkCoreCard?.classList.remove('d-none');
    renderDarkCoreEventLog('darkCoreEventLog', darkCoreEvts);
  } else {
    darkCoreCard?.classList.add('d-none');
  }

  // ── Travel / transit events ───────────────────────────────────────────────
  const travelEvts = data.travelEvents ?? [];
  const travelCard = document.getElementById('travelCard');
  if (travelEvts.length > 0) {
    travelCard?.classList.remove('d-none');
    renderTravelEventLog('travelEventLog', travelEvts);
  } else {
    travelCard?.classList.add('d-none');
  }
}

// ── Behavior helpers ──────────────────────────────────────────────────────────

/** Find the needs snapshot closest to (but not after) worldTime. */
export function findNearestSnapshot(snapshots, worldTime) {
  let result = null;
  for (const s of snapshots) {
    if (s.worldTime <= worldTime) result = s;
    else break;
  }
  return result;
}

/** From a snapshot, return the highest-scoring need {key, value, label}. */
export function dominantNeed(snap) {
  if (!snap) return null;
  const NEED_LABELS = { rest:'Rest', food:'Food', water:'Water',
                        belonging:'Belonging', competence:'Competence', intimacy:'Intimacy' };
  let best = null;
  for (const [key, label] of Object.entries(NEED_LABELS)) {
    const v = snap[key] ?? 0;
    if (!best || v > best.value) best = { key, value: v, label };
  }
  return best;
}

/** Category for row coloring: physio / social / rest / other */
export function actionCategory(action) {
  if (['Eat','Drink','SelfCare'].includes(action))          return 'physio';
  if (['ReachOut','InviteIntimacy'].includes(action))       return 'social';
  if (['Sleep','MoveToRest'].includes(action))              return 'rest';
  return 'other';
}

/** Compute behavior stats from raw data arrays. */
export function computeBehaviorStats(actions, intents, habits, snaps) {
  const grouped = {};
  for (const a of actions) grouped[a.action] = (grouped[a.action] ?? 0) + 1;
  const top = Object.entries(grouped).sort((a, b) => b[1] - a[1])[0];

  // Count how many times each need was dominant (across all snapshots)
  const needCounts = {};
  const NEED_KEYS  = ['rest','food','water','belonging','competence','intimacy'];
  for (const s of snaps) {
    let maxKey = null, maxVal = -1;
    for (const k of NEED_KEYS) { if ((s[k] ?? 0) > maxVal) { maxVal = s[k] ?? 0; maxKey = k; } }
    if (maxKey) needCounts[maxKey] = (needCounts[maxKey] ?? 0) + 1;
  }
  const topNeed = Object.entries(needCounts).sort((a, b) => b[1] - a[1])[0];
  const intentSwitches = intents.filter(i => i.type === 'switched').length;

  return { totalActions: actions.length, topAction: top?.[0], topActionCount: top?.[1],
           intentSwitches, habitEvents: habits.length,
           dominantNeedKey: topNeed?.[0] ?? null };
}

export const NEED_LABELS_MAP = { rest:'Rest', food:'Food', water:'Water',
                          belonging:'Belonging', competence:'Competence', intimacy:'Intimacy' };

/** Render the stats summary row. */
export function renderBehaviorStats(containerId, actions, intents, habits, snaps) {
  const row = document.getElementById(containerId);
  if (!row) return;
  while (row.firstChild) row.removeChild(row.firstChild);
  const stats = computeBehaviorStats(actions, intents, habits, snaps);
  const ICONS = { Eat:'🍽️',Drink:'💧',SelfCare:'🧼',Sleep:'😴',MoveToRest:'🛏️',
                  ReachOut:'🤝',InviteIntimacy:'💕',Idle:'💭',Create:'🎨',Work:'⚒️' };

  const pills = [
    { label: 'Actions committed', value: stats.totalActions, cls: 'primary' },
    stats.topAction
      ? { label: 'Most frequent', value: `${ICONS[stats.topAction] ?? '▶️'} ${stats.topAction} (${stats.topActionCount}×)`, cls: 'info' }
      : null,
    stats.dominantNeedKey
      ? { label: 'Dominant need', value: NEED_LABELS_MAP[stats.dominantNeedKey] ?? stats.dominantNeedKey, cls: 'warning' }
      : null,
    { label: 'Intent changes', value: stats.intentSwitches, cls: stats.intentSwitches > 10 ? 'warning' : 'secondary' },
    { label: 'Habit events', value: stats.habitEvents, cls: 'success' },
  ].filter(Boolean);

  for (const p of pills) {
    const col  = mkDiv('col-auto');
    const card = mkDiv('card text-center border-0 shadow-sm');
    const body = mkDiv('card-body py-1 px-3');
    const val  = mkDiv('fw-bold'); val.textContent = p.value;
    val.style.color = `var(--bs-${p.cls})`;
    const lbl  = mkDiv('text-muted small'); lbl.textContent = p.label;
    body.appendChild(val); body.appendChild(lbl); card.appendChild(body); col.appendChild(card);
    row.appendChild(col);
  }
}

/** Render the action log with causal need context + goal bias. */
export function renderBehaviorActionLog(logId, distId, actions, snapshots, biasByAction = {}) {
  // Distribution badges
  const distEl = document.getElementById(distId);
  if (distEl) {
    while (distEl.firstChild) distEl.removeChild(distEl.firstChild);
    const ICONS = { Eat:'🍽️',Drink:'💧',SelfCare:'🧼',Sleep:'😴',MoveToRest:'🛏️',
                    ReachOut:'🤝',InviteIntimacy:'💕',Idle:'💭',Create:'🎨',Work:'⚒️' };
    const grouped = {};
    for (const a of actions) grouped[a.action] = (grouped[a.action] ?? 0) + 1;
    for (const [action, count] of Object.entries(grouped).sort((a, b) => b[1] - a[1]).slice(0, 6)) {
      const b = document.createElement('span');
      b.className = 'badge text-bg-secondary';
      b.style.fontSize = '0.6rem';
      b.textContent = `${ICONS[action] ?? '▶️'} ${action}: ${count}`;
      distEl.appendChild(b);
    }
  }

  // Action log table
  const el = document.getElementById(logId);
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);

  if (!actions.length) {
    const p = document.createElement('p');
    p.className = 'text-muted small fst-italic m-3';
    p.textContent = 'No actions found (EventId 1003 not in logs).';
    el.appendChild(p); return;
  }

  const table = document.createElement('table');
  table.className = 'table table-sm table-hover mb-0 small';
  const thead = document.createElement('thead'); thead.className = 'table-light';
  const hr = document.createElement('tr');
  ['Time','Action','Utility','Duration','Driving Need','Goal Bias'].forEach(h => {
    const th = document.createElement('th'); th.textContent = h; hr.appendChild(th);
  });
  thead.appendChild(hr); table.appendChild(thead);

  const tbody = document.createElement('tbody');
  for (const a of actions) {
    const tr = document.createElement('tr');
    tr.className = `behav-row-${actionCategory(a.action)}`;

    const tdT = document.createElement('td'); tdT.className = 'text-nowrap text-muted';
    tdT.textContent = fmtWt(a.worldTime);

    const tdA = document.createElement('td');
    const badge = document.createElement('span');
    badge.className = `badge text-bg-${a.level ?? 'secondary'}`;
    badge.textContent = `${a.icon ?? '▶️'} ${a.label ?? a.action}`;
    tdA.appendChild(badge);

    const tdU = document.createElement('td'); tdU.className = 'text-muted';
    tdU.textContent = a.utility != null ? a.utility.toFixed(2) : '—';

    const tdD = document.createElement('td'); tdD.className = 'text-muted';
    tdD.textContent = a.duration ?? '—';

    // Driving need
    const tdN = document.createElement('td');
    const snap = findNearestSnapshot(snapshots, a.worldTime);
    const dn   = dominantNeed(snap);
    if (dn) {
      const pill = mkDiv('behav-need-pill ' + (dn.value > 70 ? 'behav-need-high' : dn.value > 40 ? 'behav-need-medium' : 'behav-need-low'));
      pill.textContent = `${dn.label} ${Math.round(dn.value)}`;
      pill.title = `Highest need: ${dn.label} = ${Math.round(dn.value)}`;
      tdN.appendChild(pill);
    } else { tdN.textContent = '—'; tdN.className = 'text-muted'; }

    // Goal bias — find nearest GoalBiasApplied (1303) for this action
    const tdG = document.createElement('td');
    const biases = biasByAction[a.action] ?? [];
    // Find the most recent bias event before or at this action's worldTime
    const nearBias = biases.filter(b => b.worldTime <= a.worldTime).at(-1);
    if (nearBias) {
      const gp = mkDiv('behav-need-pill behav-need-medium');
      gp.textContent = `${nearBias.icon ?? '🎯'} +${nearBias.bias?.toFixed(2)}`;
      gp.title = `${nearBias.kind} (salience ${nearBias.salience?.toFixed(2)})`;
      tdG.appendChild(gp);
    } else { tdG.className = 'text-muted'; tdG.textContent = '—'; }

    [tdT, tdA, tdU, tdD, tdN, tdG].forEach(td => tr.appendChild(td));
    tbody.appendChild(tr);
  }
  table.appendChild(tbody);
  el.appendChild(table);
}

/** Render intent history (when behavioral direction changed and why). */
export function renderBehaviorIntentLog(containerId, intents) {
  const el = document.getElementById(containerId);
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);

  if (!intents.length) {
    const p = document.createElement('p');
    p.className = 'text-muted small fst-italic m-3';
    p.textContent = 'No intent events found (EventId 1005-1009 not in logs — Debug level may be filtered).';
    el.appendChild(p); return;
  }

  const TYPE_CFG = {
    selected:  { cls: 'info',      label: 'Selected'  },
    retained:  { cls: 'secondary', label: 'Retained'  },
    switched:  { cls: 'warning',   label: 'Switched'  },
    expired:   { cls: 'secondary', label: 'Expired'   },
    emergency: { cls: 'danger',    label: 'Emergency' },
  };

  const table = document.createElement('table');
  table.className = 'table table-sm table-hover mb-0 small';
  const tbody = document.createElement('tbody');

  for (const ev of intents) {
    const cfg = TYPE_CFG[ev.type] ?? { cls: 'secondary', label: ev.type ?? '?' };
    const tr = document.createElement('tr');

    const tdT = document.createElement('td'); tdT.className = 'text-nowrap text-muted';
    tdT.textContent = fmtWt(ev.worldTime);

    const tdB = document.createElement('td');
    const badge = document.createElement('span');
    badge.className = `badge text-bg-${cfg.cls}`;
    badge.textContent = `${ev.icon ?? ''} ${cfg.label}`;
    tdB.appendChild(badge);

    const tdK = document.createElement('td'); tdK.className = 'fw-semibold';
    tdK.textContent = ev.intentKind ?? '?';

    const tdA = document.createElement('td'); tdA.className = 'text-muted';
    tdA.textContent = ev.action ? `→ ${ev.action}` : '';

    const tdS = document.createElement('td'); tdS.className = 'text-muted text-end';
    tdS.textContent = ev.score != null ? ev.score.toFixed(2) : '';

    [tdT, tdB, tdK, tdA, tdS].forEach(td => tr.appendChild(td));
    tbody.appendChild(tr);
  }
  table.appendChild(tbody);
  el.appendChild(table);
}

/** Render habit patterns analysis. */
export function renderBehaviorHabits(containerId, habits) {
  const el = document.getElementById(containerId);
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);

  if (!habits.length) {
    const p = document.createElement('p');
    p.className = 'text-muted small fst-italic m-3';
    p.textContent = 'No habit events found (EventId 1011-1014 not in logs).';
    el.appendChild(p); return;
  }

  // Group by event type
  const learned = habits.filter(h => h.eventId === 1011);
  const decayed = habits.filter(h => h.eventId === 1012);
  const biased  = habits.filter(h => h.eventId === 1014);

  const table = document.createElement('table');
  table.className = 'table table-sm table-hover mb-0 small';
  const thead = document.createElement('thead'); thead.className = 'table-light';
  const hr = document.createElement('tr');
  ['Time','Type','Action','Cue','Surface','Time Band','Strength Δ','Reps'].forEach(h => {
    const th = document.createElement('th'); th.textContent = h; hr.appendChild(th);
  });
  thead.appendChild(hr); table.appendChild(thead);

  const tbody = document.createElement('tbody');

  // Show learned habits (1011) first — most informative
  for (const h of learned) {
    const tr = document.createElement('tr');
    const cells = [
      { text: fmtWt(h.worldTime), cls: 'text-nowrap text-muted' },
      { badge: { text: `💪 Reinforced`, cls: 'success' } },
      { text: h.habitName ?? h.action ?? '?', cls: 'fw-semibold' },
      { text: h.cue     ?? '—', cls: 'text-muted' },
      { text: h.surface ?? '—', cls: 'text-muted' },
      { text: h.timeBand ?? '—', cls: 'text-muted' },
      { strength: h },
      { text: h.repetitions != null ? String(h.repetitions) : '—', cls: 'text-muted' },
    ];
    for (const c of cells) {
      const td = document.createElement('td');
      if (c.badge) {
        const b = document.createElement('span'); b.className = `badge text-bg-${c.badge.cls}`;
        b.textContent = c.badge.text; td.appendChild(b);
      } else if (c.strength) {
        const h2 = c.strength;
        if (h2.strengthBefore != null && h2.strengthAfter != null) {
          const delta = h2.strengthAfter - h2.strengthBefore;
          const sign  = delta >= 0 ? '+' : '';
          td.className = 'fw-semibold ' + (delta > 0 ? 'text-success' : 'text-danger');
          td.textContent = `${h2.strengthBefore.toFixed(3)} → ${h2.strengthAfter.toFixed(3)} (${sign}${delta.toFixed(3)})`;
        } else { td.textContent = '—'; }
      } else {
        if (c.cls) td.className = c.cls;
        td.textContent = c.text ?? '—';
      }
      tr.appendChild(td);
    }
    tbody.appendChild(tr);
  }

  // Then biased habits (1014) — shows which actions get a habit boost
  for (const h of biased) {
    const tr = document.createElement('tr');
    const delta = h.utilityAfter != null && h.utilityBefore != null ? h.utilityAfter - h.utilityBefore : null;
    const cells = [
      { text: fmtWt(h.worldTime), cls: 'text-nowrap text-muted' },
      { badge: { text: '⚡ Bias applied', cls: 'info' } },
      { text: h.action ?? '?', cls: 'fw-semibold' },
      { text: '—' }, { text: '—' }, { text: '—' },
      { text: delta != null ? `util ×${h.multiplier?.toFixed(2) ?? '?'} (${delta >= 0 ? '+' : ''}${delta?.toFixed(3)})` : '—', cls: 'text-info' },
      { text: '—' },
    ];
    for (const c of cells) {
      const td = document.createElement('td');
      if (c.badge) { const b = document.createElement('span'); b.className = `badge text-bg-${c.badge.cls}`; b.textContent = c.badge.text; td.appendChild(b); }
      else { if (c.cls) td.className = c.cls; td.textContent = c.text ?? '—'; }
      tr.appendChild(td);
    }
    tbody.appendChild(tr);
  }

  // Decayed habits summary (1012) — just one summary row if any
  if (decayed.length) {
    const tr = document.createElement('tr');
    tr.className = 'table-secondary';
    const td = document.createElement('td'); td.colSpan = 8; td.className = 'text-muted small fst-italic px-3';
    td.textContent = `📉 ${decayed.length} habit decay event${decayed.length > 1 ? 's' : ''} (traces gradually removed over time)`;
    tr.appendChild(td); tbody.appendChild(tr);
  }

  table.appendChild(tbody);
  el.appendChild(table);
}

// ── Schedule log ─────────────────────────────────────────────────────────────
export function renderScheduleLog(containerId, events) {
  const el = document.getElementById(containerId);
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);

  if (!events.length) {
    const p = document.createElement('p');
    p.className = 'text-muted small fst-italic m-3';
    p.textContent = 'No schedule events found (EventId 1400-1404 not in logs).';
    el.appendChild(p); return;
  }

  const table = document.createElement('table');
  table.className = 'table table-sm table-hover mb-0 small';
  const tbody = document.createElement('tbody');

  for (const ev of events) {
    const tr = document.createElement('tr');

    const tdT = document.createElement('td'); tdT.className = 'text-nowrap text-muted';
    tdT.textContent = fmtWt(ev.worldTime);

    const tdB = document.createElement('td'); tdB.style.width = '130px';
    const badge = document.createElement('span');
    badge.className = `badge text-bg-${ev.level ?? 'secondary'}`;
    badge.textContent = `${ev.icon ?? '📅'} ${ev.label ?? ''}`;
    tdB.appendChild(badge);

    const tdD = document.createElement('td'); tdD.className = 'text-muted small';
    const parts = [];
    if (ev.dayIndex   != null) parts.push(`Day ${ev.dayIndex}`);
    if (ev.slotCount  != null) parts.push(`${ev.slotCount} slots`);
    if (ev.slotId     != null) parts.push(`slot ${ev.slotId}`);
    if (ev.action     != null) parts.push(ev.action);
    if (ev.location   != null) parts.push(`@ ${ev.location}`);
    if (ev.bias       != null) parts.push(`+${ev.bias.toFixed(2)}`);
    if (ev.stress     != null) parts.push(`stress ${ev.stress.toFixed(1)}`);
    if (ev.energy     != null) parts.push(`energy ${ev.energy.toFixed(1)}`);
    if (ev.occupation != null) parts.push(ev.occupation);
    tdD.textContent = parts.join(' · ');

    tr.appendChild(tdT); tr.appendChild(tdB); tr.appendChild(tdD);
    tbody.appendChild(tr);
  }
  table.appendChild(tbody); el.appendChild(table);
}

// ── Behavioral profile from snapshot ─────────────────────────────────────────

export const SURFACE_LABELS = { 0:'Neutral', 1:'Social', 2:'Private', 3:'Intimate', 4:'Rest', 5:'Public', 6:'Work' };

export const TIME_BAND_LABELS = { 0:'Night', 1:'Morning', 2:'Day', 3:'Evening' };

export const CUE_LABELS = { 0:'Neutral', 1:'BodyNeed', 2:'StressRelief', 3:'SocialNeed', 4:'Competence' };

export function renderBehaviorHabitTraces(containerId, traces) {
  const el = document.getElementById(containerId);
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);
  if (!traces.length) {
    const p = document.createElement('p'); p.className = 'text-muted small fst-italic m-3';
    p.textContent = 'No habit traces in snapshot.'; el.appendChild(p); return;
  }
  const ICONS = { Eat:'🍽️',Drink:'💧',SelfCare:'🧼',Sleep:'😴',MoveToRest:'🛏️',ReachOut:'🤝',InviteIntimacy:'💕',Idle:'💭',Create:'🎨',Work:'⚒️' };
  const table = document.createElement('table'); table.className = 'table table-sm table-hover mb-0 small';
  const thead = document.createElement('thead'); thead.className = 'table-light';
  const hr = document.createElement('tr');
  ['Action','Strength','Surface','Time','Cue','Reps','Tendency'].forEach(h => {
    const th = document.createElement('th'); th.textContent = h; hr.appendChild(th);
  });
  thead.appendChild(hr); table.appendChild(thead);
  const tbody = document.createElement('tbody');
  for (const t of traces) {
    const tr = document.createElement('tr');
    const pct = Math.round((t.strength ?? 0) * 100);
    const action = t.actionName ?? '?';
    const cells = [
      (() => { const td = document.createElement('td'); td.className = 'fw-semibold';
        td.textContent = `${ICONS[action] ?? '▶️'} ${action}`; return td; })(),
      (() => { const td = document.createElement('td'); td.style.minWidth='80px';
        const wrap = mkDiv('d-flex align-items-center gap-1');
        const prog = mkDiv('progress flex-grow-1'); prog.style.height='5px';
        const bar = mkDiv('progress-bar'); bar.style.width = pct + '%';
        prog.appendChild(bar); wrap.appendChild(prog);
        const v = document.createElement('span'); v.className='text-muted'; v.textContent = pct + '%';
        wrap.appendChild(v); td.appendChild(wrap); return td; })(),
      (() => { const td = document.createElement('td'); td.className='text-muted';
        td.textContent = SURFACE_LABELS[t.surfaceKind] ?? t.surfaceKind ?? '—'; return td; })(),
      (() => { const td = document.createElement('td'); td.className='text-muted';
        td.textContent = TIME_BAND_LABELS[t.timeBand] ?? t.timeBand ?? '—'; return td; })(),
      (() => { const td = document.createElement('td'); td.className='text-muted';
        td.textContent = CUE_LABELS[t.cueKind] ?? t.cueKind ?? '—'; return td; })(),
      (() => { const td = document.createElement('td'); td.className='text-muted';
        td.textContent = t.repetitionCount ?? '—'; return td; })(),
      (() => { const td = document.createElement('td');
        const cls = t.tendency === 1 ? 'text-success' : 'text-danger';
        td.className = cls; td.textContent = t.tendency === 1 ? '✓ Adaptive' : '⚠ Maladaptive'; return td; })(),
    ];
    cells.forEach(td => tr.appendChild(td)); tbody.appendChild(tr);
  }
  table.appendChild(tbody); el.appendChild(table);
}

export function renderBehaviorCooldowns(containerId, cooldowns) {
  const el = document.getElementById(containerId);
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);
  if (!cooldowns || !Object.keys(cooldowns).length) {
    const p = document.createElement('p'); p.className = 'text-muted small fst-italic';
    p.textContent = 'Žádné aktivní cooldowny.'; el.appendChild(p); return;
  }
  const ACTION_ICONS = { Sleep:'😴', ReachOut:'🤝', InviteIntimacy:'💕' };
  for (const [action, hours] of Object.entries(cooldowns)) {
    const row = mkDiv('d-flex align-items-center justify-content-between mb-2');
    const lbl = document.createElement('span'); lbl.className = 'small fw-semibold';
    lbl.textContent = `${ACTION_ICONS[action] ?? '⏳'} ${action}`;
    const val = document.createElement('span'); val.className = 'badge text-bg-warning';
    val.textContent = typeof hours === 'number' ? hours.toFixed(1) + ' h' : String(hours);
    row.appendChild(lbl); row.appendChild(val); el.appendChild(row);
  }
}

// ── Values & Guilt event log ──────────────────────────────────────────────────
export function renderValuesEventLog(containerId, events) {
  const el = document.getElementById(containerId);
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);

  if (!events.length) {
    const p = document.createElement('p');
    p.className = 'text-muted small fst-italic m-3';
    p.textContent = 'No values events found (EventId 1210–1213 not in logs).';
    el.appendChild(p); return;
  }

  const table = document.createElement('table');
  table.className = 'table table-sm table-hover mb-0 small';
  const tbody = document.createElement('tbody');

  for (const ev of events) {
    const tr = document.createElement('tr');

    const tdT = document.createElement('td'); tdT.className = 'text-nowrap text-muted'; tdT.style.width = '155px';
    tdT.textContent = fmtWt(ev.worldTime);

    const tdB = document.createElement('td'); tdB.style.width = '160px';
    const badge = document.createElement('span');
    badge.className = `badge text-bg-${ev.level ?? 'secondary'}`;
    badge.textContent = `${ev.icon ?? ''} ${ev.label ?? ''}`;
    tdB.appendChild(badge);

    const tdD = document.createElement('td'); tdD.className = 'text-muted small';
    const parts = [];
    if (ev.eventId === 1210) {
      // Values profile — show bars
      const vals = [
        { k:'benevolence', l:'Benevolence', c:'#198754' },
        { k:'universalism', l:'Universalism', c:'#0d6efd' },
        { k:'achievement', l:'Achievement', c:'#fd7e14' },
        { k:'power', l:'Power', c:'#dc3545' },
      ];
      const wrap = mkDiv('d-flex flex-wrap gap-2');
      for (const v of vals) {
        if (ev[v.k] == null) continue;
        const pct = Math.round(ev[v.k] * 100);
        const chip = mkDiv('d-flex align-items-center gap-1');
        const lbl  = document.createElement('span'); lbl.className = 'text-muted'; lbl.textContent = v.l;
        const prog = mkDiv('progress'); prog.style.cssText = 'height:6px;width:60px';
        const bar  = mkDiv('progress-bar'); bar.style.cssText = `width:${pct}%;background:${v.c}`;
        prog.appendChild(bar); chip.appendChild(lbl); chip.appendChild(prog);
        const num  = document.createElement('span'); num.className = 'text-muted'; num.textContent = (ev[v.k]).toFixed(2);
        chip.appendChild(num); wrap.appendChild(chip);
      }
      tdD.appendChild(wrap);
    } else if (ev.eventId === 1211) {
      parts.push(`congruence ${ev.congruence?.toFixed(2) ?? '?'}`);
      if (ev.utilityDelta != null) parts.push(`utility ${ev.utilityDelta >= 0 ? '+' : ''}${ev.utilityDelta.toFixed(2)}`);
      tdD.textContent = parts.join(' · ');
    } else if (ev.eventId === 1212) {
      parts.push(`violated: ${ev.violatedValue ?? '?'}`);
      if (ev.congruence != null) parts.push(`congruence ${ev.congruence.toFixed(2)}`);
      if (ev.deltaValence != null) parts.push(`valence ${ev.deltaValence >= 0 ? '+' : ''}${ev.deltaValence.toFixed(2)}`);
      tdD.textContent = parts.join(' · ');
    } else if (ev.eventId === 1213) {
      parts.push(`${ev.strengthened ?? '?'} ↑ / ${ev.eroded ?? '?'} ↓`);
      if (ev.delta != null) parts.push(`Δ ${ev.delta.toFixed(3)}`);
      tdD.textContent = parts.join(' · ');
    }

    tr.appendChild(tdT); tr.appendChild(tdB); tr.appendChild(tdD);
    tbody.appendChild(tr);
  }
  table.appendChild(tbody); el.appendChild(table);
}

// ── Dark Core modifier event list ─────────────────────────────────────────────
export function renderDarkCoreEventLog(containerId, events) {
  const el = document.getElementById(containerId);
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);

  if (!events.length) {
    const p = document.createElement('p');
    p.className = 'text-muted small fst-italic m-3';
    p.textContent = 'No Dark Core modifier events found (EventId 1017 not in logs).';
    el.appendChild(p); return;
  }

  const table = document.createElement('table');
  table.className = 'table table-sm table-hover mb-0 small';
  const tbody = document.createElement('tbody');

  for (const ev of events) {
    const tr = document.createElement('tr');
    markBrushable(tr, ev.worldTime);

    const tdT = document.createElement('td'); tdT.className = 'text-nowrap text-muted'; tdT.style.width = '155px';
    tdT.textContent = fmtWt(ev.worldTime);

    const tdK = document.createElement('td'); tdK.style.width = '110px';
    const badge = document.createElement('span');
    badge.className = ev.kind === 'Antagonism' ? 'badge text-bg-danger' : 'badge text-bg-secondary';
    badge.textContent = ev.kind === 'Antagonism' ? '😈 Antagonism' : '🧊 Prosocial';
    tdK.appendChild(badge);

    const tdA = document.createElement('td'); tdA.style.width = '130px';
    tdA.className = 'fw-semibold';
    tdA.textContent = ev.action ?? '';

    const tdD = document.createElement('td'); tdD.className = 'text-muted';
    const deltaSign = ev.delta >= 0 ? '+' : '';
    tdD.textContent = `D=${(ev.darkCore ?? 0).toFixed(3)}  delta=${deltaSign}${(ev.delta ?? 0).toFixed(3)}`;

    tr.appendChild(tdT); tr.appendChild(tdK); tr.appendChild(tdA); tr.appendChild(tdD);
    tbody.appendChild(tr);
  }
  table.appendChild(tbody); el.appendChild(table);
  applyBrushFilter();
}

// ── Travel / transit event list ───────────────────────────────────────────────
export function renderTravelEventLog(containerId, events) {
  const el = document.getElementById(containerId);
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);

  if (!events.length) {
    const p = document.createElement('p');
    p.className = 'text-muted small fst-italic m-3';
    p.textContent = 'No travel events found (EventId 1700/1701 not in logs).';
    el.appendChild(p); return;
  }

  const table = document.createElement('table');
  table.className = 'table table-sm table-hover mb-0 small';
  const tbody = document.createElement('tbody');

  for (const ev of events) {
    const tr = document.createElement('tr');
    markBrushable(tr, ev.worldTime);

    const tdT = document.createElement('td'); tdT.className = 'text-nowrap text-muted'; tdT.style.width = '155px';
    tdT.textContent = fmtWt(ev.worldTime);

    const tdK = document.createElement('td'); tdK.style.width = '110px';
    const badge = document.createElement('span');
    badge.className = ev.kind === 'departed' ? 'badge text-bg-info' : 'badge text-bg-success';
    badge.textContent = ev.kind === 'departed' ? '🚶 Departed' : '🏁 Arrived';
    tdK.appendChild(badge);

    const tdD = document.createElement('td'); tdD.className = 'fw-semibold';
    tdD.textContent = ev.destination ?? '';

    const tdE = document.createElement('td'); tdE.className = 'text-muted';
    tdE.textContent = ev.kind === 'departed'
      ? `${(ev.minutes ?? 0).toFixed(1)}min · ETA ${fmtWt(ev.arriveAt) || ev.arriveAt || '?'}`
      : '';

    tr.appendChild(tdT); tr.appendChild(tdK); tr.appendChild(tdD); tr.appendChild(tdE);
    tbody.appendChild(tr);
  }
  table.appendChild(tbody); el.appendChild(table);
  applyBrushFilter();
}

export function groupActionSessions(actions) {
  if (!actions.length) return [];
  const sessions = [];
  let cur = { action: actions[0].action, icon: actions[0].icon, label: actions[0].label,
               level: actions[0].level, events: [actions[0]] };
  for (let i = 1; i < actions.length; i++) {
    const a = actions[i];
    if (a.action === cur.action) { cur.events.push(a); }
    else { sessions.push(cur); cur = { action: a.action, icon: a.icon, label: a.label, level: a.level, events: [a] }; }
  }
  sessions.push(cur);
  return sessions;
}

// ── Relationship trend ────────────────────────────────────────────────────────
