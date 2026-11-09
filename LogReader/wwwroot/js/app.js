// public/js/app.js
import * as api from './api.js';
import * as charts from './charts.js';
import { state } from './core/state.js';
import { loadAllCharacters, loadCharacterList, setTabLoading } from './core/characters.js';
import { applyBrushFilter } from './core/dashboard.js';
import { showError } from './core/util.js';
import { loadBehavior } from './views/behavior.js';
import { loadGoals } from './views/goals.js';
import { loadInteractions } from './views/interactions.js';
import { loadLocations, loadMap } from './views/locations.js';
import { loadMemory } from './views/memory.js';
import { loadPhysiology } from './views/physio.js';
import { loadProfile } from './views/profile.js';
import { loadPsychology } from './views/psych.js';
import { loadRelationships } from './views/relationships.js';
import { loadStory } from './views/story.js';
import { loadTicks } from './views/ticks.js';

// ── Dark mode ─────────────────────────────────────────────────────────────────
export function applyTheme(dark) {
  document.documentElement.setAttribute('data-bs-theme', dark ? 'dark' : 'light');
  const btn = document.getElementById('darkModeToggle');
  if (btn) btn.textContent = dark ? '☀️' : '🌙';
}

export function setupDarkMode() {
  const saved = localStorage.getItem('theme');
  const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
  applyTheme(saved ? saved === 'dark' : prefersDark);

  document.getElementById('darkModeToggle')?.addEventListener('click', () => {
    const isDark = document.documentElement.getAttribute('data-bs-theme') === 'dark';
    applyTheme(!isDark);
    localStorage.setItem('theme', !isDark ? 'dark' : 'light');
    // Grafy mají hardcodované barvy os/mřížky — musí se překreslit s novým tématem
    if (state.currentCharId) {
      setTabLoading(true);
      loadCurrentTab().catch(() => {}).finally(() => setTabLoading(false));
    }
  });
}

// ── Startup ──────────────────────────────────────────────────────────────────
document.addEventListener('DOMContentLoaded', async () => {
  setupDarkMode();
  setupTabs();
  document.addEventListener('click', e => {
    const btn = e.target.closest('[data-reset-zoom]');
    if (btn) charts.resetZoom(btn.dataset.resetZoom);
  });

  // Reset / run-change handling: both paths flush the server caches and do a
  // full page reload so no client state (charts, state.npcMapCache…) survives a
  // log regeneration.
  const resetAndReload = async () => {
    try { await api.postReset(); } catch {}
    location.reload();
  };
  document.getElementById('btnResetData')?.addEventListener('click', resetAndReload);
  document.getElementById('btnReloadData')?.addEventListener('click', resetAndReload);
  document.addEventListener('run-stamp-changed', () => {
    document.getElementById('runChangedBanner')?.classList.remove('d-none');
  });

  // Time-range brushing: Shift+drag on a chart filters list rows to that window.
  document.addEventListener('brush-changed', applyBrushFilter);

  // Load calendar config + event registry (id→name), then characters
  state.worldCal = await api.getConfig().catch(() => state.worldCal);
  state.EVENT_ID_NAMES = await api.getEventRegistry()
    .then(r => r.idNames ?? {})
    .catch(() => ({}));
  await loadAllCharacters();
  await loadCharacterList();
});

// ── Tabs ─────────────────────────────────────────────────────────────────────
export function setupTabs() {
  // Sync initial active state with state.currentTab
  document.querySelectorAll('[data-tab]').forEach(l =>
    l.classList.toggle('active', l.dataset.tab === state.currentTab));
  document.querySelectorAll('[data-panel]').forEach(p =>
    p.classList.toggle('d-none', p.dataset.panel !== state.currentTab));

  document.querySelectorAll('[data-tab]').forEach(link => {
    link.addEventListener('click', async (e) => {
      e.preventDefault();
      state.currentTab = link.dataset.tab;
      document.querySelectorAll('[data-tab]').forEach(l => l.classList.remove('active'));
      link.classList.add('active');
      document.querySelectorAll('[data-panel]').forEach(p => {
        p.classList.toggle('d-none', p.dataset.panel !== state.currentTab);
      });
      if (state.currentCharId) {
        setTabLoading(true);
        try { await loadCurrentTab(); }
        catch (e) { showError(e.message); }
        finally { setTabLoading(false); }
      }
    });
  });
}

export async function loadCurrentTab() {
  switch (state.currentTab) {
    case 'profile':       return loadProfile();
    case 'physio':        return loadPhysiology();
    case 'psych':         return loadPsychology();
    case 'behavior':      return loadBehavior();
    case 'interactions':  return loadInteractions();
    case 'relations':     return loadRelationships();
    case 'story':         return loadStory();
    case 'ticks':         return loadTicks();
    case 'locations':     return loadLocations();
    case 'map':           return loadMap();
    case 'goals':         return loadGoals();
    case 'memory':        return loadMemory();
  }
}
