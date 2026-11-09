// public/js/views/physio.js
import * as api from '../api.js';
import * as charts from '../charts.js';
import { state } from '../core/state.js';
import { updateSidebarDeathBadge } from '../core/characters.js';
import { buildSnapBar } from '../core/dashboard.js';
import { fmtWt, mkDiv } from '../core/util.js';
import { findNearestSnapshot } from './behavior.js';
import { renderObjectUsedLog } from './objects.js';

export async function loadPhysiology() {
  const data = await api.getPhysiology(state.currentCharId);
  // data = { snapshots, sleepEvents, cycle, actions, reproEvents }

  const snaps      = data.snapshots   ?? [];
  const sleepEvts  = data.sleepEvents ?? [];
  const actions    = data.actions     ?? [];

  // ── Stats summary ────────────────────────────────────────────────────────
  const sessions   = buildSleepSessions(sleepEvts);
  const physStats  = computePhysioStats(snaps, sessions, actions);
  renderPhysioStats('physioStatsRow', physStats);

  // ── Charts ───────────────────────────────────────────────────────────────
  // Pass current snapshot to health chart for Allostatic Load / Cortisol reference lines
  const currentPhysSnap = state.npcMapCache.get(state.currentCharId.toLowerCase())?.snapshot?.physiology ?? null;
  // Use rAF to ensure the panel has been laid out by the browser before Chart.js
  // queries canvas dimensions — without it a cache-hit response can arrive before
  // the first paint, leaving the canvas with 0×0 px and an invisible chart.
  // Sleep bands: shade the time the character was asleep so energy recovery /
  // sleep-debt repayment is readable against WHEN they slept.
  const sleepBands = charts.buildSleepBands(sessions);

  await new Promise(r => requestAnimationFrame(r));
  charts.renderPhysioNeedsChart('physioNeedsChart', snaps, sleepBands);
  charts.renderPhysioHealthChart('physioHealthChart', snaps, currentPhysSnap);
  charts.renderHormoneCycleChart('hormoneCycleChart', snaps, data.cycle ?? []);
  charts.renderHormoneLevelsChart('hormoneLevelsChart', data.hormones ?? []);
  charts.renderNutritionChart('nutritionChart', snaps);
  charts.renderAgingChart('agingChart', snaps);

  // ── Sleep sessions: quality chart + table ─────────────────────────────────
  const qualWrap = document.getElementById('sleepQualityChartWrap');
  if (sessions.some(s => s.quality != null)) {
    if (qualWrap) qualWrap.style.display = '';
    charts.renderSleepQualityChart('sleepQualityChart', sessions);
  } else {
    if (qualWrap) qualWrap.style.display = 'none';
  }
  renderSleepSessions('sleepSessionsList', sessions);

  // ── Nutrition (from snapshot) ────────────────────────────────────────────
  const profileForPhysio = state.npcMapCache.get(state.currentCharId.toLowerCase());
  renderNutrition('nutritionList', profileForPhysio?.snapshot?.physiology ?? null);

  // ── Physical actions log (with need context) ─────────────────────────────
  renderPhysioActions('physioActionsList', actions, snaps);

  // ── Cycle ────────────────────────────────────────────────────────────────
  const cycleCard = document.getElementById('cycleCard');
  if (data.cycle?.length) {
    cycleCard?.classList.remove('d-none');
    renderCycleList('cycleList', data.cycle);
  } else {
    cycleCard?.classList.add('d-none');
  }

  // ── Reproduction ─────────────────────────────────────────────────────────
  const reproCard = document.getElementById('reproCard');
  if (data.reproEvents?.length) {
    reproCard?.classList.remove('d-none');
    renderPhysioEventList('reproList', data.reproEvents, '');
  } else {
    reproCard?.classList.add('d-none');
  }

  // ── Object usage log ──────────────────────────────────────────────────────
  const objUsedCard = document.getElementById('objectUsedCard');
  if ((data.objectUsed?.length ?? 0) > 0) {
    objUsedCard?.classList.remove('d-none');
    renderObjectUsedLog('objectUsedList', data.objectUsed);
  } else {
    objUsedCard?.classList.add('d-none');
  }

  // ── Death record ──────────────────────────────────────────────────────────
  const deathEvents = sleepEvts.filter(ev => ev.eventId === 5007);
  const deathCard = document.getElementById('deathCard');
  if (deathEvents.length > 0) {
    deathCard?.classList.remove('d-none');
    const deathEvent = deathEvents[deathEvents.length - 1];
    renderDeathRecord('deathRecord', deathEvent);
    // Update sidebar character list with death indicator
    updateSidebarDeathBadge(state.currentCharId, deathEvent);
  } else {
    deathCard?.classList.add('d-none');
  }
}

// ── Sleep session builder ─────────────────────────────────────────────────────
export function buildSleepSessions(sleepEvts) {
  const sessions = [];
  let current    = null;

  for (const ev of sleepEvts) {
    const eid = ev.eventId;
    if (eid === 1101) {                          // Solo sleep started
      current = { start: ev.worldTime, shared: false, interrupted: false,
                  plannedHours: ev.plannedHours ?? null, nightmare: false };
    } else if (eid === 1102) {                   // Shared sleep started
      current = { start: ev.worldTime, shared: true, interrupted: false, nightmare: false };
    } else if (eid === 1105) {                   // Woke up — session end
      const ses = current ?? { start: null, shared: false, interrupted: false, nightmare: false };
      ses.end         = ev.worldTime;
      ses.hours       = ev.hours       ?? null;
      ses.quality     = ev.quality     ?? null;
      ses.interrupted = ev.interrupted ?? ses.interrupted;
      sessions.push(ses);
      current = null;
    } else if (eid === 5002 && !current) {       // Physio sleep-ended without tracked start
      sessions.push({ start: null, end: ev.worldTime, hours: ev.hours,
                      quality: ev.quality, debtAfter: ev.debtAfter,
                      shared: false, interrupted: false, nightmare: false });
    } else if (eid === 5002 && sessions.length) {// Enrich last session with debt info
      sessions[sessions.length - 1].debtAfter = ev.debtAfter;
    } else if (eid === 1106 && current) {        // Interrupted
      current.interrupted    = true;
      current.interruptCause = ev.cause ?? null;
      current.interruptPhase = ev.phase ?? null;
    } else if (eid === 1108 && current) {        // Nightmare
      current.nightmare = true;
      current.nightmareStress = ev.stressAtSleep ?? null;
    }
  }
  if (current) sessions.push(current); // ongoing session
  return sessions;
}

// ── Physio stats computation ──────────────────────────────────────────────────
export function computePhysioStats(snaps, sessions, actions) {
  const completeSessions = sessions.filter(s => s.hours != null);
  const totalSleepH  = completeSessions.reduce((s, ses) => s + (ses.hours ?? 0), 0);
  const qualSessions = completeSessions.filter(s => s.quality != null);
  const avgQuality   = qualSessions.length
    ? qualSessions.reduce((s, ses) => s + ses.quality, 0) / qualSessions.length : null;
  const avgEnergy    = snaps.length
    ? snaps.reduce((s, sn) => s + (sn.energy ?? 0), 0) / snaps.length : null;
  const avgHunger    = snaps.length
    ? snaps.reduce((s, sn) => s + (sn.hunger ?? 0), 0) / snaps.length : null;
  const eatCount      = actions.filter(a => a.action === 'Eat').length;
  const drinkCount    = actions.filter(a => a.action === 'Drink').length;
  const selfCareCount = actions.filter(a => a.action === 'SelfCare').length;
  const nightmares    = sessions.filter(s => s.nightmare).length;
  const interrupted   = sessions.filter(s => s.interrupted).length;
  return { totalSleepH, avgQuality, avgEnergy, avgHunger,
           eatCount, drinkCount, selfCareCount, nightmares,
           sessionCount: sessions.length, interrupted };
}

// ── Render physio summary stats ───────────────────────────────────────────────
export function renderPhysioStats(containerId, stats) {
  const row = document.getElementById(containerId);
  if (!row) return;
  while (row.firstChild) row.removeChild(row.firstChild);

  const pills = [
    { label: 'Sleep sessions', value: stats.sessionCount,
      cls: 'primary' },
    { label: 'Total sleep',    value: stats.totalSleepH.toFixed(1) + ' h',
      cls: 'info' },
    { label: 'Avg quality',    value: stats.avgQuality != null ? Math.round(stats.avgQuality) + ' / 100' : '—',
      cls: stats.avgQuality != null ? (stats.avgQuality > 70 ? 'success' : stats.avgQuality > 40 ? 'warning' : 'danger') : 'secondary' },
    { label: 'Avg energy',     value: stats.avgEnergy  != null ? Math.round(stats.avgEnergy)  + ' / 100' : '—',
      cls: stats.avgEnergy  != null ? (stats.avgEnergy  > 60 ? 'success' : stats.avgEnergy  > 30 ? 'warning' : 'danger') : 'secondary' },
    { label: 'Ate',            value: stats.eatCount + '×',       cls: 'success'   },
    { label: 'Drank',          value: stats.drinkCount + '×',     cls: 'info'      },
    { label: 'Self-care',      value: stats.selfCareCount + '×',  cls: 'primary'   },
    stats.nightmares > 0
      ? { label: 'Nightmares', value: stats.nightmares + '×',     cls: 'danger'    }
      : null,
    stats.interrupted > 0
      ? { label: 'Interrupted', value: stats.interrupted + '×',   cls: 'warning'   }
      : null,
  ].filter(Boolean);

  for (const p of pills) {
    const col  = mkDiv('col-auto');
    const card = mkDiv('card text-center border-0 shadow-sm');
    const body = mkDiv('card-body py-1 px-3');
    const val  = mkDiv('fw-bold'); val.textContent = p.value;
    val.style.color = `var(--bs-${p.cls})`;
    const lbl  = mkDiv('text-muted' + ' small'); lbl.textContent = p.label;
    body.appendChild(val); body.appendChild(lbl); card.appendChild(body); col.appendChild(card);
    row.appendChild(col);
  }
}

// ── Sleep sessions table ──────────────────────────────────────────────────────
export function renderSleepSessions(containerId, sessions) {
  const el = document.getElementById(containerId);
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);

  if (!sessions.length) {
    const p = document.createElement('p');
    p.className = 'text-muted small fst-italic m-3';
    p.textContent = 'No sleep sessions found (EventId 1101/1105 not in logs).';
    el.appendChild(p); return;
  }

  const table = document.createElement('table');
  table.className = 'table table-sm table-hover mb-0 small';
  const thead = document.createElement('thead'); thead.className = 'table-light';
  const hr = document.createElement('tr');
  ['Start', 'End', 'Duration', 'Quality', 'Type', 'Notes'].forEach(h => {
    const th = document.createElement('th'); th.textContent = h; hr.appendChild(th);
  });
  thead.appendChild(hr); table.appendChild(thead);

  const tbody = document.createElement('tbody');
  for (const ses of sessions) {
    const tr = document.createElement('tr');
    if (ses.nightmare)    tr.classList.add('table-warning-subtle');
    else if (ses.interrupted) tr.classList.add('table-danger-subtle');

    const fmtWt = wt => (wt ?? '—').slice(0, 19).replace('T', ' ');

    // Start
    const tdStart = document.createElement('td'); tdStart.className = 'text-nowrap text-muted';
    tdStart.textContent = fmtWt(ses.start);
    // End
    const tdEnd   = document.createElement('td'); tdEnd.className = 'text-nowrap text-muted';
    tdEnd.textContent = ses.end ? fmtWt(ses.end) : '…';
    // Duration
    const tdHours = document.createElement('td'); tdHours.className = 'fw-semibold';
    tdHours.textContent = ses.hours != null ? ses.hours.toFixed(1) + ' h' : '—';
    // Quality — colored progress bar
    const tdQual  = document.createElement('td'); tdQual.style.minWidth = '90px';
    if (ses.quality != null) {
      const q = Math.round(ses.quality);
      const barColor = q > 70 ? 'success' : q > 40 ? 'warning' : 'danger';
      const wrap = mkDiv('d-flex align-items-center gap-1');
      const prog = mkDiv('progress flex-grow-1'); prog.style.height = '6px';
      const bar  = mkDiv(`progress-bar bg-${barColor}`); bar.style.width = q + '%';
      prog.appendChild(bar); wrap.appendChild(prog);
      const val = document.createElement('span'); val.className = 'text-muted'; val.textContent = q;
      wrap.appendChild(val); tdQual.appendChild(wrap);
    } else { tdQual.textContent = '—'; }
    // Type
    const tdType  = document.createElement('td');
    tdType.textContent = ses.shared ? '🛌 Shared' : '😴 Solo';
    // Notes
    const tdNote  = document.createElement('td'); tdNote.className = 'text-muted small';
    const notes = [];
    if (ses.interrupted)  notes.push(`🚨 ${ses.interruptCause ?? 'interrupted'}${ses.interruptPhase ? ' (' + ses.interruptPhase + ')' : ''}`);
    if (ses.nightmare)    notes.push(`👻 nightmare${ses.nightmareStress != null ? ' (stress ' + ses.nightmareStress.toFixed(0) + ')' : ''}`);
    if (ses.debtAfter != null) notes.push(`debt→${ses.debtAfter.toFixed(1)}h`);
    tdNote.textContent = notes.join(' · ') || '';

    [tdStart, tdEnd, tdHours, tdQual, tdType, tdNote].forEach(td => tr.appendChild(td));
    tbody.appendChild(tr);
  }
  table.appendChild(tbody);
  el.appendChild(table);
}

// ── Physical actions log ──────────────────────────────────────────────────────
// Map each physio action to the snapshot field that explains WHY it was done
export const ACTION_TRIGGER_FIELD = {
  Eat:        { key: 'hunger',    label: 'Hunger'    },
  Drink:      { key: 'thirst',    label: 'Thirst'    },
  SelfCare:   { key: 'pain',      label: 'Pain'      },
  Sleep:      { key: 'sleepDebt', label: 'SleepDebt' },
  MoveToRest: { key: 'energy',    label: 'Energy'    },
};

export function renderPhysioActions(containerId, actions, snapshots = []) {
  const el = document.getElementById(containerId);
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);

  if (!actions.length) {
    const p = document.createElement('p');
    p.className = 'text-muted small fst-italic m-3';
    p.textContent = 'No physical actions found (EventId 1003 not in logs).';
    el.appendChild(p); return;
  }

  // Summary badges at top
  const summaryRow = mkDiv('d-flex flex-wrap gap-2 p-2 border-bottom');
  const grouped = {};
  for (const a of actions) grouped[a.action] = (grouped[a.action] ?? 0) + 1;
  const ICONS = { Eat:'🍽️', Drink:'💧', SelfCare:'🧼', Sleep:'😴', MoveToRest:'🛏️' };
  for (const [action, count] of Object.entries(grouped).sort((a, b) => b[1] - a[1])) {
    const b = document.createElement('span');
    b.className = 'badge text-bg-secondary';
    b.textContent = `${ICONS[action] ?? '▶️'} ${action}: ${count}×`;
    summaryRow.appendChild(b);
  }
  el.appendChild(summaryRow);

  // Timeline list with causal context
  const list = mkDiv('');
  const table = document.createElement('table');
  table.className = 'table table-sm table-hover mb-0 small';
  const tbody = document.createElement('tbody');
  for (const a of actions) {
    const tr = document.createElement('tr');

    // Time
    const tdT = document.createElement('td'); tdT.className = 'text-nowrap text-muted'; tdT.style.width = '155px';
    tdT.textContent = fmtWt(a.worldTime);

    // Action badge
    const tdA = document.createElement('td');
    const badge = document.createElement('span');
    badge.className = `badge text-bg-${a.level ?? 'secondary'}`;
    badge.textContent = `${a.icon ?? '▶️'} ${a.label ?? a.action}`;
    tdA.appendChild(badge);

    // Utility + duration
    const tdD = document.createElement('td'); tdD.className = 'text-muted small';
    const parts = [];
    if (a.utility  != null) parts.push(`utility ${a.utility.toFixed(2)}`);
    if (a.duration != null) parts.push(String(a.duration));
    tdD.textContent = parts.join(' · ');

    // Causal context — the physiological value that triggered this action
    const tdCtx = document.createElement('td');
    const trigger = ACTION_TRIGGER_FIELD[a.action];
    if (trigger && snapshots.length) {
      const snap = findNearestSnapshot(snapshots, a.worldTime);
      const val  = snap?.[trigger.key];
      if (val != null) {
        const v   = Math.round(val);
        const cls = v > 70 ? 'behav-need-high' : v > 40 ? 'behav-need-medium' : 'behav-need-low';
        const pill = mkDiv('behav-need-pill ' + cls);
        pill.textContent = `${trigger.label}: ${v}`;
        pill.title = `${trigger.label} was ${v} when this action was committed`;
        tdCtx.appendChild(pill);
      }
    }

    tr.appendChild(tdT); tr.appendChild(tdA); tr.appendChild(tdD); tr.appendChild(tdCtx);
    tbody.appendChild(tr);
  }
  table.appendChild(tbody); list.appendChild(table);
  el.appendChild(list);
}

export function renderPhysioEventList(containerId, events, emptyMsg) {
  const el = document.getElementById(containerId);
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);

  if (!events.length) {
    const p = document.createElement('p');
    p.className = 'text-muted small fst-italic m-3';
    p.textContent = emptyMsg;
    el.appendChild(p);
    return;
  }

  const table = document.createElement('table');
  table.className = 'table table-sm table-hover mb-0 small';

  const tbody = document.createElement('tbody');
  for (const ev of events) {
    const tr = document.createElement('tr');

    // World time
    const tdTime = document.createElement('td');
    tdTime.className = 'text-nowrap text-muted';
    tdTime.style.width = '170px';
    tdTime.textContent = fmtWt(ev.worldTime);

    // Icon + label badge
    const tdLabel = document.createElement('td');
    tdLabel.style.width = '150px';
    const badge = document.createElement('span');
    badge.className = `badge text-bg-${ev.level ?? 'secondary'}`;
    badge.textContent = `${ev.icon ?? ''} ${ev.label ?? ev.action ?? ''}`;
    tdLabel.appendChild(badge);

    // Extra details
    const tdDetail = document.createElement('td');
    tdDetail.className = 'text-muted';
    const detail = buildEventDetail(ev);
    tdDetail.textContent = detail;

    tr.appendChild(tdTime);
    tr.appendChild(tdLabel);
    tr.appendChild(tdDetail);
    tbody.appendChild(tr);
  }
  table.appendChild(tbody);
  el.appendChild(table);
}

export function buildEventDetail(ev) {
  const f  = v => v != null ? v.toFixed(1) : null;
  const f2 = v => v != null ? v.toFixed(2) : null;
  const parts = [];
  if (ev.hours         != null) parts.push(`${f(ev.hours)}h`);
  if (ev.quality       != null) parts.push(`quality ${Math.round(ev.quality)}`);
  if (ev.sleepQuality  != null) parts.push(`quality ${Math.round(ev.sleepQuality)}`);
  if (ev.interrupted   != null) parts.push(ev.interrupted ? 'interrupted' : 'normal');
  if (ev.cause         != null) parts.push(`cause: ${ev.cause}`);
  if (ev.phase         != null) parts.push(`phase: ${ev.phase}`);
  if (ev.stressAtSleep != null) parts.push(`stress at sleep ${f(ev.stressAtSleep)}`);
  if (ev.stressSpike   != null) parts.push(`stress +${f(ev.stressSpike)}`);
  if (ev.valencePenalty!= null) parts.push(`valence -${f2(ev.valencePenalty)}`);
  if (ev.stressDelta   != null) parts.push(`stress +${f(ev.stressDelta)}`);
  if (ev.debtAfter     != null) parts.push(`debt after ${f(ev.debtAfter)}h`);
  if (ev.hunger        != null) parts.push(`hunger ${f(ev.hunger)}`);
  if (ev.thirst        != null) parts.push(`thirst ${f(ev.thirst)}`);
  if (ev.needRest      != null) parts.push(`need ${f(ev.needRest)}`);
  if (ev.energy        != null) parts.push(`energy ${f(ev.energy)}`);
  if (ev.utility       != null) parts.push(`utility ${f2(ev.utility)}`);
  if (ev.duration      != null) parts.push(String(ev.duration));
  if (ev.plannedHours  != null) parts.push(`planned ${f(ev.plannedHours)}h`);
  if (ev.habitName     != null) parts.push(`'${ev.habitName}'`);
  if (ev.strengthBefore!= null) parts.push(`${f2(ev.strengthBefore)}→${f2(ev.strengthAfter)}`);
  if (ev.cue           != null) parts.push(`cue:${ev.cue}`);
  if (ev.timeBand      != null) parts.push(ev.timeBand);
  if (ev.strength      != null) parts.push(`strength ${f2(ev.strength)}`);
  if (ev.repetitions   != null) parts.push(`rep ${Math.round(ev.repetitions)}`);
  if (ev.removed       != null) parts.push(`removed ${ev.removed}`);
  if (ev.retention     != null) parts.push(`retention ${f2(ev.retention)}`);
  if (ev.multiplier    != null) parts.push(`×${ev.multiplier.toFixed(2)}`);
  if (ev.utilityBefore != null) parts.push(`utility ${f2(ev.utilityBefore)}→${f2(ev.utilityAfter)}`);
  if (ev.applicability != null) parts.push(`fit ${f2(ev.applicability)}`);
  // Reproduction events
  if (ev.result        != null) parts.push(`result: ${ev.result}`);
  if (ev.chance        != null) parts.push(`chance ${(ev.chance * 100).toFixed(1)}%`);
  if (ev.daysPregnant  != null) parts.push(`${ev.daysPregnant} days pregnant`);
  // Decisions & plans
  if (ev.start         != null) parts.push(`start ${ev.start}`);
  if (ev.duration      != null && ev.hours == null) parts.push(`${ev.duration}`);
  // Interaction outcomes
  if (ev.outcome       != null && ev.cause == null) parts.push(ev.outcome);
  if (ev.probability   != null) parts.push(`p=${(ev.probability * 100).toFixed(0)}%`);
  if (ev.touchLevel    != null) parts.push(`level ${ev.touchLevel}`);
  return parts.length ? parts.join(' · ') : '';
}

export function renderCycleList(containerId, cycleEntries) {
  const el = document.getElementById(containerId);
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);
  const table = document.createElement('table');
  table.className = 'table table-sm mb-0 small';
  const tbody = document.createElement('tbody');
  for (const c of cycleEntries) {
    const tr = document.createElement('tr');
    const tdTime  = document.createElement('td'); tdTime.className = 'text-nowrap text-muted';
    tdTime.textContent = fmtWt(c.worldTime);
    const tdPhase = document.createElement('td');
    const badge = document.createElement('span'); badge.className = 'badge text-bg-primary';
    badge.textContent = c.phase ?? '?'; tdPhase.appendChild(badge);
    const tdDay = document.createElement('td'); tdDay.className = 'text-muted';
    tdDay.textContent = `Day ${c.dayInCycle ?? '?'}`;
    tr.appendChild(tdTime); tr.appendChild(tdPhase); tr.appendChild(tdDay);
    tbody.appendChild(tr);
  }
  table.appendChild(tbody);
  el.appendChild(table);
}

// ── Physiology: nutrition ─────────────────────────────────────────────────────
export function renderNutrition(containerId, physSnap) {
  const el = document.getElementById(containerId);
  const card = document.getElementById('nutritionCard');
  if (!el || !card) return;
  const nutrients = [
    { label: 'Calories',      key: 'calories',     max: 100 },
    { label: 'Vitamin D',     key: 'vitaminD',     max: 100 },
    { label: 'Iron',          key: 'iron',          max: 100 },
    { label: 'Protein',       key: 'protein',       max: 100 },
    { label: 'Blood Glucose', key: 'bloodGlucose',  max: 100 },
  ].filter(n => physSnap?.[n.key] != null);
  if (!nutrients.length) { card.classList.add('d-none'); return; }
  card.classList.remove('d-none');
  while (el.firstChild) el.removeChild(el.firstChild);
  const grid = mkDiv('row g-0');
  for (const n of nutrients) {
    const col = mkDiv('col-6');
    const v = physSnap[n.key];
    const cls = v < 30 ? 'snap-bar-danger' : v < 60 ? 'snap-bar-warning' : 'snap-bar-ok';
    col.appendChild(buildSnapBar(n.label, v, n.max, cls));
    grid.appendChild(col);
  }
  el.appendChild(grid);
}

// ── Death record ─────────────────────────────────────────────────────────────
export function renderDeathRecord(containerId, deathEvent) {
  const el = document.getElementById(containerId);
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);

  if (!deathEvent) {
    const p = document.createElement('p');
    p.className = 'text-muted small fst-italic';
    p.textContent = 'No death record found.';
    el.appendChild(p);
    return;
  }

  // Create a card-like display for the death record
  const container = mkDiv('');

  // Time of death
  const timeRow = mkDiv('mb-3 pb-3 border-bottom');
  const timeLabel = document.createElement('span');
  timeLabel.className = 'text-muted small';
  timeLabel.textContent = 'Time of Death';
  const timeVal = document.createElement('div');
  timeVal.className = 'fw-semibold text-danger';
  timeVal.textContent = fmtWt(deathEvent.worldTime);
  timeRow.appendChild(timeLabel);
  timeRow.appendChild(timeVal);
  container.appendChild(timeRow);

  // Cause of death
  const causeRow = mkDiv('mb-3 pb-3 border-bottom');
  const causeLabel = document.createElement('span');
  causeLabel.className = 'text-muted small';
  causeLabel.textContent = 'Cause';
  const causeVal = document.createElement('div');
  causeVal.className = 'fw-semibold';
  causeVal.textContent = deathEvent.cause ?? '—';
  causeRow.appendChild(causeLabel);
  causeRow.appendChild(causeVal);
  container.appendChild(causeRow);

  // Age at death
  const ageRow = mkDiv('mb-3 pb-3 border-bottom');
  const ageLabel = document.createElement('span');
  ageLabel.className = 'text-muted small';
  ageLabel.textContent = 'Age at Death';
  const ageVal = document.createElement('div');
  ageVal.className = 'fw-semibold';
  ageVal.textContent = deathEvent.age != null ? deathEvent.age + ' years' : '—';
  ageRow.appendChild(ageLabel);
  ageRow.appendChild(ageVal);
  container.appendChild(ageRow);

  // Risk per hour
  if (deathEvent.riskPerHour != null) {
    const riskRow = mkDiv('mb-3');
    const riskLabel = document.createElement('span');
    riskLabel.className = 'text-muted small';
    riskLabel.textContent = 'Risk per Hour';
    const riskVal = document.createElement('div');
    riskVal.className = 'fw-semibold';
    riskVal.textContent = (deathEvent.riskPerHour * 100).toFixed(4) + '%';
    riskRow.appendChild(riskLabel);
    riskRow.appendChild(riskVal);
    container.appendChild(riskRow);
  }

  el.appendChild(container);
}
