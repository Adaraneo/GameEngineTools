// public/js/api.js
const BASE = '/api';

// Server attaches X-Run-Stamp to every /api response; it changes when the logs
// were deleted and regenerated (new simulation run). When the page's data no
// longer matches the server's stamp, dispatch one event so the UI can offer a
// reload instead of silently mixing data from two different runs.
let knownRunStamp = null;
let runChangeNotified = false;

/**
 * Records an observed run-stamp; dispatches `run-stamp-changed` (once) when it
 * differs from the stamp this page's data was loaded under. Also exported for
 * SSE consumers — EventSource can't read response headers, so load-all sends
 * the stamp in its `start` event instead.
 */
export function noteRunStamp(stamp) {
  if (!stamp) return;
  if (knownRunStamp && stamp !== knownRunStamp && !runChangeNotified) {
    runChangeNotified = true;
    document.dispatchEvent(new CustomEvent('run-stamp-changed'));
  }
  knownRunStamp ??= stamp;
}

function watchRunStamp(res) {
  noteRunStamp(res.headers.get('X-Run-Stamp'));
}

async function get(url) {
  const res = await fetch(url);
  watchRunStamp(res);
  if (!res.ok) {
    const body = await res.json().catch(() => ({ error: res.statusText }));
    throw new Error(body.error ?? res.statusText);
  }
  return res.json();
}

/** Flush server caches + reload NPC map (after log regeneration). */
export async function postReset() {
  const res = await fetch(`${BASE}/reset`, { method: 'POST' });
  if (!res.ok) throw new Error('Reset failed: ' + res.statusText);
  return res.json();
}

function qs(params) {
  const p = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v != null && v !== '') p.set(k, v);
  }
  const s = p.toString();
  return s ? '?' + s : '';
}

export const getConfig        = ()  => get(`${BASE}/config`);
export const getEventRegistry = ()  => get(`${BASE}/event-registry`);
export const getStory      = (id) => get(`${BASE}/logs/${id}/story`);
export const getCharacters = () => get(`${BASE}/characters`);
export const getCharacter  = (id) => get(`${BASE}/characters/${id}`);
export const getSummary    = (id) => get(`${BASE}/logs/${id}/summary`);
export const getPhysiology = (id) => get(`${BASE}/logs/${id}/physiology`);
export const getMemory     = (id) => get(`${BASE}/logs/${id}/memory`);
export const getPsychology = (id) => get(`${BASE}/logs/${id}/psychology`);
export const getBehavior   = (id) => get(`${BASE}/logs/${id}/behavior`);

export const getEvents = (id, opts = {}) =>
  get(`${BASE}/logs/${id}/events${qs(opts)}`);

export const getRelationships = (id, otherId = null) =>
  get(`${BASE}/logs/${id}/relationships${qs({ otherId })}`);

export const getRelationshipPairs = (id) =>
  get(`${BASE}/logs/${id}/relationship-pairs`);

export const getRelationshipPair = (id, target) =>
  get(`${BASE}/logs/${id}/relationship-pair${qs({ target })}`);

export const getEventFrequency = (id, bucket = 'hour') =>
  get(`${BASE}/logs/${id}/event-frequency${qs({ bucket })}`);

export const getInteractions = (id, params = {}) =>
  get(`${BASE}/logs/${id}/interactions${qs(params)}`);

export const getGoals    = (id) => get(`${BASE}/logs/${id}/goals`);
export const getSchedule  = (id) => get(`${BASE}/logs/${id}/schedule`);
export const getLocations    = (id) => get(`${BASE}/logs/${id}/locations`);
export const getWorldRespawns = ()   => get(`${BASE}/world/respawns`);
export const getTicks         = (id) => get(`${BASE}/logs/${id}/ticks`);
export const getWorldMap      = ()   => get(`${BASE}/world/map`);
