// public/js/charts.js
// All chart-building functions. Each destroys any previous chart on that canvas first.
// Zoom: scroll-wheel to zoom X axis, drag to pan, Reset button to restore.

const _charts = {};

/** Reset zoom on any chart by canvas ID. Called from app.js via data-reset-zoom.
 *  Falls back to a prefix match so one button can reset a small-multiples group
 *  (e.g. "relChart" resets relChart_0, relChart_1, …). */
export function resetZoom(canvasId) {
  if (_charts[canvasId]) { _charts[canvasId].resetZoom(); return; }
  for (const id in _charts) if (id.startsWith(canvasId)) _charts[id]?.resetZoom();
}

/**
 * Show a Bootstrap info alert inside the canvas's parent card-body when there is no data.
 * Removes any existing chart on that canvas first.
 */
function showNoData(canvasId, message = 'No data available for this character.') {
  destroyChart(canvasId);
  const canvas = document.getElementById(canvasId);
  // If canvas not found, try to show the message in the nearest .card-body parent
  const parent = canvas?.parentElement
               ?? document.getElementById(canvasId + 'Wrap')?.parentElement
               ?? null;
  if (!parent) return;
  parent.querySelectorAll('.no-data-alert').forEach(el => el.remove());
  if (canvas) canvas.style.display = 'none';
  const alertEl = document.createElement('div');
  alertEl.className = 'alert alert-secondary no-data-alert my-2 mx-2 small';
  alertEl.textContent = message;
  parent.appendChild(alertEl);
}

/** Restore canvas visibility (called before creating a new chart). */
function ensureCanvasVisible(canvasId) {
  const canvas = document.getElementById(canvasId);
  if (!canvas) return;
  canvas.style.display = '';
  canvas.parentElement?.querySelector('.no-data-alert')?.remove();
}

/** Shared zoom/pan plugin config applied to all charts. */
const ZOOM_OPTIONS = {
  // Plain drag pans; Shift+drag is reserved for time-range brushing (see brushPlugin).
  pan:  { enabled: true, mode: 'x', onPanStart: ({ event }) => !(event?.srcEvent?.shiftKey) },
  zoom: {
    wheel:  { enabled: true, modifierKey: 'ctrl' },
    pinch:  { enabled: true },
    mode:   'x',
  },
  limits: { x: { minRange: 1 } },
};

function destroyChart(id) {
  if (_charts[id]) { _charts[id].destroy(); delete _charts[id]; }
}

/** Returns Chart.js color tokens for the current theme (light / dark). */
function chartTheme() {
  const dark = document.documentElement.getAttribute('data-bs-theme') === 'dark';
  return {
    grid:    dark ? 'rgba(232,232,232,0.06)' : 'rgba(26,26,26,0.06)',
    tick:    dark ? '#9a9a9a' : '#6b6b6b',
    legend:  dark ? '#c4c4c4' : '#3a3a3a',
    title:   dark ? '#e8e8e8' : '#1a1a1a',
    subtitle:dark ? '#6b6b6b' : '#9a9a9a',
  };
}

/** Shared scale defaults that apply dark/light grid + tick colors.
 *  `extra.grid` and `extra.ticks` are merged into — not replacing — the themed defaults. */
function themedScale(extra = {}) {
  const t = chartTheme();
  const { grid: eg = {}, ticks: et = {}, ...rest } = extra;
  return {
    grid:  { color: t.grid,  ...eg },
    ticks: { color: t.tick,  ...et },
    ...rest,
  };
}

/** Shared plugin defaults (legend + title text colors).
 *  `extra` is merged on top — legend key is merged, not replaced. */
function themedPlugins(extra = {}) {
  const t = chartTheme();
  const { legend: el = {}, ...rest } = extra;
  return {
    legend: { position: 'bottom', labels: { color: t.legend }, ...el },
    ...rest,
  };
}

// Deterministic color from subsystem name
function subsystemColor(name, alpha = 1) {
  let h = 0;
  for (const c of name) h = (h * 31 + c.charCodeAt(0)) & 0x7fffffff;
  return `hsla(${h % 360}, 65%, 50%, ${alpha})`;
}

// Uniform decimation — keep every k-th point. Fast but flattens spikes; used as
// fallback and for non-numeric series. Prefer downsamplePeaks for value charts.
function downsample(arr, max = 2000) {
  if (arr.length <= max) return arr;
  const step = Math.ceil(arr.length / max);
  return arr.filter((_, i) => i % step === 0);
}

/**
 * Largest-Triangle-Three-Buckets (Steinarsson 2013). Returns the indices of
 * `threshold` points that best preserve the visual shape — peaks and troughs —
 * of the scalar series `ys`, instead of dropping every k-th point. x is the array
 * index (uniform spacing). First and last points are always kept.
 */
function lttbIndices(ys, threshold) {
  const n = ys.length;
  if (threshold >= n || threshold <= 0) return ys.map((_, i) => i);
  if (threshold === 1) return [0];
  if (threshold === 2) return [0, n - 1];

  const out = [0];
  const bucketSize = (n - 2) / (threshold - 2);
  let a = 0;                                   // last selected point
  for (let i = 0; i < threshold - 2; i++) {
    // average of the NEXT bucket (the "third" point of the triangle)
    const avgStart = Math.floor((i + 1) * bucketSize) + 1;
    let avgEnd = Math.min(Math.floor((i + 2) * bucketSize) + 1, n);
    const avgCount = Math.max(1, avgEnd - avgStart);
    let avgX = 0, avgY = 0;
    for (let j = avgStart; j < avgEnd; j++) { avgX += j; avgY += ys[j]; }
    avgX /= avgCount; avgY /= avgCount;

    // pick the point in THIS bucket forming the largest triangle with a + avg
    const rangeStart = Math.floor(i * bucketSize) + 1;
    const rangeEnd = Math.floor((i + 1) * bucketSize) + 1;
    const ax = a, ay = ys[a];
    let maxArea = -1, next = rangeStart;
    for (let j = rangeStart; j < rangeEnd; j++) {
      const area = Math.abs((ax - avgX) * (ys[j] - ay) - (ax - j) * (avgY - ay));
      if (area > maxArea) { maxArea = area; next = j; }
    }
    out.push(next);
    a = next;
  }
  out.push(n - 1);
  return out;
}

/**
 * Peak-preserving downsample of an array of records to ~`max` rows. Runs LTTB over
 * the UNION of all numeric fields (or the given `accessors`) so a spike or trough
 * in ANY plotted series survives — uniform decimation silently drops them.
 * Falls back to uniform decimation when no numeric field is found.
 * @param {object[]} arr
 * @param {number} max
 * @param {Array<(d:object)=>number|null>} [accessors]  one per series; auto-detected if omitted
 */
function downsamplePeaks(arr, max = 2000, accessors = null) {
  if (arr.length <= max) return arr;

  if (!accessors) {
    const keys = new Set();
    // scan a sample of rows — early rows may have null fields
    const stride = Math.max(1, Math.floor(arr.length / 200));
    for (let i = 0; i < arr.length; i += stride) {
      const d = arr[i];
      for (const k in d) if (k !== 'worldTime' && typeof d[k] === 'number') keys.add(k);
    }
    accessors = [...keys].map(k => (d => d[k]));
  }
  if (!accessors.length) return downsample(arr, max);

  const keep = new Set([0, arr.length - 1]);
  const per = Math.max(3, Math.ceil(max / accessors.length));
  for (const acc of accessors) {
    let last = 0;
    // forward-fill nulls so gaps don't read as artificial spikes to zero
    const ys = arr.map(d => {
      const v = acc(d);
      if (v == null || Number.isNaN(v)) return last;
      last = v; return v;
    });
    for (const i of lttbIndices(ys, per)) keep.add(i);
  }
  const idx = [...keep].sort((a, b) => a - b);
  return idx.map(i => arr[i]);
}

// ── Event-overlay markers ──────────────────────────────────────────────────────
// Bridges discrete events (nightmares, emotion shifts…) onto continuous charts so a
// spike in stress/mood is readable against WHAT happened at that moment.

/** Marker accent color from an event "level" token. */
function markerColor(level) {
  return ({
    danger:    'hsla(0,75%,52%,1)',
    warning:   'hsla(38,85%,48%,1)',
    success:   'hsla(140,55%,42%,1)',
    info:      'hsla(200,70%,50%,1)',
    secondary: 'hsla(0,0%,55%,1)',
  })[level] ?? 'hsla(0,0%,55%,1)';
}

/**
 * Maps a worldTime string to the nearest index in an ascending labels array.
 * Labels are ISO-ish ("0100-01-01T05:30:00") so lexical order == chronological.
 */
function nearestLabelIndex(labels, wt) {
  if (!labels.length) return -1;
  let lo = 0, hi = labels.length - 1;
  if (wt <= labels[0]) return 0;
  if (wt >= labels[hi]) return hi;
  while (lo <= hi) {
    const mid = (lo + hi) >> 1;
    if (labels[mid] === wt) return mid;
    if (labels[mid] < wt) lo = mid + 1; else hi = mid - 1;
  }
  // No exact tick match: `lo` is the first tick at/after the event. Events are
  // logged at tick worldTimes so exact match is the norm; snap to `lo` otherwise.
  return Math.min(lo, labels.length - 1);
}

/**
 * Chart.js v4 inline plugin: draws a thin vertical rule + emoji glyph at each
 * marker's worldTime. `markers` = [{ worldTime, icon, color, label }].
 * Reads chart.data.labels so it respects zoom/pan automatically.
 */
function eventMarkerPlugin(markers) {
  return {
    id: 'eventMarkers',
    afterDatasetsDraw(chart) {
      const { ctx, chartArea, scales, data } = chart;
      const x = scales.x;
      const labels = data.labels ?? [];
      if (!x || !labels.length || !markers?.length) return;

      ctx.save();
      ctx.beginPath();
      ctx.rect(chartArea.left, chartArea.top, chartArea.right - chartArea.left, chartArea.bottom - chartArea.top);
      ctx.clip();

      // Vertical stagger so glyphs at adjacent times don't overlap.
      let lastPx = -1e9, row = 0;
      for (const m of markers) {
        const idx = nearestLabelIndex(labels, m.worldTime);
        if (idx < 0) continue;
        const px = x.getPixelForValue(idx);
        if (!Number.isFinite(px) || px < chartArea.left || px > chartArea.right) continue;

        ctx.strokeStyle = (m.color ?? 'hsla(0,0%,55%,1)').replace('1)', '0.45)');
        ctx.lineWidth = 1;
        ctx.setLineDash([3, 3]);
        ctx.beginPath();
        ctx.moveTo(px, chartArea.top);
        ctx.lineTo(px, chartArea.bottom);
        ctx.stroke();

        row = (px - lastPx < 14) ? (row + 1) % 4 : 0;
        lastPx = px;
        if (m.icon) {
          ctx.setLineDash([]);
          ctx.font = '11px sans-serif';
          ctx.textAlign = 'center';
          ctx.textBaseline = 'top';
          ctx.fillText(m.icon, px, chartArea.top + 1 + row * 13);
        }
      }
      ctx.restore();
    },
  };
}

/**
 * Selects notable discrete events and maps them to chart markers. Frequent
 * bookkeeping events (sleep start/phase changes) are excluded to avoid clutter —
 * only rare, high-signal moments get a rule.
 * @param {Array} lists  arrays of parsed events ({worldTime, eventId, icon, level})
 */
// High-signal, relatively rare events only — frequent ones (emotion transitions
// 5103, sleep start/phase changes) are excluded so the overlay stays readable;
// they have their own dedicated charts/lists.
const MARKER_EVENT_IDS = new Set([
  1108,        // Nightmare
  1106, 1110,  // Sleep interrupted / ambush
  5101,        // Nightmare effect on psychology
  5104,        // MoodBaseline shift
  5105,        // Allostatic milestone
  1208,        // Norm-violation shame spike
  2015,        // Dissolution considered
  1600, 1601,  // Life-stage transition / evaluation episode
  1610,        // ToM L2 mutual-knowledge formed
  2016,        // Social comparison occurred (envy/self-esteem impact)
  2017,        // Reputation prior applied at first impression
  1017,        // Dark Core modifier applied (antagonism / prosocial penalty)
  1700, 1701,  // Travel departed / arrived
]);
export function buildEventMarkers(...lists) {
  const out = [];
  for (const list of lists) {
    for (const e of list ?? []) {
      if (!e?.worldTime || !MARKER_EVENT_IDS.has(e.eventId)) continue;
      out.push({ worldTime: e.worldTime, icon: e.icon ?? '•',
                 color: markerColor(e.level), label: e.label ?? '' });
    }
  }
  out.sort((a, b) => a.worldTime < b.worldTime ? -1 : a.worldTime > b.worldTime ? 1 : 0);
  return out;
}

// ── Interval bands (sleep / illness / episodes) ────────────────────────────────
// Faint background shading over a [startWt, endWt] time interval — gives continuous
// charts the "why" context for a recovery: stress drops / energy climbs *because the
// character was asleep*. Generalises phaseBandPlugin (per-index) to arbitrary spans.

const SLEEP_BAND_COLOR        = 'hsla(232,45%,55%,0.11)';  // faint indigo
const SLEEP_BAND_COLOR_BAD    = 'hsla(0,55%,55%,0.10)';    // interrupted/nightmare → reddish

/**
 * Chart.js v4 inline plugin: shades the plot background over each [startWt,endWt]
 * band. Reads chart.data.labels and maps worldTimes via nearestLabelIndex, so it
 * respects zoom/pan and independent (LTTB) downsampling. `bands` = [{startWt, endWt, color}].
 */
function intervalBandPlugin(bands) {
  return {
    id: 'intervalBands',
    beforeDatasetsDraw(chart) {
      const { ctx, chartArea, scales, data } = chart;
      const x = scales.x;
      const labels = data.labels ?? [];
      if (!x || !labels.length || !bands?.length) return;
      ctx.save();
      ctx.beginPath();
      ctx.rect(chartArea.left, chartArea.top, chartArea.right - chartArea.left, chartArea.bottom - chartArea.top);
      ctx.clip();
      for (const b of bands) {
        const i0 = b.startWt != null ? nearestLabelIndex(labels, b.startWt) : 0;
        const i1 = b.endWt   != null ? nearestLabelIndex(labels, b.endWt)   : labels.length - 1;
        let L = x.getPixelForValue(i0), R = x.getPixelForValue(i1);
        if (!Number.isFinite(L) || !Number.isFinite(R)) continue;
        if (R < L) { const t = L; L = R; R = t; }
        L = Math.max(L, chartArea.left);
        R = Math.min(R, chartArea.right);
        if (R - L < 0.5) continue;                       // off-screen or zero-width
        ctx.fillStyle = b.color ?? SLEEP_BAND_COLOR;
        ctx.fillRect(L, chartArea.top, R - L, chartArea.bottom - chartArea.top);
      }
      ctx.restore();
    },
  };
}

/**
 * Maps sleep sessions ({start, end, interrupted, nightmare}) to background bands.
 * Sessions without a known start are skipped (can't place the band); an ongoing
 * session (no end) extends to the chart's right edge.
 */
export function buildSleepBands(sessions) {
  const out = [];
  for (const s of sessions ?? []) {
    if (!s.start) continue;
    out.push({
      startWt: s.start,
      endWt:   s.end ?? null,
      color:   (s.interrupted || s.nightmare) ? SLEEP_BAND_COLOR_BAD : SLEEP_BAND_COLOR,
    });
  }
  return out;
}

// ── Synchronized hover crosshair ───────────────────────────────────────────────
// Hovering any line chart broadcasts the worldTime under the cursor; every other
// line chart draws a vertical rule + value dots at the matching worldTime, so the
// co-occurrence of states across engines (stress ↑ AND sleep-debt ↑ AND cortisol ↑
// at the same tick) is readable without eyeballing timestamps between charts.
// Charts are downsampled independently (LTTB), so they share worldTime, not index.

let _syncHoverWt = null;   // worldTime string currently hovered, or null
let _crosshairRaf = 0;

/** Repaint all line charts (throttled to one frame) to refresh the crosshair. */
function redrawCrosshairCharts() {
  if (_crosshairRaf) return;
  _crosshairRaf = requestAnimationFrame(() => {
    _crosshairRaf = 0;
    for (const id in _charts) {
      const c = _charts[id];
      if (c && c.config?.type === 'line') { try { c.draw(); } catch {} }
    }
  });
}

/** Global Chart.js plugin: captures hover on any line chart and renders the
 *  shared crosshair on all of them. Registered once below. */
const crosshairSyncPlugin = {
  id: 'crosshairSync',

  afterEvent(chart, args) {
    if (chart.config?.type !== 'line') return;
    const e = args.event;
    if (e.type === 'mouseout') {
      if (_syncHoverWt !== null) { _syncHoverWt = null; redrawCrosshairCharts(); }
      return;
    }
    if (e.type !== 'mousemove') return;
    const x = chart.scales.x;
    const labels = chart.data.labels ?? [];
    if (!x || !labels.length || e.x == null) return;
    const idx = Math.round(x.getValueForPixel(e.x));
    if (idx < 0 || idx >= labels.length) return;
    const wt = labels[idx];
    if (wt !== _syncHoverWt) { _syncHoverWt = wt; redrawCrosshairCharts(); }
  },

  afterDatasetsDraw(chart) {
    if (chart.config?.type !== 'line' || _syncHoverWt == null) return;
    const { ctx, chartArea, scales } = chart;
    const labels = chart.data.labels ?? [];
    if (!labels.length) return;
    const idx = nearestLabelIndex(labels, _syncHoverWt);
    if (idx < 0) return;
    const px = scales.x.getPixelForValue(idx);
    if (!Number.isFinite(px) || px < chartArea.left - 0.5 || px > chartArea.right + 0.5) return;

    ctx.save();
    ctx.strokeStyle = chartTheme().tick;
    ctx.globalAlpha = 0.5;
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(px, chartArea.top);
    ctx.lineTo(px, chartArea.bottom);
    ctx.stroke();
    ctx.globalAlpha = 1;

    // value dot on each visible series at this worldTime
    for (let di = 0; di < chart.data.datasets.length; di++) {
      const meta = chart.getDatasetMeta(di);
      if (meta.hidden) continue;
      const pt = meta.data[idx];
      if (!pt || pt.y == null || !Number.isFinite(pt.y)) continue;
      ctx.beginPath();
      ctx.fillStyle = chart.data.datasets[di].borderColor ?? '#888';
      ctx.arc(pt.x, pt.y, 3, 0, Math.PI * 2);
      ctx.fill();
    }
    ctx.restore();
  },
};

if (typeof Chart !== 'undefined') Chart.register(crosshairSyncPlugin);

// ── Time-range brushing ────────────────────────────────────────────────────────
// Shift+drag on any line chart selects a [from,to] worldTime range and broadcasts
// it (CustomEvent 'brush-changed'); list views filter their rows to that window.
// A plain click (no drag) clears the selection.

let _brushDrag  = null;   // { chart, x0, x1 } while dragging (canvas px)
let _brushRange = null;   // committed { from, to } worldTimes, or null

export function getBrushRange() { return _brushRange; }

function commitBrush(range) {
  _brushRange = range;
  document.dispatchEvent(new CustomEvent('brush-changed', { detail: range }));
  redrawCrosshairCharts();   // repaint committed-range dimming on every chart
}
export function clearBrush() { commitBrush(null); }

const brushPlugin = {
  id: 'brushSelect',
  afterDatasetsDraw(chart) {
    if (chart.config?.type !== 'line') return;
    const { ctx, chartArea, scales } = chart;

    if (_brushDrag && _brushDrag.chart === chart) {        // live drag rectangle
      const lo = Math.min(_brushDrag.x0, _brushDrag.x1);
      const hi = Math.max(_brushDrag.x0, _brushDrag.x1);
      ctx.save();
      ctx.fillStyle = 'hsla(210,90%,55%,0.15)';
      ctx.fillRect(lo, chartArea.top, hi - lo, chartArea.bottom - chartArea.top);
      ctx.strokeStyle = 'hsla(210,90%,55%,0.65)';
      ctx.lineWidth = 1;
      ctx.strokeRect(lo, chartArea.top, hi - lo, chartArea.bottom - chartArea.top);
      ctx.restore();
      return;
    }

    if (_brushRange) {                                     // dim outside committed range
      const labels = chart.data.labels ?? [];
      if (!labels.length) return;
      let L = scales.x.getPixelForValue(nearestLabelIndex(labels, _brushRange.from));
      let R = scales.x.getPixelForValue(nearestLabelIndex(labels, _brushRange.to));
      if (!Number.isFinite(L) || !Number.isFinite(R)) return;
      if (R < L) { const t = L; L = R; R = t; }
      L = Math.max(L, chartArea.left); R = Math.min(R, chartArea.right);
      ctx.save();
      ctx.fillStyle = 'hsla(210,12%,50%,0.20)';
      ctx.fillRect(chartArea.left, chartArea.top, L - chartArea.left, chartArea.bottom - chartArea.top);
      ctx.fillRect(R, chartArea.top, chartArea.right - R, chartArea.bottom - chartArea.top);
      ctx.restore();
    }
  },
};

function initBrushControls() {
  const clampX = (chart, px) =>
    Math.max(chart.chartArea.left, Math.min(chart.chartArea.right, px));

  document.addEventListener('mousedown', e => {
    if (!e.shiftKey || e.button !== 0 || e.target?.tagName !== 'CANVAS') return;
    const chart = Chart.getChart(e.target);
    if (!chart || chart.config?.type !== 'line') return;
    const x = e.clientX - e.target.getBoundingClientRect().left;
    if (x < chart.chartArea.left || x > chart.chartArea.right) return;
    _brushDrag = { chart, x0: x, x1: x };
    e.preventDefault();                  // suppress text selection during brush
  });

  document.addEventListener('mousemove', e => {
    if (!_brushDrag) return;
    const { chart } = _brushDrag;
    _brushDrag.x1 = clampX(chart, e.clientX - chart.canvas.getBoundingClientRect().left);
    chart.draw();
  });

  document.addEventListener('mouseup', () => {
    if (!_brushDrag) return;
    const { chart, x0, x1 } = _brushDrag;
    _brushDrag = null;
    const lo = Math.min(x0, x1), hi = Math.max(x0, x1);
    if (hi - lo < 4) { commitBrush(null); chart.draw(); return; }   // click → clear
    const labels = chart.data.labels ?? [];
    const xs = chart.scales.x;
    const clampIdx = i => Math.max(0, Math.min(labels.length - 1, Math.round(i)));
    const from = labels[clampIdx(xs.getValueForPixel(lo))];
    const to   = labels[clampIdx(xs.getValueForPixel(hi))];
    commitBrush(from <= to ? { from, to } : { from: to, to: from });
    chart.draw();
  });
}

if (typeof Chart !== 'undefined') { Chart.register(brushPlugin); initBrushControls(); }

export function renderFrequencyChart(canvasId, data) {
  if (!data?.buckets?.length) { showNoData(canvasId, 'No event data found for this character.'); return; }
  ensureCanvasVisible(canvasId);
  destroyChart(canvasId);
  const ctx = document.getElementById(canvasId)?.getContext('2d');
  if (!ctx) return;
  const datasets = data.subsystems.map(ss => ({
    label: ss,
    data: data.counts[ss],
    backgroundColor: subsystemColor(ss, 0.8),
    stack: 'stack'
  }));
  _charts[canvasId] = new Chart(ctx, {
    type: 'bar',
    data: { labels: data.buckets, datasets },
    options: {
      responsive: true,
      plugins: { ...themedPlugins(), zoom: ZOOM_OPTIONS },
      scales: {
        x: { stacked: true, ...themedScale() },
        y: { stacked: true, beginAtZero: true, ...themedScale() },
      },
    },
  });
}

/** Energy & Needs chart: Energy / Hunger / Thirst / SleepDebt (EventId 5000) */
export function renderPhysioNeedsChart(canvasId, data, bands = []) {
  const NO_DATA = 'No physiology snapshots found (EventId 5000 not in logs).';
  if (!data?.length) { showNoData(canvasId, NO_DATA); return; }
  ensureCanvasVisible(canvasId);
  destroyChart(canvasId);
  const ctx = document.getElementById(canvasId)?.getContext('2d');
  if (!ctx) return;
  const ds = downsamplePeaks(data);
  const labels = ds.map(d => d.worldTime);
  const line = (label, key, color, yAxisID = 'y') => ({
    label, data: ds.map(d => d[key] ?? null),
    borderColor: color, backgroundColor: color.replace('1)', '0.08)'),
    borderWidth: 2, pointRadius: 0, tension: 0.3, spanGaps: true, yAxisID,
  });
  _charts[canvasId] = new Chart(ctx, {
    type: 'line',
    data: { labels, datasets: [
      line('Energy',     'energy',    'hsla(120,65%,38%,1)'),
      line('Hunger',     'hunger',    'hsla(25,85%,50%,1)'),
      line('Thirst',     'thirst',    'hsla(195,80%,45%,1)'),
      line('Sleep Debt', 'sleepDebt', 'hsla(270,65%,52%,1)', 'yDebt'),
    ]},
    plugins: bands.length ? [intervalBandPlugin(bands)] : [],
    options: {
      responsive: true,
      plugins: { ...themedPlugins(), zoom: ZOOM_OPTIONS },
      scales: {
        y:     { ...themedScale(), type: 'linear', position: 'left',  min: 0, max: 100, title: { display: true, text: '0 – 100', color: chartTheme().tick } },
        yDebt: { ...themedScale({ grid: { drawOnChartArea: false } }), type: 'linear', position: 'right', min: 0, max: 16, title: { display: true, text: 'Debt (h)', color: chartTheme().tick } },
      },
    },
  });
}

/**
 * Health chart: Pain / Immune Load / Body Temp Delta (EventId 5000).
 * currentSnap: optional profile.snapshot.physiology — adds Allostatic Load and
 * Cortisol as flat dashed reference lines showing the CURRENT state (historical
 * data for these markers is not included in EventId 5000 log messages).
 */
export function renderPhysioHealthChart(canvasId, data, currentSnap = null) {
  const NO_DATA = 'No physiology snapshots found (EventId 5000 not in logs).';
  if (!data?.length) { showNoData(canvasId, NO_DATA); return; }
  ensureCanvasVisible(canvasId);
  destroyChart(canvasId);
  const ctx = document.getElementById(canvasId)?.getContext('2d');
  if (!ctx) return;
  const ds = downsamplePeaks(data);
  const labels = ds.map(d => d.worldTime);
  const line = (label, key, color, yAxisID = 'y') => ({
    label, data: ds.map(d => d[key] ?? null),
    borderColor: color, backgroundColor: color.replace('1)', '0.08)'),
    borderWidth: 2, pointRadius: 0, tension: 0.3, spanGaps: true, yAxisID,
  });
  const flatLine = (label, value, color) => ({
    label: `${label} (current: ${Math.round(value)})`,
    data: ds.map(() => value),
    borderColor: color,
    borderDash: [5, 4],
    borderWidth: 1.5,
    pointRadius: 0,
    tension: 0,
    fill: false,
    yAxisID: 'y',
  });

  const datasets = [
    line('Pain',        'pain',   'hsla(0,80%,48%,1)'),
    line('Immune Load', 'immune', 'hsla(38,85%,50%,1)'),
    line('Temp Δ °C',  'temp',   'hsla(210,75%,52%,1)', 'yTemp'),
  ];

  // Add current-state reference lines if profile snapshot provides them
  if (currentSnap?.allostaticLoad != null)
    datasets.push(flatLine('Allostatic Load', currentSnap.allostaticLoad, 'hsla(20,75%,42%,0.85)'));
  if (currentSnap?.cortisolLevel != null)
    datasets.push(flatLine('Cortisol',        currentSnap.cortisolLevel,  'hsla(280,60%,52%,0.85)'));

  _charts[canvasId] = new Chart(ctx, {
    type: 'line',
    data: { labels, datasets },
    options: {
      responsive: true,
      plugins: { ...themedPlugins(), zoom: ZOOM_OPTIONS },
      scales: {
        y:     { ...themedScale(), type: 'linear', position: 'left',  min: 0, max: 100, title: { display: true, text: '0 – 100', color: chartTheme().tick } },
        yTemp: { ...themedScale({ grid: { drawOnChartArea: false }, ticks: { callback: v => (v >= 0 ? '+' : '') + v.toFixed(1) } }), type: 'linear', position: 'right', min: -3, max: 3,
                 title: { display: true, text: '°C delta', color: chartTheme().tick } },
      },
    },
  });
}

/** Sleep quality bar chart — one bar per session, colored by quality score. */
export function renderSleepQualityChart(canvasId, sessions) {
  destroyChart(canvasId);
  const ctx = document.getElementById(canvasId)?.getContext('2d');
  if (!ctx) return;
  const valid = sessions.filter(s => s.quality != null);
  if (!valid.length) return;

  const labels = valid.map((s, i) => s.start ? s.start.slice(0, 10) : `#${i + 1}`);
  const values = valid.map(s => Math.round(s.quality));
  const colors = values.map(q => q > 70 ? 'hsla(120,55%,42%,0.8)' : q > 40 ? 'hsla(45,85%,50%,0.8)' : 'hsla(0,75%,50%,0.8)');

  _charts[canvasId] = new Chart(ctx, {
    type: 'bar',
    data: { labels, datasets: [{ label: 'Sleep Quality', data: values, backgroundColor: colors, borderRadius: 3 }] },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { display: false },
        zoom: { pan: { enabled: true, mode: 'x' }, zoom: { wheel: { enabled: true, modifierKey: 'ctrl' }, mode: 'x' } },
        tooltip: { callbacks: { afterLabel: (ctx) => {
          const s = valid[ctx.dataIndex];
          return `${s.hours?.toFixed(1) ?? '?'}h${s.interrupted ? ' · interrupted' : ''}${s.nightmare ? ' · nightmare' : ''}`;
        }}},
      },
      scales: { y: { ...themedScale(), min: 0, max: 100 }, x: { ...themedScale(), ticks: { color: chartTheme().tick, font: { size: 9 } } } },
    },
  });
}

/** Legacy alias — kept so any leftover calls don't crash */
export function renderPhysiologyChart(canvasId, data) {
  renderPhysioNeedsChart(canvasId, data);
}

/** Legacy single-chart alias (kept for compatibility). */
export function renderPsychologyChart(canvasId, data) {
  renderPsychVADChart(canvasId, data);
}

const NO_PSYCH = 'No psychology snapshots found (EventId 5100 not in logs).';

/** Chart 1 — Valence / Arousal / Dominance (PAD emotional space). */
export function renderPsychVADChart(canvasId, data, markers = []) {
  if (!data?.length) { showNoData(canvasId, NO_PSYCH); return; }
  ensureCanvasVisible(canvasId);
  destroyChart(canvasId);
  const ctx = document.getElementById(canvasId)?.getContext('2d');
  if (!ctx) return;
  const ds = downsamplePeaks(data);
  const labels = ds.map(d => d.worldTime);
  const line = (label, key, color) => ({
    label, data: ds.map(d => d[key] ?? null),
    borderColor: color, backgroundColor: color.replace('1)', '0.08)'),
    borderWidth: 2, pointRadius: 0, tension: 0.3, spanGaps: true,
  });
  _charts[canvasId] = new Chart(ctx, {
    type: 'line',
    data: { labels, datasets: [
      line('Valence',   'valence',   'hsla(120,70%,40%,1)'),
      line('Arousal',   'arousal',   'hsla(35,85%,52%,1)'),
      line('Dominance', 'dominance', 'hsla(200,75%,48%,1)'),
    ]},
    plugins: markers.length ? [eventMarkerPlugin(markers)] : [],
    options: {
      responsive: true,
      plugins: { ...themedPlugins(), zoom: ZOOM_OPTIONS },
      scales: {
        y: { ...themedScale({ ticks: { callback: v => (v >= 0 ? '+' : '') + v.toFixed(1) } }), type: 'linear', min: -1, max: 1,
             title: { display: true, text: '−1 … +1', color: chartTheme().tick } },
      },
    },
  });
}

/** Chart 2 — Stress & Cognitive Load (0–100). */
export function renderPsychStressChart(canvasId, data, markers = [], bands = []) {
  if (!data?.length) { showNoData(canvasId, NO_PSYCH); return; }
  ensureCanvasVisible(canvasId);
  destroyChart(canvasId);
  const ctx = document.getElementById(canvasId)?.getContext('2d');
  if (!ctx) return;
  const ds = downsamplePeaks(data);
  const labels = ds.map(d => d.worldTime);
  const line = (label, key, color) => ({
    label, data: ds.map(d => d[key] ?? null),
    borderColor: color, backgroundColor: color.replace('1)', '0.1)'),
    borderWidth: 2, pointRadius: 0, tension: 0.3, spanGaps: true,
  });
  // Danger threshold annotation (stress=70) as flat reference dataset
  const refLine = {
    label: 'Stress threshold (70)',
    data: ds.map(() => 70),
    borderColor: 'hsla(0,80%,55%,0.4)',
    borderDash: [4, 4], borderWidth: 1, pointRadius: 0, tension: 0, fill: false,
  };
  _charts[canvasId] = new Chart(ctx, {
    type: 'line',
    data: { labels, datasets: [
      line('Stress',   'stress',  'hsla(0,78%,50%,1)'),     // red
      line('CogLoad',  'cogLoad', 'hsla(270,65%,52%,1)'),   // purple
      refLine,
    ]},
    plugins: [
      ...(bands.length   ? [intervalBandPlugin(bands)]   : []),
      ...(markers.length ? [eventMarkerPlugin(markers)] : []),
    ],
    options: {
      responsive: true,
      plugins: { ...themedPlugins(), zoom: ZOOM_OPTIONS },
      scales: { y: { ...themedScale(), type: 'linear', min: 0, max: 100, title: { display: true, text: '0 – 100', color: chartTheme().tick } } },
    },
  });
}

/**
 * Chart 3 — HPA axis & mood: Stress + Cortisol + Allostatic Load + MoodBaseline over time.
 * Cortisol/Allostatic come from physiology snapshots (5000), Stress/MoodBaseline from
 * psychology (5100); both engines snapshot in the same tick so they share worldTime.
 */
export function renderStressMoodChart(canvasId, psychSnaps, physioSnaps, markers = [], bands = []) {
  if (!psychSnaps?.length) { showNoData(canvasId, NO_PSYCH); return; }
  ensureCanvasVisible(canvasId);
  destroyChart(canvasId);
  const ctx = document.getElementById(canvasId)?.getContext('2d');
  if (!ctx) return;

  const physByWt = new Map((physioSnaps ?? []).map(s => [s.worldTime, s]));
  const merged = psychSnaps.map(s => {
    const ph = physByWt.get(s.worldTime);
    return {
      worldTime:  s.worldTime,
      stress:     s.stress,
      mood:       s.moodBaseline,
      cortisol:   ph?.cortisol ?? null,
      allostatic: ph?.allostaticLoad ?? null,
    };
  });
  const ds = downsamplePeaks(merged);
  const labels = ds.map(d => d.worldTime);
  const line = (label, key, color) => ({
    label, data: ds.map(d => d[key] ?? null),
    borderColor: color, backgroundColor: color.replace('1)', '0.08)'),
    borderWidth: 2, pointRadius: 0, tension: 0.3, spanGaps: true,
  });
  _charts[canvasId] = new Chart(ctx, {
    type: 'line',
    data: { labels, datasets: [
      line('Stress',          'stress',     'hsla(0,78%,50%,1)'),    // red
      line('Cortisol',        'cortisol',   'hsla(280,60%,52%,1)'),  // purple
      line('Allostatic Load', 'allostatic', 'hsla(20,75%,45%,1)'),   // burnt orange
      line('MoodBaseline',    'mood',       'hsla(140,60%,42%,1)'),  // green
    ]},
    plugins: [
      ...(bands.length   ? [intervalBandPlugin(bands)]   : []),
      ...(markers.length ? [eventMarkerPlugin(markers)] : []),
    ],
    options: {
      responsive: true,
      plugins: { ...themedPlugins(), zoom: ZOOM_OPTIONS },
      scales: { y: { ...themedScale(), min: 0, max: 100, title: { display: true, text: '0 – 100', color: chartTheme().tick } } },
    },
  });
}

/** Psychological motivations (drives) over time — the "what pulls them" layer (5100). */
export function renderMotivationsChart(canvasId, data) {
  const hasMot = data?.some(s => s.needSocial != null);
  if (!data?.length || !hasMot) { showNoData(canvasId, 'No motivation data in psychology snapshots (regenerate logs).'); return; }
  ensureCanvasVisible(canvasId);
  destroyChart(canvasId);
  const ctx = document.getElementById(canvasId)?.getContext('2d');
  if (!ctx) return;
  const ds = downsamplePeaks(data);
  const labels = ds.map(d => d.worldTime);
  const line = (label, key, color) => ({
    label, data: ds.map(d => d[key] ?? null),
    borderColor: color, backgroundColor: color.replace('1)', '0.08)'),
    borderWidth: 2, pointRadius: 0, tension: 0.3, spanGaps: true,
  });
  _charts[canvasId] = new Chart(ctx, {
    type: 'line',
    data: { labels, datasets: [
      line('Social',      'needSocial',      'hsla(200,75%,50%,1)'),
      line('Intimacy',    'needIntimacy',    'hsla(330,80%,55%,1)'),
      line('Achievement', 'needAchievement', 'hsla(40,85%,50%,1)'),
      line('Care',        'needCare',        'hsla(140,60%,45%,1)'),
      line('Safety',      'needSafety',      'hsla(0,75%,50%,1)'),
    ]},
    options: {
      responsive: true,
      plugins: { ...themedPlugins(), zoom: ZOOM_OPTIONS },
      scales: { y: { ...themedScale(), min: 0, max: 100, title: { display: true, text: '0 – 100', color: chartTheme().tick } } },
    },
  });
}

/** Self-concept over time (5300): global self-esteem + ideal/perceived discrepancy.
 *  Esteem/discrepancy are 0–1 → shown on a 0–100 axis (×100). Perceived vs ideal
 *  Big Five (E/A) drawn as faint dashed references. Accepts event markers. */
export function renderSelfConceptChart(canvasId, snaps, markers = []) {
  if (!snaps?.length) { showNoData(canvasId, 'No self-concept data (EventId 5300 not in logs).'); return; }
  ensureCanvasVisible(canvasId);
  destroyChart(canvasId);
  const ctx = document.getElementById(canvasId)?.getContext('2d');
  if (!ctx) return;
  const ds = downsamplePeaks(snaps);
  const labels = ds.map(d => d.worldTime);
  const line = (label, key, color, dash = []) => ({
    label, data: ds.map(d => d[key] != null ? d[key] * 100 : null),
    borderColor: color, backgroundColor: color.replace('1)', '0.08)'),
    borderWidth: dash.length ? 1.25 : 2, borderDash: dash,
    pointRadius: 0, tension: 0.3, spanGaps: true,
  });
  _charts[canvasId] = new Chart(ctx, {
    type: 'line',
    data: { labels, datasets: [
      line('Self-esteem',      'esteem',      'hsla(140,60%,42%,1)'),
      line('Self-discrepancy', 'discrepancy', 'hsla(15,80%,50%,1)'),
      line('Perceived E',      'percExtra',   'hsla(200,70%,50%,1)', [4,3]),
      line('Ideal E',          'idealExtra',  'hsla(200,70%,50%,1)', [1,3]),
      line('Perceived A',      'percAgree',   'hsla(280,60%,55%,1)', [4,3]),
      line('Ideal A',          'idealAgree',  'hsla(280,60%,55%,1)', [1,3]),
    ]},
    plugins: markers.length ? [eventMarkerPlugin(markers)] : [],
    options: {
      responsive: true,
      plugins: { ...themedPlugins(), zoom: ZOOM_OPTIONS },
      scales: { y: { ...themedScale(), min: 0, max: 100, title: { display: true, text: '0 – 100 (×100)', color: chartTheme().tick } } },
    },
  });
}

/** RIASEC interest profile over time (5400): current 6 dims (solid) + baseline 6 dims (dashed).
 *  Values are 0..1 on a 0–100 axis (×100). Accepts event markers. */
export function renderInterestsChart(canvasId, data, markers = []) {
  if (!data?.length) { showNoData(canvasId, 'No interests data (EventId 5400 not in logs).'); return; }
  ensureCanvasVisible(canvasId);
  destroyChart(canvasId);
  const ctx = document.getElementById(canvasId)?.getContext('2d');
  if (!ctx) return;
  const ds = downsamplePeaks(data);
  const labels = ds.map(d => d.worldTime);
  const line = (label, key, color, dash = []) => ({
    label, data: ds.map(d => d[key] != null ? d[key] * 100 : null),
    borderColor: color, backgroundColor: color.replace('1)', '0.06)'),
    borderWidth: dash.length ? 1.0 : 2, borderDash: dash,
    pointRadius: 0, tension: 0.3, spanGaps: true,
  });
  _charts[canvasId] = new Chart(ctx, {
    type: 'line',
    data: { labels, datasets: [
      line('Realistic (cur)',    'realistic',        'hsla(20,75%,50%,1)'),
      line('Investigative (cur)','investigative',     'hsla(200,75%,45%,1)'),
      line('Artistic (cur)',     'artistic',          'hsla(280,65%,55%,1)'),
      line('Social (cur)',       'social',            'hsla(140,60%,42%,1)'),
      line('Enterprising (cur)', 'enterprising',      'hsla(45,90%,45%,1)'),
      line('Conventional (cur)', 'conventional',      'hsla(0,0%,50%,1)'),
      line('Realistic (base)',   'baseRealistic',     'hsla(20,75%,50%,0.4)',    [4,3]),
      line('Investigative (base)','baseInvestigative','hsla(200,75%,45%,0.4)',   [4,3]),
      line('Artistic (base)',    'baseArtistic',      'hsla(280,65%,55%,0.4)',   [4,3]),
      line('Social (base)',      'baseSocial',        'hsla(140,60%,42%,0.4)',   [4,3]),
      line('Enterprising (base)','baseEnterprising',  'hsla(45,90%,45%,0.4)',   [4,3]),
      line('Conventional (base)','baseConventional',  'hsla(0,0%,50%,0.4)',     [4,3]),
    ]},
    plugins: markers.length ? [eventMarkerPlugin(markers)] : [],
    options: {
      responsive: true,
      plugins: { ...themedPlugins(), zoom: ZOOM_OPTIONS },
      scales: { y: { ...themedScale(), min: 0, max: 100, title: { display: true, text: 'RIASEC 0–100 (×100)', color: chartTheme().tick } } },
    },
  });
}

/** Schwartz Basic Human Values drift over time (5700): 10 Current lines with their
 *  Baseline as a dashed/faded reference line each, mirroring renderInterestsChart. */
export function renderValuesChart(canvasId, data, markers = []) {
  if (!data?.length) { showNoData(canvasId, 'No values data (EventId 5700 not in logs).'); return; }
  ensureCanvasVisible(canvasId);
  destroyChart(canvasId);
  const ctx = document.getElementById(canvasId)?.getContext('2d');
  if (!ctx) return;
  const ds = downsamplePeaks(data);
  const labels = ds.map(d => d.worldTime);
  const line = (label, key, color, dash = []) => ({
    label, data: ds.map(d => d[key] != null ? d[key] * 100 : null),
    borderColor: color, backgroundColor: color.replace('1)', '0.06)'),
    borderWidth: dash.length ? 1.0 : 2, borderDash: dash,
    pointRadius: 0, tension: 0.3, spanGaps: true,
  });
  // 10 distinct hues spread evenly around the wheel, one per Schwartz value.
  const VALUE_HUES = {
    benevolence: 140, universalism: 160, selfDirection: 260, stimulation: 285,
    hedonism: 315, achievement: 45, power: 0, security: 200, conformity: 220, tradition: 30,
  };
  const VALUE_LABELS = {
    benevolence: 'Benevolence', universalism: 'Universalism', selfDirection: 'Self-Direction',
    stimulation: 'Stimulation', hedonism: 'Hedonism', achievement: 'Achievement',
    power: 'Power', security: 'Security', conformity: 'Conformity', tradition: 'Tradition',
  };
  const datasets = [];
  for (const key of Object.keys(VALUE_HUES)) {
    const hue = VALUE_HUES[key];
    datasets.push(line(`${VALUE_LABELS[key]} (cur)`, key, `hsla(${hue},70%,48%,1)`));
  }
  for (const key of Object.keys(VALUE_HUES)) {
    const hue = VALUE_HUES[key];
    const baseKey = 'base' + key.charAt(0).toUpperCase() + key.slice(1);
    datasets.push(line(`${VALUE_LABELS[key]} (base)`, baseKey, `hsla(${hue},70%,48%,0.4)`, [4, 3]));
  }
  _charts[canvasId] = new Chart(ctx, {
    type: 'line',
    data: { labels, datasets },
    plugins: markers.length ? [eventMarkerPlugin(markers)] : [],
    options: {
      responsive: true,
      plugins: { ...themedPlugins(), zoom: ZOOM_OPTIONS },
      scales: { y: { ...themedScale(), min: 0, max: 100, title: { display: true, text: 'Schwartz values 0–100 (×100)', color: chartTheme().tick } } },
    },
  });
}

/** Dark Core (D-factor) over time (5500): DarkCore and JustifyingBeliefs as two lines. */
export function renderDarkCoreChart(canvasId, data, markers = []) {
  if (!data?.length) { showNoData(canvasId, 'No Dark Core data (EventId 5500 not in logs — character may predate Stage 3).'); return; }
  ensureCanvasVisible(canvasId);
  destroyChart(canvasId);
  const ctx = document.getElementById(canvasId)?.getContext('2d');
  if (!ctx) return;
  const ds = downsamplePeaks(data);
  const labels = ds.map(d => d.worldTime);
  const line = (label, key, color) => ({
    label, data: ds.map(d => d[key] != null ? d[key] * 100 : null),
    borderColor: color, backgroundColor: color.replace('1)', '0.08)'),
    borderWidth: 2, pointRadius: 0, tension: 0.3, spanGaps: true,
  });
  _charts[canvasId] = new Chart(ctx, {
    type: 'line',
    data: { labels, datasets: [
      line('D-factor (DarkCore)',       'darkCore',          'hsla(0,75%,48%,1)'),
      line('Justifying Beliefs',        'justifyingBeliefs', 'hsla(30,80%,50%,1)'),
    ]},
    plugins: markers.length ? [eventMarkerPlugin(markers)] : [],
    options: {
      responsive: true,
      plugins: { ...themedPlugins(), zoom: ZOOM_OPTIONS },
      scales: { y: { ...themedScale(), min: 0, max: 100, title: { display: true, text: 'D-factor 0–100 (×100)', color: chartTheme().tick } } },
    },
  });
}

/** SDT basic-need appraisal over time (5210): per-channel Balance (Satisfaction − 1.5×Frustration)
 *  for Competence/Relatedness/Autonomy, plus the GlobalBalance average. Balance ranges roughly
 *  [-1.5..1] so the axis auto-scales rather than being pinned to 0–100. Accepts event markers. */
export function renderNeedAppraisalChart(canvasId, data, markers = []) {
  if (!data?.length) { showNoData(canvasId, 'No need-appraisal data (EventId 5210 not in logs — character may predate Stage 4E).'); return; }
  ensureCanvasVisible(canvasId);
  destroyChart(canvasId);
  const ctx = document.getElementById(canvasId)?.getContext('2d');
  if (!ctx) return;
  const merged = data.map(d => ({
    worldTime: d.worldTime,
    competenceBalance:  d.competenceSatisfaction  - 1.5 * d.competenceFrustration,
    relatednessBalance: d.relatednessSatisfaction - 1.5 * d.relatednessFrustration,
    autonomyBalance:    d.autonomySatisfaction    - 1.5 * d.autonomyFrustration,
    globalBalance:      d.globalBalance,
  }));
  const ds = downsamplePeaks(merged);
  const labels = ds.map(d => d.worldTime);
  const line = (label, key, color, dash = []) => ({
    label, data: ds.map(d => d[key] ?? null),
    borderColor: color, backgroundColor: color.replace('1)', '0.08)'),
    borderWidth: dash.length ? 1.5 : 2, borderDash: dash,
    pointRadius: 0, tension: 0.3, spanGaps: true,
  });
  _charts[canvasId] = new Chart(ctx, {
    type: 'line',
    data: { labels, datasets: [
      line('Competence balance',  'competenceBalance',  'hsla(200,75%,45%,1)'),
      line('Relatedness balance', 'relatednessBalance',  'hsla(330,80%,55%,1)'),
      line('Autonomy balance',    'autonomyBalance',     'hsla(45,90%,45%,1)'),
      line('Global balance',      'globalBalance',       'hsla(0,0%,40%,1)', [4,3]),
    ]},
    plugins: markers.length ? [eventMarkerPlugin(markers)] : [],
    options: {
      responsive: true,
      plugins: { ...themedPlugins(), zoom: ZOOM_OPTIONS },
      scales: { y: { ...themedScale(), title: { display: true, text: 'Balance (Satisfaction − 1.5×Frustration)', color: chartTheme().tick } } },
    },
  });
}

/** Nutrition over time (5000): Calories, VitaminD, Iron, Protein, Blood Glucose. */
export function renderNutritionChart(canvasId, physioSnaps) {
  const data = physioSnaps ?? [];
  const hasNut = data.some(s => s.calories != null);
  if (!data.length || !hasNut) { showNoData(canvasId, 'No nutrition data in physiology snapshots (regenerate logs).'); return; }
  ensureCanvasVisible(canvasId);
  destroyChart(canvasId);
  const ctx = document.getElementById(canvasId)?.getContext('2d');
  if (!ctx) return;
  const ds = downsamplePeaks(data);
  const labels = ds.map(d => d.worldTime);
  const line = (label, key, color) => ({
    label, data: ds.map(d => d[key] ?? null),
    borderColor: color, backgroundColor: color.replace('1)', '0.08)'),
    borderWidth: 2, pointRadius: 0, tension: 0.3, spanGaps: true,
  });
  _charts[canvasId] = new Chart(ctx, {
    type: 'line',
    data: { labels, datasets: [
      line('Calories',      'calories',     'hsla(40,85%,50%,1)'),
      line('Vitamin D',     'vitaminD',     'hsla(50,90%,55%,1)'),
      line('Iron',          'iron',         'hsla(0,70%,50%,1)'),
      line('Protein',       'protein',      'hsla(280,55%,55%,1)'),
      line('Blood Glucose', 'bloodGlucose', 'hsla(200,70%,50%,1)'),
    ]},
    options: {
      responsive: true,
      plugins: { ...themedPlugins(), zoom: ZOOM_OPTIONS },
      scales: { y: { ...themedScale(), min: 0, max: 100, title: { display: true, text: '0 – 100', color: chartTheme().tick } } },
    },
  });
}

/** Physical aging over time (5000): grey hair, wrinkles, hair/muscle/bone — long-run drift. */
export function renderAgingChart(canvasId, physioSnaps) {
  const data = physioSnaps ?? [];
  const hasAging = data.some(s => s.greyFraction != null || s.wrinkleScore != null);
  if (!data.length || !hasAging) { showNoData(canvasId, 'No aging data in physiology snapshots (regenerate logs).'); return; }
  ensureCanvasVisible(canvasId);
  destroyChart(canvasId);
  const ctx = document.getElementById(canvasId)?.getContext('2d');
  if (!ctx) return;
  const ds = downsamplePeaks(data);
  const labels = ds.map(d => d.worldTime);
  // 0–1 fractions → percent; WrinkleScore is already 0–100.
  const line = (label, key, color, scale = 1) => ({
    label, data: ds.map(d => d[key] != null ? d[key] * scale : null),
    borderColor: color, backgroundColor: color.replace('1)', '0.08)'),
    borderWidth: 2, pointRadius: 0, tension: 0.3, spanGaps: true,
  });
  _charts[canvasId] = new Chart(ctx, {
    type: 'line',
    data: { labels, datasets: [
      line('Grey hair %',    'greyFraction', 'hsla(0,0%,55%,1)',   100),
      line('Wrinkles',       'wrinkleScore', 'hsla(30,45%,45%,1)', 1),
      line('Hair density %', 'hairDensity',  'hsla(25,60%,40%,1)', 100),
      line('Muscle mass %',  'muscleMass',   'hsla(0,65%,45%,1)',  100),
      line('Bone density %', 'boneDensity',  'hsla(205,30%,55%,1)',100),
    ]},
    options: {
      responsive: true,
      plugins: { ...themedPlugins(), zoom: ZOOM_OPTIONS },
      scales: { y: { ...themedScale(), min: 0, max: 100, title: { display: true, text: '% / score', color: chartTheme().tick } } },
    },
  });
}

// Menstrual cycle phase → faint background tint for the hormone/cycle chart.
const CYCLE_PHASE_COLORS = {
  Menses:     'hsla(0,70%,55%,0.13)',
  Follicular: 'hsla(45,50%,55%,0.07)',
  Ovulation:  'hsla(140,60%,45%,0.15)',
  Luteal:     'hsla(38,85%,55%,0.12)',
};

/** Chart.js v4 inline plugin: shades the plot background by cycle phase per x-index. */
function phaseBandPlugin(phases) {
  return {
    id: 'cyclePhaseBands',
    beforeDatasetsDraw(chart) {
      const { ctx, chartArea, scales } = chart;
      const x = scales.x;
      const n = phases?.length ?? 0;
      if (!x || n === 0) return;
      // getPixelForValue(index) on the category axis (respects zoom); linear fallback.
      const px = i => {
        const p = x.getPixelForValue(i);
        return Number.isFinite(p) ? p : chartArea.left + (i / n) * (chartArea.right - chartArea.left);
      };
      const edge = i => i <= 0 ? chartArea.left : i >= n ? chartArea.right : (px(i - 1) + px(i)) / 2;
      ctx.save();
      ctx.beginPath();
      ctx.rect(chartArea.left, chartArea.top, chartArea.right - chartArea.left, chartArea.bottom - chartArea.top);
      ctx.clip();               // clip bands to the plot area (handles zoom overflow)
      let start = 0;
      for (let i = 1; i <= n; i++) {
        if (i === n || phases[i] !== phases[start]) {
          const color = CYCLE_PHASE_COLORS[phases[start]];
          if (color) {
            const L = edge(start), R = edge(i);
            ctx.fillStyle = color;
            ctx.fillRect(L, chartArea.top, R - L, chartArea.bottom - chartArea.top);
          }
          start = i;
        }
      }
      ctx.restore();
    },
  };
}

/**
 * Sex hormones & menstrual cycle over time. Sex-adaptive: testosterone (from 5000
 * snapshots) shows for males; cycle metrics (from 5001 cycle events) show for fertile
 * females — libido on a secondary axis, symptoms as lines, over phase-coloured bands.
 */
export function renderHormoneCycleChart(canvasId, physioSnaps, cycleEvents) {
  const phys = physioSnaps ?? [];
  const cyc  = cycleEvents ?? [];
  const hasTesto = phys.some(s => (s.testosterone ?? 0) > 0);
  const hasCycle = cyc.length > 0;
  if (!hasTesto && !hasCycle) {
    showNoData(canvasId, 'No sex-hormone or cycle data (testosterone / EventId 5001).');
    return;
  }
  ensureCanvasVisible(canvasId);
  destroyChart(canvasId);
  const ctx = document.getElementById(canvasId)?.getContext('2d');
  if (!ctx) return;

  const physByWt = new Map(phys.map(s => [s.worldTime, s]));
  const cycByWt  = new Map(cyc.map(c => [c.worldTime, c]));
  const wts = [...new Set([...physByWt.keys(), ...cycByWt.keys()])].sort();
  const merged = wts.map(wt => {
    const s = physByWt.get(wt), c = cycByWt.get(wt);
    return {
      worldTime:   wt,
      testosterone:s?.testosterone ?? null,
      libido:      c ? c.libidoMod : null,
      pain:        c ? c.symptomPain : null,
      bloat:       c ? c.symptomBloat : null,
      phase:       c ? c.phase : null,
    };
  });
  const ds = downsamplePeaks(merged);
  const labels = ds.map(d => d.worldTime);
  const line = (label, key, color, extra = {}) => ({
    label, data: ds.map(d => d[key] ?? null),
    borderColor: color, backgroundColor: color.replace('1)', '0.08)'),
    borderWidth: 2, pointRadius: 0, tension: 0.3, spanGaps: true, ...extra,
  });

  const datasets = [];
  if (hasTesto) datasets.push(line('Testosterone', 'testosterone', 'hsla(210,75%,50%,1)'));
  if (hasCycle) {
    datasets.push(line('Libido (×)',   'libido', 'hsla(330,80%,55%,1)', { yAxisID: 'yLibido' }));
    datasets.push(line('Cramp / pain', 'pain',   'hsla(0,75%,50%,1)'));
    datasets.push(line('Bloat',        'bloat',  'hsla(40,80%,50%,1)'));
  }

  _charts[canvasId] = new Chart(ctx, {
    type: 'line',
    data: { labels, datasets },
    // Cycle phase background bands (Menses red · Follicular neutral · Ovulation green · Luteal amber).
    plugins: hasCycle ? [phaseBandPlugin(ds.map(d => d.phase))] : [],
    options: {
      responsive: true,
      plugins: { ...themedPlugins(), zoom: ZOOM_OPTIONS },
      scales: {
        y:       { ...themedScale(), min: 0, max: 100, position: 'left',
                   title: { display: true, text: '0 – 100', color: chartTheme().tick } },
        yLibido: { ...themedScale({ grid: { drawOnChartArea: false } }), min: 0, max: 2, position: 'right',
                   title: { display: true, text: 'Libido ×', color: chartTheme().tick } },
      },
    },
  });
}

/** Estradiol / Progesterone hormone-level chart (EventId 5600) — per-tick snapshot. */
export function renderHormoneLevelsChart(canvasId, hormoneSnaps, markers = []) {
  const snaps = hormoneSnaps ?? [];
  if (!snaps.length) {
    showNoData(canvasId, 'No hormone-level data (EventId 5600 not in logs).');
    return;
  }
  ensureCanvasVisible(canvasId);
  destroyChart(canvasId);
  const ctx = document.getElementById(canvasId)?.getContext('2d');
  if (!ctx) return;

  const ds = downsamplePeaks(snaps);
  const labels = ds.map(d => d.worldTime);
  const line = (label, key, color) => ({
    label, data: ds.map(d => d[key] ?? null),
    borderColor: color, backgroundColor: color.replace('1)', '0.08)'),
    borderWidth: 2, pointRadius: 0, tension: 0.3, spanGaps: true,
  });

  const datasets = [
    line('Estradiol',    'estradiol',    'hsla(330,80%,55%,1)'),
    line('Progesterone', 'progesterone', 'hsla(260,65%,55%,1)'),
  ];

  _charts[canvasId] = new Chart(ctx, {
    type: 'line',
    data: { labels, datasets },
    plugins: [eventMarkerPlugin(markers)],
    options: {
      responsive: true,
      plugins: { ...themedPlugins(), zoom: ZOOM_OPTIONS },
      scales: {
        y: { ...themedScale(), min: 0, max: 100,
             title: { display: true, text: '0 – 100', color: chartTheme().tick } },
      },
    },
  });
}

/** Emotion timeline — colored bar per snapshot showing dominant emotion. */
export function renderEmotionTimelineChart(canvasId, data) {
  if (!data?.length) { showNoData(canvasId, 'No emotion data.'); return; }
  ensureCanvasVisible(canvasId);
  destroyChart(canvasId);
  const ctx = document.getElementById(canvasId)?.getContext('2d');
  if (!ctx) return;

  const EMOTION_COLORS = {
    Neutral:'#adb5bd', Joy:'#ffc107', Sadness:'#6f9fd8', Anger:'#dc3545',
    Fear:'#fd7e14', Disgust:'#6c757d', Surprise:'#0dcaf0',
    Tenderness:'#e83e8c', Pride:'#6610f2', Shame:'#495057',
  };
  // Categorical ribbon (colour = emotion) — peak-preserving LTTB is meaningless on
  // categories, so uniform decimation is the right tool here.
  const ds = downsample(data, 1000);
  const labels   = ds.map(d => d.worldTime);
  const colors   = ds.map(d => EMOTION_COLORS[d.emotion] ?? '#adb5bd');
  const emotions = ds.map(() => 1); // fixed height

  _charts[canvasId] = new Chart(ctx, {
    type: 'bar',
    data: { labels, datasets: [{ data: emotions, backgroundColor: colors, borderWidth: 0, barPercentage: 1.0, categoryPercentage: 1.0 }] },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { display: false },
        tooltip: { callbacks: {
          title: ctx => ctx[0].label,
          label: ctx => ds[ctx.dataIndex]?.emotion ?? '',
        }},
        zoom: { pan: { enabled: true, mode: 'x' }, zoom: { wheel: { enabled: true, modifierKey: 'ctrl' }, mode: 'x' } },
      },
      scales: {
        x: { display: false },
        y: { display: false, min: 0, max: 1 },
      },
    },
  });
}

/**
 * Goal salience/progress chart.
 * timelines: { [kind]: [{worldTime, salience, progress}] }
 */
export function renderGoalsChart(canvasId, timelines) {
  const kinds = Object.keys(timelines);
  if (!kinds.length) { showNoData(canvasId, 'No goal events found (EventId 1300-1302 not in logs).'); return; }
  ensureCanvasVisible(canvasId);
  destroyChart(canvasId);
  const ctx = document.getElementById(canvasId)?.getContext('2d');
  if (!ctx) return;

  const GOAL_COLORS = [
    'hsla(330,80%,55%,1)', 'hsla(200,80%,50%,1)', 'hsla(120,65%,40%,1)',
    'hsla(40,90%,50%,1)',  'hsla(270,65%,55%,1)', 'hsla(0,75%,50%,1)',
    'hsla(160,65%,45%,1)', 'hsla(50,85%,50%,1)',  'hsla(280,60%,52%,1)',
    'hsla(15,80%,52%,1)',
  ];

  const datasets = [];
  kinds.forEach((kind, i) => {
    const pts    = timelines[kind];
    const color  = GOAL_COLORS[i % GOAL_COLORS.length];
    const labels_i = pts.map(p => p.worldTime);
    // Salience line (solid)
    datasets.push({
      label: `${kind} — salience`,
      data: pts.map(p => ({ x: p.worldTime, y: p.salience })),
      borderColor: color, backgroundColor: color.replace('1)', '0.06)'),
      borderWidth: 2, pointRadius: 0, tension: 0.3, spanGaps: true,
    });
    // Progress line (dashed)
    datasets.push({
      label: `${kind} — progress`,
      data: pts.map(p => ({ x: p.worldTime, y: p.progress })),
      borderColor: color, backgroundColor: 'transparent',
      borderDash: [5, 4], borderWidth: 1.5, pointRadius: 0, tension: 0.3, spanGaps: true,
    });
  });

  // Collect all worldTime labels across all series, sort, deduplicate
  const allTimes = [...new Set(kinds.flatMap(k => timelines[k].map(p => p.worldTime)))].sort();

  _charts[canvasId] = new Chart(ctx, {
    type: 'line',
    data: { labels: allTimes, datasets },
    options: {
      responsive: true,
      plugins: {
        legend: { position: 'bottom', labels: { color: chartTheme().legend, filter: item => !item.text.includes('— progress') || item.datasetIndex < 4 } },
        zoom: ZOOM_OPTIONS,
      },
      scales: { y: { ...themedScale(), min: 0, max: 1, title: { display: true, text: 'Salience / Progress (0–1)', color: chartTheme().tick } } },
    },
  });
}

export function renderBehaviorChart(canvasId, data) {
  if (!data?.length) { showNoData(canvasId, 'No behavior snapshots found (EventId 5200 not in logs).'); return; }
  ensureCanvasVisible(canvasId);
  destroyChart(canvasId);
  const ctx = document.getElementById(canvasId)?.getContext('2d');
  if (!ctx) return;
  const ds = downsamplePeaks(data);
  const labels = ds.map(d => d.worldTime);
  const cols = ['hsla(120,70%,40%,1)','hsla(30,80%,50%,1)','hsla(200,80%,50%,1)',
                 'hsla(270,70%,50%,1)','hsla(0,80%,50%,1)','hsla(45,90%,50%,1)'];
  const keys = ['rest','food','water','belonging','competence','intimacy'];
  const labels2 = ['Rest','Food','Water','Belonging','Competence','Intimacy'];
  const datasets = keys.map((k, i) => ({
    label: labels2[i], data: ds.map(d => d[k]),
    borderColor: cols[i], backgroundColor: cols[i].replace('1)','0.1)'),
    borderWidth: 1.5, pointRadius: 0, tension: 0.3
  }));
  _charts[canvasId] = new Chart(ctx, {
    type: 'line',
    data: { labels, datasets },
    options: {
      responsive: true,
      plugins: { ...themedPlugins(), zoom: ZOOM_OPTIONS },
      scales: { y: { ...themedScale(), min: 0, max: 1 } },
    },
  });
}

// Per-dimension styling, shared by the relationship small-multiples.
const REL_DIMS = {
  like:                { label: 'Like',                 color: 'hsla(120,70%,40%,1)', dash: [] },
  trust:               { label: 'Trust',                color: 'hsla(200,80%,50%,1)', dash: [] },
  closeness:           { label: 'Closeness',            color: 'hsla(270,70%,50%,1)', dash: [] },
  comfort:             { label: 'Comfort',              color: 'hsla(30,80%,50%,1)',  dash: [] },
  respect:             { label: 'Respect',              color: 'hsla(0,80%,50%,1)',   dash: [] },
  familiarity:         { label: 'Familiarity',          color: 'hsla(50,75%,50%,1)',  dash: [] },
  intimateAffinity:    { label: 'Intimate Affinity',    color: 'hsla(330,85%,55%,1)', dash: [] },
  sexualInterest:      { label: 'Sexual Interest',      color: 'hsla(350,80%,60%,1)', dash: [6,3] },
  aestheticAttraction: { label: 'Aesthetic Attraction', color: 'hsla(160,65%,45%,1)', dash: [] },
  physicalAttraction:  { label: 'Physical Attraction',  color: 'hsla(280,65%,55%,1)', dash: [6,3] },
  communalStrength:    { label: 'Communal',             color: 'hsla(140,55%,45%,1)', dash: [] },
  exchangeStrength:    { label: 'Exchange',             color: 'hsla(190,60%,45%,1)', dash: [] },
  transgressionResidue:{ label: 'Grudge (residue)',     color: 'hsla(0,70%,45%,1)',   dash: [8,3] },
  responsiveDesire:    { label: 'Responsive Desire',    color: 'hsla(320,55%,55%,1)', dash: [6,3] },
  perceivedDominance:  { label: 'Dominance',            color: 'hsla(15,75%,45%,1)',  dash: [4,4] },
  perceivedPrestige:   { label: 'Prestige',             color: 'hsla(45,85%,45%,1)',  dash: [4,4] },
};

// Thematic clusters → one small chart each (vs. 16 lines crammed on one canvas).
const REL_GROUPS = [
  { title: '🤝 Core bond',                 keys: ['like', 'trust', 'closeness', 'comfort', 'respect'] },
  { title: '💞 Attraction & intimacy',     keys: ['familiarity', 'intimateAffinity', 'sexualInterest', 'aestheticAttraction', 'physicalAttraction'] },
  { title: '⚖️ Type · repair · standing',  keys: ['communalStrength', 'exchangeStrength', 'transgressionResidue', 'responsiveDesire', 'perceivedDominance', 'perceivedPrestige'] },
];

/**
 * Relationship timeline as SMALL MULTIPLES: instead of 16 overlapping lines on one
 * canvas, render one compact chart per thematic cluster into `containerId` (a div).
 * Each sub-chart keeps zoom + the shared crosshair/brush. Dimensions never triggered
 * this run are noted per group rather than drawn as flat lines.
 */
export function renderRelationshipChart(containerId, timelineData, partnerName) {
  const container = document.getElementById(containerId);
  if (!container) return;
  // tear down previously created sub-charts for this container
  for (const id of Object.keys(_charts)) if (id.startsWith(containerId + '_')) destroyChart(id);
  while (container.firstChild) container.removeChild(container.firstChild);
  container.classList.add('row', 'g-2');

  if (!timelineData?.length) {
    const msg = document.createElement('div');
    msg.className = 'alert alert-secondary small m-2';
    msg.textContent = partnerName
      ? `No relationship timeline data with ${partnerName}.`
      : 'No relationship data found (EventId 2005 not in logs).';
    container.appendChild(msg);
    return;
  }

  const ds = downsamplePeaks(timelineData);
  const labels = ds.map(d => d.worldTime);
  const theme = chartTheme();

  REL_GROUPS.forEach((grp, gi) => {
    const present = grp.keys.filter(k => ds.some(p => p[k] != null));
    const missing = grp.keys.filter(k => !present.includes(k)).map(k => REL_DIMS[k].label);

    const col  = document.createElement('div'); col.className = 'col-12 col-xl-6';
    const card = document.createElement('div'); card.className = 'border rounded p-2 h-100';
    const h    = document.createElement('div'); h.className = 'small fw-semibold text-muted mb-1';
    h.textContent = grp.title;
    card.appendChild(h);

    if (!present.length) {
      const none = document.createElement('div');
      none.className = 'text-muted small fst-italic';
      none.textContent = 'Not yet triggered this run.';
      card.appendChild(none); col.appendChild(card); container.appendChild(col);
      return;
    }

    const wrap = document.createElement('div'); wrap.style.height = '160px';
    const canvas = document.createElement('canvas');
    const cid = `${containerId}_${gi}`;
    canvas.id = cid;
    wrap.appendChild(canvas); card.appendChild(wrap); col.appendChild(card); container.appendChild(col);

    const datasets = present.map(k => {
      const d = REL_DIMS[k];
      return {
        label: d.label, data: ds.map(p => p[k] ?? null),
        borderColor: d.color, backgroundColor: d.color.replace('1)', '0.08)'),
        borderWidth: d.dash.length ? 1.5 : 2, borderDash: d.dash,
        pointRadius: 0, tension: 0.3, spanGaps: true,
      };
    });

    _charts[cid] = new Chart(canvas.getContext('2d'), {
      type: 'line',
      data: { labels, datasets },
      options: {
        responsive: true, maintainAspectRatio: false,
        plugins: {
          ...themedPlugins({
            legend:   { position: 'bottom', labels: { color: theme.legend, boxWidth: 10, font: { size: 9 }, padding: 6 } },
            subtitle: { display: !!missing.length, text: missing.length ? `Not triggered: ${missing.join(', ')}` : '', color: theme.subtitle, font: { size: 9 } },
          }),
          zoom: ZOOM_OPTIONS,
        },
        scales: {
          y: { ...themedScale(), min: 0, max: 100 },
          x: { ...themedScale({ ticks: { display: false } }) },
        },
      },
    });
  });
}

// ── Per-pair chart (specific subject→target) including Rusbult predictors ─────
// Single canvas, mirrors renderStressMoodChart. Used by the dedicated pair-selection
// view (two dropdowns: subject + target) — distinct from renderRelationshipChart's
// small-multiples view, which only ever shows the selected character's own partners.
export function renderRelationshipPairChart(canvasId, series, markers = []) {
  if (!series?.length) { showNoData(canvasId, 'Pro tuto dvojici nejsou žádná data hrany (EventId 2005).'); return; }
  ensureCanvasVisible(canvasId);
  destroyChart(canvasId);
  const ctx = document.getElementById(canvasId)?.getContext('2d');
  if (!ctx) return;

  const ds = downsamplePeaks(series);
  const labels = ds.map(d => d.worldTime);
  const line = (label, key, color, dash = []) => ({
    label, data: ds.map(d => d[key] ?? null),
    borderColor: color, backgroundColor: color.replace('1)', '0.08)'),
    borderWidth: dash.length ? 1.5 : 2, borderDash: dash,
    pointRadius: 0, tension: 0.3, spanGaps: true,
  });

  _charts[canvasId] = new Chart(ctx, {
    type: 'line',
    data: { labels, datasets: [
      line('Like',               'like',               'hsla(120,70%,40%,1)'),
      line('Trust',              'trust',              'hsla(200,80%,50%,1)'),
      line('Closeness',          'closeness',          'hsla(270,70%,50%,1)'),
      line('Commitment',         'commitment',         'hsla(210,90%,40%,1)', [0]),
      line('InvestmentSize',     'investmentSize',     'hsla(160,70%,40%,1)', [4,2]),
      line('AlternativeQuality', 'alternativeQuality', 'hsla(15,85%,50%,1)',  [4,2]),
      line('Grudge (residue)',   'transgressionResidue','hsla(0,75%,45%,1)', [8,3]),
    ]},
    plugins: markers.length ? [eventMarkerPlugin(markers)] : [],
    options: {
      responsive: true,
      plugins: { ...themedPlugins(), zoom: ZOOM_OPTIONS },
      scales: { y: { ...themedScale(), min: 0, max: 100, title: { display: true, text: '0 – 100', color: chartTheme().tick } } },
    },
  });
}
