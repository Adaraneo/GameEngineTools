// public/js/map.js — world map (SVG): locations at their authored X/Y + the selected
// character's visited-location trail. Pan (drag) + zoom (wheel), no external library.
//
// Data sources:
//   mapData — GET /api/world/map      → { locations: [{id, displayName, type, region, x, y}], connections: [{from, to, distanceMeters}] }
//   locData — GET /api/logs/:id/locations → { sessions: [{location, start, end, ...}] }
//
// A session's `location` is either a real location id (from EventId 1200, ContextChanged)
// or a LocationType category like "Social"/"Rest" (from EventId 1206, MoveTo target) —
// buildLocationSessions() merges both under the same field. Only sessions whose location
// matches a real map node id can be placed on the map; the rest are skipped here.

let _svg = null;
let _viewBox = null;
let _baseViewBox = null;

function themeColors() {
  const dark = document.documentElement.getAttribute('data-bs-theme') === 'dark';
  return {
    border: dark ? '#2c2c2c' : '#e4e4e4',
    edge:   dark ? '#4a4a4a' : '#b8b8b8',   // more visible than the generic UI border — this is map content, not chrome
    text:   dark ? '#9a9a9a' : '#6b6b6b',
    label:  dark ? '#d0d0d0' : '#3a3a3a',
    accent: dark ? '#6785e8' : '#3b5bdb',
    legendBg: dark ? 'rgba(20,20,20,0.85)' : 'rgba(255,255,255,0.85)',
  };
}

// Small fixed palette, cycled per distinct Region — consistent with the app's
// minimalist look (flat, no gradients).
const REGION_PALETTE = ['#3b5bdb', '#2f9e5c', '#b8860f', '#d1435b', '#2b7fb8', '#8b5cf6', '#c2410c', '#0891b2'];

// LocationDescriptor.Terrain → a glyph so each node reads as an actual place, not a
// data point. Falls back to a plain pin for anything unmapped.
const TERRAIN_ICONS = {
  Indoor: '🏠', Forest: '🌲', Water: '💧', Mountain: '⛰️', Courtyard: '⛲',
};
const terrainIcon = (terrain) => TERRAIN_ICONS[terrain] ?? '📍';

/** Andrew's monotone-chain convex hull. Returns points in CCW order. */
function convexHull(points) {
  if (points.length < 3) return points;
  const pts = [...points].sort((a, b) => a.x - b.x || a.y - b.y);
  const cross = (o, a, b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

  const lower = [];
  for (const p of pts) {
    while (lower.length >= 2 && cross(lower[lower.length - 2], lower[lower.length - 1], p) <= 0) lower.pop();
    lower.push(p);
  }
  const upper = [];
  for (let i = pts.length - 1; i >= 0; i--) {
    const p = pts[i];
    while (upper.length >= 2 && cross(upper[upper.length - 2], upper[upper.length - 1], p) <= 0) upper.pop();
    upper.push(p);
  }
  lower.pop(); upper.pop();
  return lower.concat(upper);
}

/**
 * Builds a soft, rounded "territory" blob path around a set of points: convex hull,
 * pushed outward from the centroid by `pad`, then connected with quadratic curves
 * instead of straight edges so corners look organic rather than geometric.
 */
function blobPath(points, pad) {
  if (points.length === 0) return null;
  if (points.length === 1) {
    const { x, y } = points[0];
    return `M ${x - pad} ${y} A ${pad} ${pad} 0 1 0 ${x + pad} ${y} A ${pad} ${pad} 0 1 0 ${x - pad} ${y} Z`;
  }

  const hull = convexHull(points);
  const cx = hull.reduce((s, p) => s + p.x, 0) / hull.length;
  const cy = hull.reduce((s, p) => s + p.y, 0) / hull.length;
  const expanded = hull.map(p => {
    const dx = p.x - cx, dy = p.y - cy;
    const len = Math.hypot(dx, dy) || 1;
    return { x: p.x + (dx / len) * pad, y: p.y + (dy / len) * pad };
  });

  if (expanded.length < 3) {
    // Two points (or a degenerate hull) — draw a capsule instead of a polygon.
    const [a, b] = expanded.length === 2 ? expanded : [expanded[0], expanded[0]];
    return `M ${a.x} ${a.y} L ${b.x} ${b.y}`; // stroked with a huge linecap by the caller
  }

  const mid = (p, q) => ({ x: (p.x + q.x) / 2, y: (p.y + q.y) / 2 });
  let d = `M ${mid(expanded[expanded.length - 1], expanded[0]).x} ${mid(expanded[expanded.length - 1], expanded[0]).y} `;
  for (let i = 0; i < expanded.length; i++) {
    const curr = expanded[i];
    const next = expanded[(i + 1) % expanded.length];
    const m = mid(curr, next);
    d += `Q ${curr.x} ${curr.y} ${m.x} ${m.y} `;
  }
  return d + 'Z';
}

function showEmpty(containerId, message) {
  const el = document.getElementById(containerId);
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);
  const div = document.createElement('div');
  div.className = 'text-muted text-center py-5';
  div.textContent = message;
  el.appendChild(div);
}

/**
 * Renders the world map into the element with id `containerId`.
 * Safe to call repeatedly (e.g. on tab switch or theme toggle) — clears and rebuilds.
 */
export function renderMap(containerId, mapData, locData) {
  const container = document.getElementById(containerId);
  if (!container) return;
  container.innerHTML = '';
  _svg = null; _viewBox = null; _baseViewBox = null;

  const locations = mapData?.locations ?? [];
  if (locations.length === 0) {
    showEmpty(containerId, mapData?.unavailable
      ? `Mapa není k dispozici (${mapData.unavailable}).`
      : 'Pro tento svět nejsou k dispozici žádné souřadnice lokací.');
    return;
  }

  const byId = new Map(locations.map(l => [l.id, l]));
  const regions = [...new Set(locations.map(l => l.region))];
  const regionIndex = new Map(regions.map((r, i) => [r, i]));
  const regionColor = (region) => REGION_PALETTE[(regionIndex.get(region) ?? 0) % REGION_PALETTE.length];

  const xs = locations.map(l => l.x), ys = locations.map(l => l.y);
  const minX = Math.min(...xs), maxX = Math.max(...xs);
  const minY = Math.min(...ys), maxY = Math.max(...ys);
  const spanX = maxX - minX, spanY = maxY - minY;
  const pad = Math.max(40, spanX * 0.08, spanY * 0.08);
  const vb = { x: minX - pad, y: minY - pad, w: (spanX || 200) + pad * 2, h: (spanY || 200) + pad * 2 };
  _baseViewBox = { ...vb };
  _viewBox = { ...vb };

  const colors = themeColors();
  const unit = Math.max(vb.w, vb.h);

  // Only sessions whose `location` is a real map node id become trail points —
  // MoveTo-category sessions ("Social"/"Rest"/…) are skipped, they aren't places.
  const trailIds = (locData?.sessions ?? [])
    .map(s => s.location)
    .filter(id => byId.has(id));
  const visited = new Set(trailIds);

  const svgNS = 'http://www.w3.org/2000/svg';
  const svg = document.createElementNS(svgNS, 'svg');
  svg.setAttribute('width', '100%');
  svg.setAttribute('height', '100%');
  svg.style.cursor = 'grab';
  svg.style.touchAction = 'none';
  _svg = svg;

  const applyViewBox = () => svg.setAttribute('viewBox', `${_viewBox.x} ${_viewBox.y} ${_viewBox.w} ${_viewBox.h}`);
  applyViewBox();

  // ── Region "territory" blobs — the thing that makes this read as a map ────
  // Drawn first, underneath everything else: a soft tinted shape behind each
  // region's cluster of locations, like a territory outline.
  const blobPad = unit * 0.05;
  const blobGroup = document.createElementNS(svgNS, 'g');
  for (const region of regions) {
    const pts = locations.filter(l => l.region === region).map(l => ({ x: l.x, y: l.y }));
    const d = blobPath(pts, blobPad);
    if (!d) continue;
    const path = document.createElementNS(svgNS, 'path');
    path.setAttribute('d', d);
    path.setAttribute('fill', regionColor(region));
    path.setAttribute('fill-opacity', '0.12');
    path.setAttribute('stroke', regionColor(region));
    path.setAttribute('stroke-opacity', '0.35');
    path.setAttribute('stroke-width', unit * 0.002);
    if (pts.length === 2) {
      // blobPath() returned a bare line segment for the 2-point case — thicken it
      // into a capsule via a huge round linecap instead of a filled polygon.
      path.setAttribute('fill', 'none');
      path.setAttribute('stroke', regionColor(region));
      path.setAttribute('stroke-opacity', '0.12');
      path.setAttribute('stroke-width', blobPad * 2);
      path.setAttribute('stroke-linecap', 'round');
    }
    blobGroup.appendChild(path);
  }
  svg.appendChild(blobGroup);

  // ── Connections ──────────────────────────────────────────────────────────
  const edgesGroup = document.createElementNS(svgNS, 'g');
  const seenEdge = new Set();
  const degree = new Map();
  for (const c of (mapData.connections ?? [])) {
    const key = [c.from, c.to].sort().join('|');
    if (seenEdge.has(key)) continue;
    seenEdge.add(key);
    const a = byId.get(c.from), b = byId.get(c.to);
    if (!a || !b) continue;
    degree.set(c.from, (degree.get(c.from) ?? 0) + 1);
    degree.set(c.to, (degree.get(c.to) ?? 0) + 1);
    const line = document.createElementNS(svgNS, 'line');
    line.setAttribute('x1', a.x); line.setAttribute('y1', a.y);
    line.setAttribute('x2', b.x); line.setAttribute('y2', b.y);
    line.setAttribute('stroke', colors.edge);
    line.setAttribute('stroke-width', unit * 0.0025);
    edgesGroup.appendChild(line);
  }
  svg.appendChild(edgesGroup);

  // ── Character trail (visiting order) ────────────────────────────────────
  if (trailIds.length > 1) {
    const pts = trailIds.map(id => byId.get(id));
    const poly = document.createElementNS(svgNS, 'polyline');
    poly.setAttribute('points', pts.map(p => `${p.x},${p.y}`).join(' '));
    poly.setAttribute('fill', 'none');
    poly.setAttribute('stroke', colors.accent);
    poly.setAttribute('stroke-width', unit * 0.004);
    poly.setAttribute('stroke-linejoin', 'round');
    poly.setAttribute('stroke-linecap', 'round');
    poly.setAttribute('opacity', '0.7');
    svg.appendChild(poly);
  }

  // ── Location nodes + labels ──────────────────────────────────────────────
  // Labelling every node gets unreadable on a large world (WorldObserver has ~60+
  // locations) — past a threshold, only label nodes that are actually meaningful:
  // ones the character visited, or well-connected hubs.
  const LABEL_ALL_THRESHOLD = 40;
  const hubCutoff = [...degree.values()].sort((a, b) => b - a)[Math.floor(locations.length * 0.15)] ?? 0;
  const shouldLabel = (loc) =>
    locations.length <= LABEL_ALL_THRESHOLD ||
    visited.has(loc.id) ||
    (degree.get(loc.id) ?? 0) > Math.max(hubCutoff, 3);

  const nodeR = unit * 0.01;
  const nodesGroup = document.createElementNS(svgNS, 'g');
  const labelsGroup = document.createElementNS(svgNS, 'g');
  const fontSize = unit * 0.016;
  const iconSize = unit * 0.022;
  const terrainsUsed = new Set();

  for (const loc of locations) {
    const wasVisited = visited.has(loc.id);
    const scale = wasVisited ? 1.35 : 1;
    terrainsUsed.add(loc.terrain);

    // Neutral backdrop disc — legibility against the region blob / edges underneath.
    // Region identity now lives in the blob behind it, not the node color.
    const backdrop = document.createElementNS(svgNS, 'circle');
    backdrop.setAttribute('cx', loc.x);
    backdrop.setAttribute('cy', loc.y);
    backdrop.setAttribute('r', nodeR * 1.6 * scale);
    backdrop.setAttribute('fill', colors.legendBg);
    backdrop.setAttribute('stroke', wasVisited ? colors.accent : colors.border);
    backdrop.setAttribute('stroke-width', nodeR * (wasVisited ? 0.3 : 0.15));
    nodesGroup.appendChild(backdrop);

    const icon = document.createElementNS(svgNS, 'text');
    icon.setAttribute('x', loc.x);
    icon.setAttribute('y', loc.y);
    icon.setAttribute('text-anchor', 'middle');
    icon.setAttribute('dominant-baseline', 'central');
    icon.setAttribute('font-size', iconSize * scale);
    icon.textContent = terrainIcon(loc.terrain);
    const title = document.createElementNS(svgNS, 'title');
    title.textContent = `${loc.displayName} — ${loc.region} (${loc.terrain})`;
    icon.appendChild(title);
    nodesGroup.appendChild(icon);

    if (shouldLabel(loc)) {
      const text = document.createElementNS(svgNS, 'text');
      text.setAttribute('x', loc.x + nodeR * 2.2);
      text.setAttribute('y', loc.y);
      text.setAttribute('dominant-baseline', 'middle');
      text.setAttribute('font-size', fontSize);
      text.setAttribute('font-weight', wasVisited ? '600' : '400');
      text.setAttribute('fill', colors.label);
      text.setAttribute('paint-order', 'stroke');
      text.setAttribute('stroke', colors.legendBg);
      text.setAttribute('stroke-width', fontSize * 0.25);
      text.textContent = loc.displayName;
      labelsGroup.appendChild(text);
    }
  }
  svg.appendChild(nodesGroup);
  svg.appendChild(labelsGroup);

  container.appendChild(svg);
  wirePanZoom(svg);
  renderLegend(container, regions, regionColor, colors, terrainsUsed);
}

/** Small always-visible overlay listing region colors + terrain icons, so the map is decodable. */
function renderLegend(container, regions, regionColor, colors, terrainsUsed) {
  container.style.position = container.style.position || 'relative';

  const legend = document.createElement('div');
  legend.style.cssText = `
    position: absolute; top: 10px; left: 10px; z-index: 2;
    background: ${colors.legendBg}; border-radius: 6px; padding: 6px 10px;
    font-size: 0.72rem; line-height: 1.6; pointer-events: none;
  `;

  for (const region of regions) {
    const row = document.createElement('div');
    row.style.cssText = 'display:flex; align-items:center; gap:6px;';
    const swatch = document.createElement('span');
    swatch.style.cssText = `display:inline-block; width:9px; height:9px; border-radius:50%; background:${regionColor(region)};`;
    const label = document.createElement('span');
    label.style.color = colors.text;
    label.textContent = region || '(bez regionu)';
    row.appendChild(swatch);
    row.appendChild(label);
    legend.appendChild(row);
  }

  if (terrainsUsed.size > 0) {
    const sep = document.createElement('div');
    sep.style.cssText = `border-top:1px solid ${colors.text}; opacity:0.3; margin:4px 0;`;
    legend.appendChild(sep);
    for (const terrain of terrainsUsed) {
      const row = document.createElement('div');
      row.style.cssText = 'display:flex; align-items:center; gap:6px;';
      const icon = document.createElement('span');
      icon.textContent = terrainIcon(terrain);
      const label = document.createElement('span');
      label.style.color = colors.text;
      label.textContent = terrain;
      row.appendChild(icon);
      row.appendChild(label);
      legend.appendChild(row);
    }
  }

  const hint = document.createElement('div');
  hint.style.cssText = `color:${colors.text}; margin-top:3px; opacity:0.85;`;
  hint.textContent = 'zvýrazněné = navštíveno';
  legend.appendChild(hint);

  container.appendChild(legend);
}

/** Drag-to-pan + wheel-to-zoom, operating directly on the SVG viewBox. */
function wirePanZoom(svg) {
  let dragStart = null;

  svg.addEventListener('pointerdown', (e) => {
    dragStart = { x: e.clientX, y: e.clientY, vb: { ..._viewBox } };
    svg.style.cursor = 'grabbing';
    svg.setPointerCapture(e.pointerId);
  });

  svg.addEventListener('pointermove', (e) => {
    if (!dragStart) return;
    const rect = svg.getBoundingClientRect();
    const scaleX = _viewBox.w / rect.width;
    const scaleY = _viewBox.h / rect.height;
    const dx = (e.clientX - dragStart.x) * scaleX;
    const dy = (e.clientY - dragStart.y) * scaleY;
    _viewBox = { ...dragStart.vb, x: dragStart.vb.x - dx, y: dragStart.vb.y - dy };
    svg.setAttribute('viewBox', `${_viewBox.x} ${_viewBox.y} ${_viewBox.w} ${_viewBox.h}`);
  });

  const endDrag = () => { dragStart = null; svg.style.cursor = 'grab'; };
  svg.addEventListener('pointerup', endDrag);
  svg.addEventListener('pointerleave', endDrag);

  svg.addEventListener('wheel', (e) => {
    e.preventDefault();
    const factor = e.deltaY > 0 ? 1.15 : 1 / 1.15;
    const rect = svg.getBoundingClientRect();
    const mx = _viewBox.x + ((e.clientX - rect.left) / rect.width) * _viewBox.w;
    const my = _viewBox.y + ((e.clientY - rect.top) / rect.height) * _viewBox.h;
    _viewBox = {
      x: mx - (mx - _viewBox.x) * factor,
      y: my - (my - _viewBox.y) * factor,
      w: _viewBox.w * factor,
      h: _viewBox.h * factor,
    };
    svg.setAttribute('viewBox', `${_viewBox.x} ${_viewBox.y} ${_viewBox.w} ${_viewBox.h}`);
  }, { passive: false });
}

/** Resets pan/zoom to the initial fit-to-content view. */
export function resetMapView() {
  if (!_svg || !_baseViewBox) return;
  _viewBox = { ..._baseViewBox };
  _svg.setAttribute('viewBox', `${_viewBox.x} ${_viewBox.y} ${_viewBox.w} ${_viewBox.h}`);
}
