// public/js/core/util.js


/**
 * Format a WorldTimeText string using the game calendar.
 * "0100-01-01T05:30:00" → "Y100 M1 D1  05:30"
 */
export function fmtWt(wt, opts = {}) {
  if (!wt) return '—';
  const [datePart, timePart] = wt.split('T');
  if (!datePart) return wt;
  const parts = datePart.split('-').map(Number);
  const y = parts[0] ?? 0, m = parts[1] ?? 1, d = parts[2] ?? 1;
  const time = timePart?.slice(0, 5) ?? '';
  if (opts.timeOnly)  return time;
  if (opts.dateOnly)  return `Y${y} M${m} D${d}`;
  if (opts.short)     return `M${m} D${d}  ${time}`;
  return `Y${y} M${m} D${d}  ${time}`;
}

// ── DOM helpers ───────────────────────────────────────────────────────────────
export function mkDiv(className) {
  const el = document.createElement('div');
  if (className) el.className = className;
  return el;
}

export function makeCard(title, colClass) {
  const col  = mkDiv(colClass);
  const card = mkDiv('card profile-card');
  const hdr  = mkDiv('card-header fw-semibold small'); hdr.textContent = title;
  const body = mkDiv('card-body');
  card.appendChild(hdr); card.appendChild(body); col.appendChild(card);
  return col;
}

export function sectionTitle(text) {
  const el = mkDiv('profile-section-title'); el.textContent = text; return el;
}

export function appendStat(parent, label, value) {
  const row = mkDiv('d-flex justify-content-between align-items-baseline mb-1');
  const lbl = document.createElement('span'); lbl.className = 'text-muted small me-2'; lbl.textContent = label;
  const val = document.createElement('span'); val.className = 'small fw-semibold text-end'; val.textContent = value;
  row.appendChild(lbl); row.appendChild(val); parent.appendChild(row);
}

export function statPill(value, label) {
  const pill = mkDiv('stat-pill');
  const vEl  = mkDiv('stat-value'); vEl.textContent = value;
  const lEl  = mkDiv('stat-label'); lEl.textContent = label;
  pill.appendChild(vEl); pill.appendChild(lEl);
  return pill;
}

export function noDataEl(msg) {
  const el = mkDiv('text-muted small fst-italic'); el.textContent = msg; return el;
}

// Approximate hex values for appearance enum labels
export function skinToneHex(t) {
  return { VeryFair:'#fde8d8',Fair:'#f5cba7',Light:'#e8b88a',LightMedium:'#d4956a',
           Medium:'#c07850',Tan:'#a86040',Dark:'#7a4030',VeryDark:'#4a2010',Olive:'#b08060' }[t] ?? null;
}

export function eyeColorHex(t) {
  return { Brown:'#6b3a2a',Hazel:'#8b6914',Green:'#4a7c4e',Blue:'#4a78a0',Gray:'#8090a0',Amber:'#c08030' }[t] ?? null;
}

export function hairColorHex(t) {
  return { Black:'#1a1008',DarkBrown:'#3b1f0e',Brown:'#6b3a1e',Auburn:'#8b3a1a',
           Red:'#b03010',Blond:'#d4a830',DarkBlond:'#a07820' }[t] ?? null;
}

export function capitalise(s) { return s.charAt(0).toUpperCase() + s.slice(1); }

export function showError(msg) {
  console.error(msg);
}
