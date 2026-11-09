// public/js/views/psych.js
import * as api from '../api.js';
import * as charts from '../charts.js';
import { state } from '../core/state.js';
import { applyBrushFilter, markBrushable } from '../core/dashboard.js';
import { fmtWt, mkDiv } from '../core/util.js';
import { findNearestSnapshot } from './behavior.js';
import { buildSleepSessions } from './physio.js';

export async function loadPsychology() {
  // Physiology snapshots carry Cortisol + AllostaticLoad for the HPA/mood chart;
  // fetched alongside psychology (server-cached, cheap).
  const [data, physioData] = await Promise.all([
    api.getPsychology(state.currentCharId),
    api.getPhysiology(state.currentCharId).catch(() => ({ snapshots: [] })),
  ]);
  const snaps      = data.snapshots  ?? [];
  const psychEvts  = data.psychEvents ?? [];
  const physioSnaps = physioData.snapshots ?? [];

  // Event-overlay markers: discrete moments (nightmares, emotion shifts, allostatic
  // milestones, sleep interruptions) drawn onto the continuous psych charts so a
  // stress/mood spike is readable against WHAT happened at that tick.
  const markers = charts.buildEventMarkers(psychEvts, physioData.sleepEvents);

  // Sleep bands: shade asleep intervals so stress/mood recovery reads against sleep.
  const sleepBands = charts.buildSleepBands(buildSleepSessions(physioData.sleepEvents ?? []));

  // ── Stats row ─────────────────────────────────────────────────────────────
  renderPsychStats('psychStatsRow', snaps, psychEvts);

  // ── Charts ────────────────────────────────────────────────────────────────
  await new Promise(r => requestAnimationFrame(r));
  charts.renderPsychVADChart('psychVADChart', snaps, markers);
  charts.renderPsychStressChart('psychStressChart', snaps, markers, sleepBands);
  charts.renderStressMoodChart('psychStressMoodChart', snaps, physioSnaps, markers, sleepBands);
  charts.renderMotivationsChart('psychMotivationsChart', snaps);
  charts.renderSelfConceptChart('selfConceptChart', data.selfConcept ?? [], markers);
  charts.renderInterestsChart('interestsChart', data.interests ?? [], markers);
  charts.renderValuesChart('valuesDriftChart', data.valuesSnapshots ?? [], markers);
  charts.renderDarkCoreChart('darkCoreChart', data.darkCoreSnapshots ?? [], markers);
  charts.renderNeedAppraisalChart('needAppraisalChart', data.needAppraisal ?? [], markers);
  charts.renderEmotionTimelineChart('psychEmotionChart', snaps);

  // ── Emotion distribution ──────────────────────────────────────────────────
  renderEmotionDistribution('psychEmotionDist', snaps);

  // ── Psych events with causal context ─────────────────────────────────────
  renderPsychEvents('psychEventsList', psychEvts, snaps);

  // ── Social comparison log ─────────────────────────────────────────────────
  renderSocialComparisonList('socialComparisonList', psychEvts);
}

// ── Psychology helpers ────────────────────────────────────────────────────────

export const EMOTION_META = {
  Neutral:   { icon:'😐', color:'#adb5bd' },
  Joy:       { icon:'😄', color:'#ffc107' },
  Sadness:   { icon:'😢', color:'#6f9fd8' },
  Anger:     { icon:'😠', color:'#dc3545' },
  Fear:      { icon:'😨', color:'#fd7e14' },
  Disgust:   { icon:'🤢', color:'#6c757d' },
  Surprise:  { icon:'😲', color:'#0dcaf0' },
  Tenderness:{ icon:'🥰', color:'#e83e8c' },
  Pride:     { icon:'😤', color:'#6610f2' },
  Shame:     { icon:'😳', color:'#495057' },
};

export function renderPsychStats(containerId, snaps, psychEvts) {
  const row = document.getElementById(containerId);
  if (!row) return;
  while (row.firstChild) row.removeChild(row.firstChild);
  if (!snaps.length) return;

  const stresses  = snaps.map(s => s.stress  ?? 0);
  const valences  = snaps.map(s => s.valence ?? 0);
  const avgStress = stresses.reduce((a, b) => a + b, 0) / stresses.length;
  const maxStress = Math.max(...stresses);
  const negPct    = Math.round(valences.filter(v => v < 0).length / valences.length * 100);
  const highStressPct = Math.round(stresses.filter(s => s > 70).length / stresses.length * 100);

  // Most frequent emotion
  const emotCounts = {};
  for (const s of snaps) if (s.emotion) emotCounts[s.emotion] = (emotCounts[s.emotion] ?? 0) + 1;
  const topEmotion = Object.entries(emotCounts).sort((a, b) => b[1] - a[1])[0];

  const pills = [
    { label: 'Avg stress',     value: Math.round(avgStress),
      cls: avgStress > 70 ? 'danger' : avgStress > 40 ? 'warning' : 'success' },
    { label: 'Peak stress',    value: Math.round(maxStress),
      cls: maxStress > 70 ? 'danger' : 'secondary' },
    { label: 'Negative mood',  value: negPct + '%',
      cls: negPct > 50 ? 'danger' : negPct > 25 ? 'warning' : 'success' },
    { label: 'High stress time', value: highStressPct + '%',
      cls: highStressPct > 30 ? 'danger' : highStressPct > 10 ? 'warning' : 'success' },
    topEmotion
      ? { label: 'Dominant emotion',
          value: `${EMOTION_META[topEmotion[0]]?.icon ?? ''} ${topEmotion[0]}`,
          cls: 'secondary' }
      : null,
    psychEvts.length
      ? { label: 'Psych events', value: psychEvts.length, cls: 'info' }
      : null,
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

export function renderEmotionDistribution(containerId, snaps) {
  const el = document.getElementById(containerId);
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);

  if (!snaps.length) {
    const p = document.createElement('p');
    p.className = 'text-muted small fst-italic m-3';
    p.textContent = 'No snapshot data.';
    el.appendChild(p); return;
  }

  const counts = {};
  for (const s of snaps) if (s.emotion) counts[s.emotion] = (counts[s.emotion] ?? 0) + 1;
  const total   = snaps.length;
  const sorted  = Object.entries(counts).sort((a, b) => b[1] - a[1]);

  const table = document.createElement('table');
  table.className = 'table table-sm mb-0 small';
  const tbody = document.createElement('tbody');
  for (const [emotion, count] of sorted) {
    const pct  = Math.round(count / total * 100);
    const meta = EMOTION_META[emotion] ?? { icon: '?', color: '#adb5bd' };
    const tr   = document.createElement('tr');

    const tdIcon = document.createElement('td'); tdIcon.style.width = '28px';
    tdIcon.textContent = meta.icon;

    const tdName = document.createElement('td'); tdName.className = 'fw-semibold';
    tdName.textContent = emotion;

    const tdBar  = document.createElement('td');
    const wrap   = mkDiv('d-flex align-items-center gap-2');
    const prog   = mkDiv('progress flex-grow-1'); prog.style.height = '7px';
    const bar    = mkDiv('progress-bar');
    bar.style.width = pct + '%'; bar.style.backgroundColor = meta.color;
    prog.appendChild(bar); wrap.appendChild(prog);
    const pctEl  = document.createElement('span'); pctEl.className = 'text-muted small';
    pctEl.style.minWidth = '36px'; pctEl.textContent = pct + '%';
    wrap.appendChild(pctEl); tdBar.appendChild(wrap);

    tr.appendChild(tdIcon); tr.appendChild(tdName); tr.appendChild(tdBar);
    tbody.appendChild(tr);
  }
  table.appendChild(tbody); el.appendChild(table);
}

/** Psych events (5101/5102) with stress/state context at the time. */
export function renderPsychEvents(containerId, events, snaps) {
  const el = document.getElementById(containerId);
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);

  if (!events.length) {
    const p = document.createElement('p');
    p.className = 'text-muted small fst-italic m-3';
    p.textContent = 'No psychological events (EventId 5101/5102 not in logs — Debug level may be filtered).';
    el.appendChild(p); return;
  }

  const table = document.createElement('table');
  table.className = 'table table-sm table-hover mb-0 small';
  const thead = document.createElement('thead'); thead.className = 'table-light';
  const hr = document.createElement('tr');
  ['Time', 'Event', 'Details', 'State at time'].forEach(h => {
    const th = document.createElement('th'); th.textContent = h; hr.appendChild(th);
  });
  thead.appendChild(hr); table.appendChild(thead);

  const tbody = document.createElement('tbody');
  for (const ev of events) {
    const tr = document.createElement('tr');
    markBrushable(tr, ev.worldTime);

    const tdT = document.createElement('td'); tdT.className = 'text-nowrap text-muted';
    tdT.textContent = fmtWt(ev.worldTime);

    const tdB = document.createElement('td');
    const badge = document.createElement('span');
    badge.className = `badge text-bg-${ev.level ?? 'secondary'}`;
    badge.textContent = `${ev.icon ?? ''} ${ev.label ?? ''}`;
    tdB.appendChild(badge);

    const tdD = document.createElement('td'); tdD.className = 'text-muted small';
    const parts = [];
    if (ev.stressSpike    != null) parts.push(`stress +${ev.stressSpike.toFixed(1)}`);
    if (ev.valencePenalty != null) parts.push(`valence −${ev.valencePenalty.toFixed(2)}`);
    if (ev.sleepQuality   != null) parts.push(`quality ${Math.round(ev.sleepQuality)}`);
    if (ev.stressDelta    != null) parts.push(`stress +${ev.stressDelta.toFixed(1)}`);
    // Social comparison fields (EventId 2016)
    if (ev.eventId === 2016) {
      if (ev.selfEsteemDelta     != null) parts.push(`esteem ${ev.selfEsteemDelta >= 0 ? '+' : ''}${ev.selfEsteemDelta.toFixed(3)}`);
      if (ev.moodValenceDelta    != null) parts.push(`valence ${ev.moodValenceDelta >= 0 ? '+' : ''}${ev.moodValenceDelta.toFixed(3)}`);
      if (ev.achievementMotivationDelta != null && ev.achievementMotivationDelta !== 0)
        parts.push(`achieve ${ev.achievementMotivationDelta >= 0 ? '+' : ''}${ev.achievementMotivationDelta.toFixed(1)}`);
      if (ev.targetHostilityDelta != null && ev.targetHostilityDelta !== 0)
        parts.push(`hostility +${ev.targetHostilityDelta.toFixed(2)}`);
      if (ev.partnerId) parts.push(`vs ${ev.partnerId.slice(0, 8)}…`);
    }
    tdD.textContent = parts.join(' · ');

    // Causal context: find nearest snapshot and show state
    const tdCtx = document.createElement('td'); tdCtx.className = 'text-muted small';
    const snap = findNearestSnapshot(snaps, ev.worldTime);
    if (snap) {
      const stressCls = (snap.stress ?? 0) > 70 ? 'behav-need-high' : (snap.stress ?? 0) > 40 ? 'behav-need-medium' : 'behav-need-low';
      const sp = mkDiv('behav-need-pill ' + stressCls);
      sp.textContent = `Stress ${Math.round(snap.stress ?? 0)}`;
      tdCtx.appendChild(sp);
      if (snap.valence != null) {
        const vp = mkDiv('behav-need-pill ' + ((snap.valence ?? 0) < -0.3 ? 'behav-need-high' : (snap.valence ?? 0) < 0 ? 'behav-need-medium' : 'behav-need-low'));
        vp.textContent = `V ${snap.valence.toFixed(2)}`;
        vp.style.marginLeft = '3px';
        tdCtx.appendChild(vp);
      }
    }

    tr.appendChild(tdT); tr.appendChild(tdB); tr.appendChild(tdD); tr.appendChild(tdCtx);
    tbody.appendChild(tr);
  }
  table.appendChild(tbody); el.appendChild(table);
  applyBrushFilter();   // honour an active brush for freshly rendered rows
}

/** Social comparison event list (EventId 2016) — rendered in Psychology tab. */
export function renderSocialComparisonList(containerId, events) {
  const el = document.getElementById(containerId);
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);

  const cmpEvents = (events ?? []).filter(e => e.eventId === 2016);
  if (!cmpEvents.length) {
    const p = document.createElement('p');
    p.className = 'text-muted small fst-italic m-3';
    p.textContent = 'No social comparison events found (EventId 2016 — comparison cooldown may mean infrequent entries).';
    el.appendChild(p);
    return;
  }

  const table = document.createElement('table');
  table.className = 'table table-sm table-hover mb-0 small';
  const thead = document.createElement('thead'); thead.className = 'table-light';
  const hr = document.createElement('tr');
  ['Time', 'Direction', 'Reaction / Envy', 'Self-esteem', 'Mood valence', 'Achieve', 'Hostility'].forEach(h => {
    const th = document.createElement('th'); th.textContent = h; hr.appendChild(th);
  });
  thead.appendChild(hr); table.appendChild(thead);

  const tbody = document.createElement('tbody');
  for (const ev of cmpEvents) {
    const tr = document.createElement('tr');
    markBrushable(tr, ev.worldTime);

    const tdT = document.createElement('td'); tdT.className = 'text-nowrap text-muted';
    tdT.textContent = fmtWt(ev.worldTime);

    const tdDir = document.createElement('td');
    const dirBadge = document.createElement('span');
    const dirCls = ev.direction === 'Upward' ? 'info' : ev.direction === 'Downward' ? 'secondary' : 'light';
    dirBadge.className = `badge text-bg-${dirCls}`;
    dirBadge.textContent = `${ev.icon ?? ''} ${ev.direction ?? '?'}`;
    tdDir.appendChild(dirBadge);

    const tdReaction = document.createElement('td');
    const envyCls = ev.envy === 'Malicious' ? 'danger' : ev.envy === 'Benign' ? 'success' : 'secondary';
    const parts = [ev.reaction ?? ''];
    if (ev.envy && ev.envy !== 'None') parts.push(ev.envy);
    const rxBadge = document.createElement('span');
    rxBadge.className = `badge text-bg-${envyCls}`;
    rxBadge.textContent = parts.join(' · ');
    tdReaction.appendChild(rxBadge);

    function deltaTd(val, invert) {
      const td = document.createElement('td');
      if (val == null) { td.textContent = '—'; return td; }
      const cls = (val > 0) !== invert ? 'text-success' : 'text-danger';
      td.className = `fw-semibold ${cls}`;
      td.textContent = (val >= 0 ? '+' : '') + val.toFixed(3);
      return td;
    }

    tr.appendChild(tdT);
    tr.appendChild(tdDir);
    tr.appendChild(tdReaction);
    tr.appendChild(deltaTd(ev.selfEsteemDelta,          false));
    tr.appendChild(deltaTd(ev.moodValenceDelta,         false));
    tr.appendChild(deltaTd(ev.achievementMotivationDelta, false));
    tr.appendChild(deltaTd(ev.targetHostilityDelta,     true));
    tbody.appendChild(tr);
  }
  table.appendChild(tbody); el.appendChild(table);
  applyBrushFilter();
}
