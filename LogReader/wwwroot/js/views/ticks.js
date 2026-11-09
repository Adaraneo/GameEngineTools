// public/js/views/ticks.js
import * as api from '../api.js';
import { state } from '../core/state.js';
import { buildSnapBar, snapBarColor } from '../core/dashboard.js';
import { fmtWt, mkDiv, sectionTitle } from '../core/util.js';
import { EMOTION_META } from './psych.js';
import { eventName } from './story.js';
import { openWhyPanel } from './why.js';

// ── Tick scrubber (P2) ─────────────────────────────────────────────────────────


export async function loadTicks() {
  state.tickData  = await api.getTicks(state.currentCharId);
  state.tickIndex = 0;
  const ticks = state.tickData?.ticks ?? [];

  const slider = document.getElementById('tickSlider');
  const prev   = document.getElementById('tickPrev');
  const next   = document.getElementById('tickNext');
  if (!slider) return;

  if (!ticks.length) {
    slider.max = 0; slider.disabled = true;
    document.getElementById('tickWorldTime').textContent = 'No ticks (run load-all first)';
    document.getElementById('tickPosition').textContent = '';
    renderTickState(null); renderTickEvents([]);
    return;
  }

  slider.disabled = false;
  slider.min = 0;
  slider.max = ticks.length - 1;
  slider.value = 0;

  slider.oninput = () => renderTick(Number(slider.value));
  prev.onclick   = () => renderTick(state.tickIndex - 1);
  next.onclick   = () => renderTick(state.tickIndex + 1);

  renderTick(0);
}

export function renderTick(i) {
  const ticks = state.tickData?.ticks ?? [];
  if (!ticks.length) return;
  state.tickIndex = Math.max(0, Math.min(i, ticks.length - 1));
  const tick = ticks[state.tickIndex];

  const slider = document.getElementById('tickSlider');
  if (slider) slider.value = state.tickIndex;
  document.getElementById('tickWorldTime').textContent = fmtWt(tick.worldTime);
  const posEl = document.getElementById('tickPosition');
  if (posEl) {
    posEl.textContent = `tick ${state.tickIndex + 1} / ${ticks.length}`
      + (state.tickData.truncated ? ` (of ${state.tickData.totalTicks}, truncated)` : '');
  }

  renderTickState(tick.snapshots);
  renderTickEvents(tick.events ?? []);
}

export function renderTickState(snaps) {
  const el = document.getElementById('tickState');
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);
  if (!snaps) {
    const p = document.createElement('p'); p.className = 'text-muted small fst-italic';
    p.textContent = 'No state snapshot at this tick.'; el.appendChild(p); return;
  }

  // Physiology — green good-high (energy/immune), red bad-high (hunger/thirst/pain)
  const ph = snaps.physio;
  if (ph) {
    el.appendChild(sectionTitle('Physiology'));
    const goodHigh = v => snapBarColor(v, 100, [30, 60], false);
    const badHigh  = v => snapBarColor(v, 100, [40, 70], true);
    if (ph.energy != null) el.appendChild(buildSnapBar('Energy', ph.energy, 100, goodHigh(ph.energy)));
    if (ph.hunger != null) el.appendChild(buildSnapBar('Hunger', ph.hunger, 100, badHigh(ph.hunger)));
    if (ph.thirst != null) el.appendChild(buildSnapBar('Thirst', ph.thirst, 100, badHigh(ph.thirst)));
    if (ph.pain   != null) el.appendChild(buildSnapBar('Pain',   ph.pain,   100, badHigh(ph.pain)));
    if (ph.immune != null) el.appendChild(buildSnapBar('Immune', ph.immune, 100, goodHigh(ph.immune)));
    if (ph.sleepDebt != null) el.appendChild(buildSnapBar('SleepDebt', ph.sleepDebt, 16, 'snap-bar-info', 'h'));
  }

  // Psychology
  const py = snaps.psych;
  if (py) {
    el.appendChild(sectionTitle('Psychology'));
    if (py.emotion) {
      const badge = document.createElement('span');
      badge.className = 'badge text-bg-secondary mb-2';
      badge.textContent = `${EMOTION_META[py.emotion]?.icon ?? '😐'} ${py.emotion}`;
      el.appendChild(badge);
    }
    if (py.valence   != null) el.appendChild(buildSnapBar('Valence', (py.valence + 1) * 50, 100, py.valence >= 0 ? 'snap-bar-ok' : 'snap-bar-danger'));
    if (py.arousal   != null) el.appendChild(buildSnapBar('Arousal', py.arousal * 100, 100, 'snap-bar-info'));
    if (py.stress    != null) el.appendChild(buildSnapBar('Stress',  py.stress, 100, snapBarColor(py.stress, 100, [40, 70], true)));
    if (py.cogLoad   != null) el.appendChild(buildSnapBar('CogLoad', py.cogLoad, 100, snapBarColor(py.cogLoad, 100, [40, 70], true)));
  }

  // Behavior — current plan + needs
  const bh = snaps.behavior;
  if (bh) {
    el.appendChild(sectionTitle('Behavior'));
    const badges = mkDiv('d-flex flex-wrap gap-1 mb-2');
    if (bh.plan) {
      const badge = document.createElement('span');
      badge.className = 'badge text-bg-primary';
      badge.textContent = `Plan: ${bh.plan}`;
      badges.appendChild(badge);
    }
    // Active intent (5200) — the stabilised direction behind the plan.
    if (bh.intentKind && bh.intentKind !== 'None') {
      const ib = document.createElement('span');
      ib.className = 'badge text-bg-info';
      const str = bh.intentStrength != null ? ` ${Math.round(bh.intentStrength * 100)}%` : '';
      const commit = bh.intentCommitment ? ` ·c${bh.intentCommitment}` : '';
      ib.textContent = `🧭 ${bh.intentKind}${str}${commit}`;
      ib.title = 'Active intent — stabilised behavioural direction (strength · commitment)';
      badges.appendChild(ib);
    }
    if (badges.children.length) el.appendChild(badges);
    const need = v => snapBarColor(v, 100, [40, 70], true);
    if (bh.rest       != null) el.appendChild(buildSnapBar('Rest',       bh.rest,       100, need(bh.rest)));
    if (bh.food       != null) el.appendChild(buildSnapBar('Food',       bh.food,       100, need(bh.food)));
    if (bh.water      != null) el.appendChild(buildSnapBar('Water',      bh.water,      100, need(bh.water)));
    if (bh.belonging  != null) el.appendChild(buildSnapBar('Belonging',  bh.belonging,  100, need(bh.belonging)));
    if (bh.competence != null) el.appendChild(buildSnapBar('Competence', bh.competence, 100, need(bh.competence)));
    if (bh.intimacy   != null) el.appendChild(buildSnapBar('Intimacy',   bh.intimacy,   100, need(bh.intimacy)));
  }
}

export const TICK_SUB_ICONS = { physio:'🍽️', sleep:'😴', repro:'🤰', object:'📦', psych:'🧠',
                         behavior:'⚒️', values:'🧭', goal:'🎯', memory:'💭', relationship:'❤️' };

export function renderTickEvents(events) {
  const el = document.getElementById('tickEvents');
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);
  if (!events.length) {
    const p = document.createElement('p'); p.className = 'text-muted small fst-italic m-3';
    p.textContent = 'Nothing happened this tick.'; el.appendChild(p); return;
  }
  const table = document.createElement('table');
  table.className = 'table table-sm table-hover mb-0 small';
  const tbody = document.createElement('tbody');
  for (const ev of events) {
    const tr = document.createElement('tr');
    tr.style.cursor = 'pointer';
    tr.title = 'Click for ❓ Why';
    tr.onclick = () => openWhyPanel(ev);
    const tdI = document.createElement('td'); tdI.style.width = '28px';
    tdI.textContent = ev.icon ?? TICK_SUB_ICONS[ev.subsystem] ?? '▶️';
    const tdB = document.createElement('td'); tdB.style.width = '110px';
    const badge = document.createElement('span');
    badge.className = `badge text-bg-${ev.level ?? 'secondary'}`;
    badge.textContent = ev.subsystem;
    tdB.appendChild(badge);
    const tdL = document.createElement('td');
    tdL.textContent = ev.label || eventName(ev.eventId);
    tr.appendChild(tdI); tr.appendChild(tdB); tr.appendChild(tdL);
    tbody.appendChild(tr);
  }
  table.appendChild(tbody); el.appendChild(table);
}
