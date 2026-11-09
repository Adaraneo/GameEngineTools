// public/js/core/characters.js
import * as api from '../api.js';
import { state } from './state.js';
import { loadCurrentTab } from '../app.js';
import { computeHeaderTrends, renderHeaderSnapshot } from './dashboard.js';
import { fmtWt, showError } from './util.js';

export async function loadCharacterList() {
  try {
    const chars = await api.getCharacters();

    // Pre-populate state.npcMapCache with basic info for ALL characters so that
    // partner names in relationship views resolve correctly without a
    // separate fetch per partner. Full profile (personality, appearance…)
    // is loaded on top of this when the character is explicitly selected.
    for (const c of chars) {
      if (!state.npcMapCache.has(c.id.toLowerCase())) {
        state.npcMapCache.set(c.id.toLowerCase(), c);
      }
    }

    renderCharacterList(chars);
    for (const c of chars) setSidebarLoadState(c.id, 'ready');

    // Load death state for all characters asynchronously (non-blocking)
    loadDeathStatesForAllCharacters(chars).catch(() => {});

    if (chars.length > 0) await selectCharacter(chars[0].id);
  } catch (e) {
    showError('Failed to load characters: ' + e.message);
  }
}

export function renderCharacterList(chars) {
  const ul = document.getElementById('characterList');
  while (ul.firstChild) ul.removeChild(ul.firstChild);
  for (const c of chars) {
    const li = document.createElement('li');
    li.className = 'list-group-item list-group-item-action py-2 character-item';
    li.dataset.id = c.id;
    li.dataset.loadState = 'idle';

    // Row: dot + name (+ optional badge)
    const nameRow = document.createElement('div');
    nameRow.className = 'd-flex align-items-start gap-2';

    const dot = document.createElement('span');
    dot.className = 'load-dot mt-1';

    const nameDiv = document.createElement('div');
    nameDiv.className = 'fw-semibold';
    nameDiv.textContent = c.displayName;
    if (!c.hasNpcData) {
      const badge = document.createElement('span');
      badge.className = 'badge bg-secondary ms-1';
      badge.textContent = 'log only';
      nameDiv.appendChild(badge);
    }
    nameRow.appendChild(dot);
    nameRow.appendChild(nameDiv);
    li.appendChild(nameRow);

    if (c.biology) {
      const bioDiv = document.createElement('div');
      bioDiv.className = 'text-muted small ms-3';
      bioDiv.textContent = c.biology;
      li.appendChild(bioDiv);
    }

    li.addEventListener('click', () => selectCharacter(c.id));
    ul.appendChild(li);
  }

  // Search filtering — `oninput` assignment (not addEventListener) so repeated
  // renders don't stack duplicate handlers.
  const search = document.getElementById('searchInput');
  if (search) search.oninput = e => {
    const q = e.target.value.toLowerCase();
    ul.querySelectorAll('.character-item').forEach(el => {
      el.style.display = el.textContent.toLowerCase().includes(q) ? '' : 'none';
    });
  };
}

/** Update tab health indicator dots from the character's summary data. */
export async function updateTabDots(charId) {
  const summary = await api.getSummary(charId).catch(() => null);
  if (!summary) return;
  const byEid = summary.byEventId ?? {};
  const has = (...eids) => eids.some(e => (byEid[e] ?? 0) > 0);
  const setDot = (tab, hasData) => {
    const el = document.getElementById('dot-' + tab);
    if (el) { el.classList.toggle('has-data', hasData); el.classList.toggle('no-data', !hasData); }
  };
  setDot('profile',      true); // always available
  setDot('story',        Object.values(byEid).some(v => v > 0));
  setDot('ticks',        Object.values(byEid).some(v => v > 0));
  setDot('physio',       has(5000, 1100, 1101, 1105, 1003));
  setDot('psych',        has(5100, 5101, 5102));
  setDot('behavior',     has(5200, 1003, 1011, 1001));
  setDot('interactions', has(1201, 1202, 2001));
  setDot('locations',    has(1200, 1205, 1206, 1401));
  setDot('map',          has(1200, 1206));
  setDot('goals',        has(1300, 1302));
  setDot('relations',    has(2005, 2002, 2001));
  setDot('memory',       has(3000, 3001));
}

/** Set the load-state dot for a character in the sidebar. */
export function setSidebarLoadState(id, state) {
  const li = document.querySelector(`.character-item[data-id="${id}"]`);
  if (li) li.dataset.loadState = state;
}

export async function selectCharacter(id) {
  if (state.isLoading) return;        // block during initial load-all
  if (state.currentCharId === id) return;
  state.currentCharId = id;
  state.relData = null;

  document.querySelectorAll('.character-item').forEach(el => {
    el.classList.toggle('active', el.dataset.id === id);
  });

  // Profile card — instant (NPC map lookup)
  try {
    const profile = await api.getCharacter(id);
    state.npcMapCache.set(id.toLowerCase(), profile);
    renderProfileCard(profile);
    document.getElementById('profileCard')?.classList.remove('d-none');
    // Progressive enhancement: header pills render instantly without arrows above,
    // then re-render with recent-trend arrows once the cached series is read.
    computeHeaderTrends(id)
      .then(tr => { if (state.currentCharId === id) renderHeaderSnapshot(profile.snapshot ?? null, tr); })
      .catch(() => {});
  } catch (e) {
    showError('Failed to load profile: ' + e.message);
  }

  // Update tab health dots (async, non-blocking)
  updateTabDots(id).catch(() => {});

  // Tab data — from cache but may still take a moment to render charts/tables
  setTabLoading(true);
  try { await loadCurrentTab(); } catch (e) { showError(e.message); }
  finally { setTabLoading(false); }
}

// ── Global load-all via SSE ───────────────────────────────────────────────────
export function loadAllCharacters() {
  return new Promise(resolve => {
    state.isLoading = true;
    setUILocked(true);
    setProgressVisible(true);
    updateProgressUI({ stage: 'connect' });

    const es = new EventSource('/api/logs/load-all');

    es.addEventListener('start', ev => {
      const { totalChars, runStamp } = JSON.parse(ev.data);
      api.noteRunStamp(runStamp);   // EventSource can't read headers — stamp travels in-band
      updateProgressUI({ stage: 'start', totalChars });
    });

    es.addEventListener('global-start', ev => {
      const { fileName, sizeBytes } = JSON.parse(ev.data);
      updateProgressUI({ stage: 'global-file', fileName, sizeBytes });
    });
    es.addEventListener('global-progress', ev => {
      const { linesRead } = JSON.parse(ev.data);
      updateProgressUI({ stage: 'global-progress', linesRead });
    });
    es.addEventListener('global-done', () => {});

    es.addEventListener('char-start', ev => {
      const d = JSON.parse(ev.data);
      updateProgressUI({ stage: 'char-start', ...d });
    });

    es.addEventListener('char-file', ev => {
      const d = JSON.parse(ev.data);
      updateProgressUI({ stage: 'char-file', ...d });
    });

    es.addEventListener('char-progress', ev => {
      const d = JSON.parse(ev.data);
      updateProgressUI({ stage: 'char-progress', ...d });
    });

    es.addEventListener('char-done', ev => {
      const { personId } = JSON.parse(ev.data);
      setSidebarLoadState(personId, 'ready');
    });

    const finish = async (ev) => {
      es.close();
      if (ev?.data) {
        try {
          const d = JSON.parse(ev.data);
          if (d.message) { showError(d.message); }
        } catch {}
      }
      updateProgressUI({ stage: 'done' });
      await new Promise(r => setTimeout(r, 350));
      setProgressVisible(false);
      setUILocked(false);
      state.isLoading = false;
      resolve();
    };

    es.addEventListener('done',  finish);
    es.addEventListener('error', finish);
    es.onerror = () => finish();
  });
}

export function setProgressVisible(on) {
  const panel = document.getElementById('progressPanel');
  if (!panel) return;
  // Always hide every tab panel first
  document.querySelectorAll('[data-panel]').forEach(p => p.classList.add('d-none'));
  if (on) {
    panel.classList.remove('d-none');
  } else {
    panel.classList.add('d-none');
    // Restore only the currently active panel
    document.querySelector(`[data-panel="${state.currentTab}"]`)?.classList.remove('d-none');
  }
}

export function updateProgressUI({ stage, totalChars, charIndex, displayName,
                            totalFiles, fileIndex, fileName, sizeBytes, linesRead }) {
  const title   = document.getElementById('progressTitle');
  const bar     = document.getElementById('progressBar');
  const fileEl  = document.getElementById('progressFile');
  const countEl = document.getElementById('progressCount');
  const spinner = document.getElementById('progressSpinner');

  const setPct = pct => { if (bar) bar.style.width = Math.min(pct, 100) + '%'; };
  const sizeTxt = sizeBytes ? ` (${formatBytes(sizeBytes)})` : '';

  switch (stage) {
    case 'connect':
      if (title)   title.textContent   = 'Připojuji se…';
      setPct(0);
      if (fileEl)  fileEl.textContent  = '';
      if (countEl) countEl.textContent = '';
      break;

    case 'start':
      if (title) title.textContent = `Načítám logy — ${totalChars} postav…`;
      setPct(0);
      break;

    case 'global-file':
      if (title)  title.textContent  = 'Globální log…';
      if (fileEl) fileEl.textContent = `Characters.jsonl${sizeTxt}`;
      setPct(0);
      break;

    case 'global-progress':
      if (countEl) countEl.textContent = `${linesRead.toLocaleString()} řádků`;
      break;

    case 'char-start': {
      const pct = Math.round(((charIndex - 1) / (totalChars || 1)) * 100);
      setPct(pct);
      if (title)   title.textContent   = `Postava ${charIndex} / ${totalChars}`;
      if (fileEl)  fileEl.textContent  = displayName ?? '';
      if (countEl) countEl.textContent = '';
      break;
    }

    case 'char-file': {
      const pct = Math.round(((charIndex - 1) / (totalChars || 1)) * 100);
      setPct(pct);
      if (fileEl)  fileEl.textContent  = `${displayName ?? ''} → ${fileName}${sizeTxt}`;
      if (countEl) countEl.textContent = '';
      break;
    }

    case 'char-progress':
      if (countEl) countEl.textContent = `${linesRead.toLocaleString()} řádků`;
      break;

    case 'done':
      if (spinner) spinner.classList.add('d-none');
      if (bar) {
        bar.style.width = '100%';
        bar.classList.remove('progress-bar-striped', 'progress-bar-animated');
        bar.classList.add('bg-success');
      }
      if (title)   title.textContent   = '✓ Hotovo';
      if (fileEl)  fileEl.textContent  = '';
      if (countEl) countEl.textContent = '';
      break;
  }
}

export function formatBytes(bytes) {
  if (bytes < 1024)        return bytes + ' B';
  if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(0) + ' KB';
  return (bytes / 1024 / 1024).toFixed(1) + ' MB';
}

/** Lock/unlock all interactive elements during initial load-all. */
export function setUILocked(locked) {
  document.body.classList.toggle('ui-locked', locked);
}

/**
 * Show/hide the thin animated bar above tabs while a tab is rendering.
 * Also dims the tab content and disables clicks briefly.
 */
export function setTabLoading(on) {
  document.body.classList.toggle('tab-loading', on);
  document.getElementById('tabLoadingBar')?.classList.toggle('d-none', !on);
}

export function renderProfileCard(profile) {
  document.getElementById('profileName').textContent = profile.displayName ?? profile.id?.slice(0, 8) ?? '—';
  const meta = [profile.biology, profile.birthDateText].filter(Boolean).join(' · ');
  document.getElementById('profileMeta').textContent = meta;

  const barsEl = document.getElementById('personalityBars');
  while (barsEl.firstChild) barsEl.removeChild(barsEl.firstChild);
  if (!profile.personality) return;

  const traits = [
    { key: 'openness',          label: 'Openness' },
    { key: 'conscientiousness', label: 'Conscientiousness' },
    { key: 'extraversion',      label: 'Extraversion' },
    { key: 'agreeableness',     label: 'Agreeableness' },
    { key: 'neuroticism',       label: 'Neuroticism' },
  ];
  for (const t of traits) {
    const val = profile.personality[t.key] ?? 0;
    const pct = Math.round(val * 100);

    const row = document.createElement('div');
    row.className = 'trait-row d-flex align-items-center gap-1';

    const label = document.createElement('span');
    label.className = 'trait-label text-muted';
    label.textContent = t.label;

    const wrap = document.createElement('div');
    wrap.className = 'trait-bar-wrap';

    const prog = document.createElement('div');
    prog.className = 'progress';
    prog.style.height = '8px';

    const bar = document.createElement('div');
    bar.className = 'progress-bar';
    bar.style.width = pct + '%';
    bar.title = pct + '%';

    prog.appendChild(bar);
    wrap.appendChild(prog);
    row.appendChild(label);
    row.appendChild(wrap);
    barsEl.appendChild(row);
  }

  // Update header snapshot gauges / emotion badge
  renderHeaderSnapshot(profile.snapshot ?? null);
}

/** Load death states for all characters and update sidebar badges. */
export async function loadDeathStatesForAllCharacters(chars) {
  for (const c of chars) {
    try {
      const data = await api.getPhysiology(c.id);
      const deathEvents = (data.sleepEvents ?? []).filter(ev => ev.eventId === 5007);
      if (deathEvents.length > 0) {
        const deathEvent = deathEvents[deathEvents.length - 1];
        updateSidebarDeathBadge(c.id, deathEvent);
      }
    } catch (e) {
      // Silent fail - death state is not critical
    }
  }
}

/** Load death state asynchronously and display in profile header. */
export async function loadDeathState(charId) {
  try {
    const data = await api.getPhysiology(charId);
    const deathEvents = (data.sleepEvents ?? []).filter(ev => ev.eventId === 5007);
    if (deathEvents.length > 0) {
      const deathEvent = deathEvents[deathEvents.length - 1];
      updateProfileDeathBadge(deathEvent);
    } else {
      // Remove death badge if no death events
      clearProfileDeathBadge();
    }
  } catch (e) {
    // Silent fail - death state is not critical
  }
}

/** Update profile card header with death indicator badge. */
export function updateProfileDeathBadge(deathEvent) {
  const profileMeta = document.getElementById('profileMeta');
  if (!profileMeta) return;

  // Clear any existing death badge first
  clearProfileDeathBadge();

  const deathBadge = document.createElement('span');
  deathBadge.className = 'badge text-bg-danger ms-2 death-badge';
  deathBadge.textContent = `💀 Died of ${deathEvent.cause ?? 'unknown'}`;
  deathBadge.title = `Age ${deathEvent.age ?? '?'} · ${fmtWt(deathEvent.worldTime)}`;
  profileMeta.parentElement.appendChild(deathBadge);
}

/** Clear death badge from profile header. */
export function clearProfileDeathBadge() {
  const profileMeta = document.getElementById('profileMeta');
  if (!profileMeta) return;
  const existing = profileMeta.parentElement.querySelector('.death-badge');
  if (existing) existing.remove();
}

/** Update sidebar character list with death indicator badge. */
export function updateSidebarDeathBadge(charId, deathEvent) {
  const li = document.querySelector(`.character-item[data-id="${charId}"]`);
  if (!li) return;

  const nameDiv = li.querySelector('.fw-semibold');
  if (!nameDiv) return;

  // Remove any existing death badge first
  const existing = nameDiv.querySelector('.death-badge-sidebar');
  if (existing) existing.remove();

  // Create new death badge
  const deathBadge = document.createElement('span');
  deathBadge.className = 'badge text-bg-danger ms-1 death-badge-sidebar';
  deathBadge.textContent = `💀 ${deathEvent.cause ?? 'died'}`;
  deathBadge.title = `Age ${deathEvent.age ?? '?'}`;
  nameDiv.appendChild(deathBadge);
}
