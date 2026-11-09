// public/js/core/dashboard.js
import * as api from '../api.js';
import * as charts from '../charts.js';
import { fmtWt, mkDiv } from './util.js';

// ── Snapshot / Dashboard constants ───────────────────────────────────────────
export const EMOTION_LABELS_DASH = ['Neutral','Joy','Sadness','Anger','Fear','Disgust','Surprise','Tenderness','Pride','Shame'];

export const EMOTION_ICONS_DASH  = { Neutral:'😐',Joy:'😄',Sadness:'😢',Anger:'😠',Fear:'😨',Disgust:'🤢',Surprise:'😲',Tenderness:'🥰',Pride:'😤',Shame:'😳' };

export const EMOTION_COLORS_DASH = ['#6c757d','#ffc107','#6f42c1','#dc3545','#0d6efd','#198754','#fd7e14','#e83e8c','#6610f2','#495057'];

export function emotionLabel(eid) { return EMOTION_LABELS_DASH[eid] ?? 'Neutral'; }

export function emotionIcon(eid)  { return EMOTION_ICONS_DASH[emotionLabel(eid)] ?? '😐'; }

export function emotionColor(eid) { return EMOTION_COLORS_DASH[eid] ?? '#6c757d'; }

// snap-bar color: "bad if high" → red when high; "good if high" → green when high
export function snapBarColor(value, max, thresholds, invertBad = false) {
  const pct = (value ?? 0) / max * 100;
  const [lo, hi] = thresholds ?? [30, 70];
  if (invertBad) return pct > hi ? 'snap-bar-danger' : pct > lo ? 'snap-bar-warning' : 'snap-bar-ok';
  return pct < lo ? 'snap-bar-danger' : pct < hi ? 'snap-bar-warning' : 'snap-bar-ok';
}

export function buildSnapBar(label, value, max, levelClass, unit = '') {
  const pct = Math.min(100, Math.max(0, ((value ?? 0) / max) * 100));
  const row  = mkDiv('snap-bar-row ' + levelClass);
  const lbl  = document.createElement('span'); lbl.className = 'snap-bar-label'; lbl.textContent = label;
  const wrap = mkDiv('snap-bar-wrap');
  const prog = mkDiv('progress'); prog.style.height = '6px';
  const bar  = mkDiv('progress-bar'); bar.style.width = pct + '%';
  prog.appendChild(bar); wrap.appendChild(prog);
  const val  = document.createElement('span'); val.className = 'snap-bar-value';
  val.textContent = value != null ? (Math.round(value * (unit === '%' ? 100 : 1)) + unit) : '—';
  row.appendChild(lbl); row.appendChild(wrap); row.appendChild(val);
  return row;
}

export function buildRelCell(value, max = 100) {
  const pct = Math.min(100, Math.max(0, ((value ?? 0) / max) * 100));
  const cell = document.createElement('td');
  const wrap = mkDiv('rel-cell-bar');
  const prog = mkDiv('progress'); prog.style.height = '5px';
  const bar  = mkDiv('progress-bar'); bar.style.width = pct + '%';
  const color = pct > 80 ? '#198754' : pct > 50 ? '#0d6efd' : pct > 20 ? '#6c757d' : '#dc3545';
  bar.style.backgroundColor = color;
  prog.appendChild(bar); wrap.appendChild(prog);
  const num = document.createElement('span'); num.className = 'rel-cell-num';
  num.textContent = value != null ? Math.round(value) : '—';
  wrap.appendChild(num); cell.appendChild(wrap);
  return cell;
}

export function appendStatePill(container, text, variant) {
  const b = document.createElement('span');
  b.className = `badge text-bg-${variant}`;
  b.textContent = text;
  container.appendChild(b);
}

// ── Time-range brush filter ─────────────────────────────────────────────────────
/** Tags a list row as filterable by the chart brush (carries its worldTime). */
export function markBrushable(el, wt) {
  if (wt) { el.dataset.wt = wt; el.classList.add('brushable'); }
}

/** Hides every .brushable row whose worldTime falls outside the active brush range. */
export function applyBrushFilter() {
  const r = charts.getBrushRange();
  document.querySelectorAll('.brushable[data-wt]').forEach(el => {
    el.classList.toggle('d-none', !!r && (el.dataset.wt < r.from || el.dataset.wt > r.to));
  });
  updateBrushChip(r);
}

/** Floating chip showing the active brush window with a clear (✕) button. */
export function updateBrushChip(r) {
  let chip = document.getElementById('brushChip');
  if (!r) { chip?.remove(); return; }
  if (!chip) {
    chip = document.createElement('div');
    chip.id = 'brushChip';
    chip.className = 'alert alert-info py-1 px-2 position-fixed shadow-sm d-flex align-items-center gap-2 mb-0';
    chip.style.cssText = 'bottom:12px; left:50%; transform:translateX(-50%); z-index:2000; font-size:.78rem;';
    document.body.appendChild(chip);
  }
  while (chip.firstChild) chip.removeChild(chip.firstChild);
  const txt = document.createElement('span');
  txt.textContent = `🔍 ${fmtWt(r.from)} – ${fmtWt(r.to)}`;
  const btn = document.createElement('button');
  btn.className = 'btn btn-sm btn-outline-secondary py-0 px-1 lh-1';
  btn.textContent = '✕';
  btn.title = 'Clear time filter';
  btn.onclick = () => charts.clearBrush();
  chip.appendChild(txt); chip.appendChild(btn);
}

/**
 * Net change of `key` over a recent window of `series` (current − past). Used for
 * the header trend arrows so a pill shows direction, not just the instantaneous
 * value. Window is a fraction of the series, clamped to [3, maxWin] points.
 */
export function trendDelta(series, key, frac = 0.08, maxWin = 60) {
  if (!series?.length) return null;
  let last = null, lastIdx = -1;
  for (let i = series.length - 1; i >= 0; i--) {
    if (series[i][key] != null) { last = series[i][key]; lastIdx = i; break; }
  }
  if (lastIdx < 0) return null;
  const win = Math.min(maxWin, Math.max(3, Math.floor(series.length * frac)));
  let past = null;
  for (let i = Math.max(0, lastIdx - win); i <= lastIdx; i++) {
    if (series[i][key] != null) { past = series[i][key]; break; }
  }
  return past == null ? null : last - past;
}

/** "↑8" / "↓5" / "→" suffix for a trend delta (negligible → flat arrow). */
export function arrowText(delta) {
  if (delta == null) return '';
  const r = Math.round(delta);
  if (r > 1)  return ` ↑${r}`;
  if (r < -1) return ` ↓${Math.abs(r)}`;
  return ' →';
}

/** Fetches cached psych/physio series and derives recent trend deltas for the header. */
export async function computeHeaderTrends(id) {
  const [psy, phy] = await Promise.all([
    api.getPsychology(id).catch(() => ({ snapshots: [] })),
    api.getPhysiology(id).catch(() => ({ snapshots: [] })),
  ]);
  const ps = psy.snapshots ?? [], ph = phy.snapshots ?? [];
  return {
    stress: trendDelta(ps, 'stress'),
    mood:   trendDelta(ps, 'moodBaseline'),
    energy: trendDelta(ph, 'energy'),
  };
}

// ── renderHeaderSnapshot ──────────────────────────────────────────────────────
export function renderHeaderSnapshot(snap, trends = null) {
  const emotBadge = document.getElementById('headerEmotionBadge');
  const pillsEl   = document.getElementById('headerStatePills');
  const gaugesEl  = document.getElementById('headerSnapshotGauges');
  if (emotBadge)  { while (emotBadge.nextSibling && emotBadge.nextSibling.classList?.contains('badge')) emotBadge.parentNode.removeChild(emotBadge.nextSibling); emotBadge.classList.add('d-none'); }
  if (pillsEl)    while (pillsEl.firstChild) pillsEl.removeChild(pillsEl.firstChild);
  if (gaugesEl)   while (gaugesEl.firstChild) gaugesEl.removeChild(gaugesEl.firstChild);
  if (!snap) return;

  // Emotion badge
  const eid = snap.psychology?.dominantEmotion;
  if (eid != null && emotBadge) {
    emotBadge.textContent = `${emotionIcon(eid)} ${emotionLabel(eid)}`;
    emotBadge.style.background = emotionColor(eid);
    emotBadge.style.color = '#fff';
    emotBadge.classList.remove('d-none');
  }

  // State pills (with recent-trend arrows when a time series is available)
  if (pillsEl && snap.psychology) {
    const stress = snap.psychology.stress ?? 0;
    appendStatePill(pillsEl, `Stress ${Math.round(stress)}${arrowText(trends?.stress)}`, stress > 70 ? 'danger' : stress > 40 ? 'warning' : 'success');
    const energy = snap.physiology?.energy ?? 0;
    appendStatePill(pillsEl, `Energy ${Math.round(energy)}${arrowText(trends?.energy)}`, energy < 30 ? 'warning' : 'secondary');
    const mood = snap.psychology.moodBaseline;
    if (mood != null) appendStatePill(pillsEl, `Mood ${Math.round(mood)}${arrowText(trends?.mood)}`, mood < 35 ? 'warning' : 'secondary');
    const plan = snap.behavior?.currentPlanName;
    if (plan) appendStatePill(pillsEl, plan, 'primary');
  }

  // Mini bars in header (2 small bars stacked)
  if (gaugesEl && snap.physiology && snap.psychology) {
    [
      { label: 'Energy', value: snap.physiology.energy, max: 100, cls: snapBarColor(snap.physiology.energy, 100, [30,60]) },
      { label: 'Stress', value: snap.psychology.stress, max: 100, cls: snapBarColor(snap.psychology.stress, 100, [40,70], true) },
    ].forEach(({ label, value, max, cls }) => {
      const row = mkDiv('header-snap-row');
      const lbl = document.createElement('span'); lbl.className = 'header-snap-lbl'; lbl.textContent = label;
      const barWrap = mkDiv('header-snap-bar');
      const prog = mkDiv('progress'); prog.style.height = '5px';
      const bar  = mkDiv(`progress-bar ${cls.replace('snap-bar-', 'bg-').replace('ok','success').replace('danger','danger').replace('warning','warning').replace('info','info')}`);
      bar.style.width = Math.min(100, ((value ?? 0) / max) * 100) + '%';
      prog.appendChild(bar); barWrap.appendChild(prog);
      row.appendChild(lbl); row.appendChild(barWrap);
      gaugesEl.appendChild(row);
    });
  }
}

// ── buildSnapshotBanner ───────────────────────────────────────────────────────
export function buildSnapshotBanner(snap) {
  const banner = mkDiv('card snap-banner mb-3');
  const body   = mkDiv('card-body py-2 d-flex flex-wrap align-items-center gap-3');

  const eid = snap.psychology?.dominantEmotion;
  if (eid != null) {
    const eb = document.createElement('span');
    eb.className = 'snap-emotion-badge badge rounded-pill';
    eb.style.background = emotionColor(eid); eb.style.color = '#fff';
    eb.textContent = `${emotionIcon(eid)} ${emotionLabel(eid)}`;
    body.appendChild(eb);
  }

  // Gauge pills
  const gauges = [
    { label: 'Valence',  value: snap.psychology?.valence,       fmt: v => (v >= 0 ? '+' : '') + v.toFixed(2), range: [-1,1], pct: v => (v + 1) / 2 * 100 },
    { label: 'Stress',   value: snap.psychology?.stress,         fmt: v => Math.round(v),  range: [0,100], pct: v => v },
    { label: 'Energy',   value: snap.physiology?.energy,         fmt: v => Math.round(v),  range: [0,100], pct: v => v },
    { label: 'Arousal',  value: snap.psychology?.arousal,        fmt: v => v.toFixed(2),   range: [0,1],   pct: v => v * 100 },
    { label: 'CogLoad',  value: snap.psychology?.cognitiveLoad,  fmt: v => Math.round(v),  range: [0,100], pct: v => v },
    { label: 'Mood',     value: snap.psychology?.moodBaseline,   fmt: v => Math.round(v),  range: [0,100], pct: v => v },
  ];
  for (const g of gauges) {
    if (g.value == null) continue;
    const pill = mkDiv('snap-gauge-pill');
    const lbl  = mkDiv('snap-gauge-label'); lbl.textContent = g.label;
    const barWrap = mkDiv('snap-gauge-bar');
    const prog = mkDiv('progress'); prog.style.height = '5px';
    const bar  = mkDiv('progress-bar'); bar.style.width = g.pct(g.value).toFixed(0) + '%';
    prog.appendChild(bar); barWrap.appendChild(prog);
    const val  = mkDiv('snap-gauge-value'); val.textContent = g.fmt(g.value);
    pill.appendChild(lbl); pill.appendChild(barWrap); pill.appendChild(val);
    body.appendChild(pill);
  }

  // Chips
  const planName = snap.behavior?.currentPlanName;
  if (planName) {
    const chip = document.createElement('span');
    chip.className = 'badge text-bg-primary ms-auto';
    chip.textContent = `▶ ${planName}${snap.behavior.currentPlanUtility != null ? '  ' + snap.behavior.currentPlanUtility.toFixed(2) : ''}`;
    body.appendChild(chip);
  }
  const loc = snap.interactionSurface?.location;
  if (loc) {
    const chip = document.createElement('span');
    chip.className = 'badge text-bg-secondary';
    chip.textContent = `📍 ${loc}${snap.interactionSurface.hasPrivacy ? ' · Private' : ''}`;
    body.appendChild(chip);
  }

  banner.appendChild(body);
  return banner;
}
