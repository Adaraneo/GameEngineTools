// public/js/views/story.js
import * as api from '../api.js';
import { state } from '../core/state.js';
import { capitalise, fmtWt, mkDiv } from '../core/util.js';

export async function loadStory() {
  const data = await api.getStory(state.currentCharId);
  const events = data.events ?? [];
  const sub = document.getElementById('storySubtitle');
  if (sub) sub.textContent = `${events.length} key events · ${state.worldCal.calName}`;
  renderStoryList('storyList', events);

  const filterEl = document.getElementById('storyFilter');
  if (filterEl) {
    filterEl.onchange = () => {
      const f = filterEl.value;
      renderStoryList('storyList', f ? events.filter(e => e.subsystem === f) : events);
    };
  }
}

export function eventName(eid) {
  return state.EVENT_ID_NAMES[Number(eid)] ?? (eid != null ? `Event ${eid}` : '');
}

export const STORY_ICONS = { sleep:'😴', 'physio-action':'🍽️', 'behavior-action':'⚒️',
                      relationship:'❤️', goal:'🎯', memory:'🧠', repro:'🤰', psych:'👻' };

export function renderStoryList(containerId, events) {
  const el = document.getElementById(containerId);
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);
  if (!events.length) {
    const p = document.createElement('p');
    p.className = 'text-muted small fst-italic m-4 text-center';
    p.textContent = 'No story events found.'; el.appendChild(p); return;
  }
  for (const ev of events) {
    const row  = mkDiv(`story-entry sub-${ev.subsystem ?? 'other'}`);
    const time = mkDiv('story-time'); time.textContent = fmtWt(ev.worldTime, {short:true});
    const icon = mkDiv('story-icon'); icon.textContent = ev.icon ?? STORY_ICONS[ev.subsystem] ?? '▶️';
    const body = mkDiv('story-body');
    const strong = document.createElement('strong');
    const detail = mkDiv('story-detail');
    buildStoryText(ev, strong, detail);
    body.appendChild(strong);
    if (detail.textContent) body.appendChild(detail);
    row.appendChild(time); row.appendChild(icon); row.appendChild(body);
    el.appendChild(row);
  }
}

export function buildStoryText(ev, strong, detail) {
  const partnerName = pid => state.npcMapCache.get((pid ?? '').toLowerCase())?.displayName ?? (pid ?? '?').slice(0,8) + '…';
  switch (ev.subsystem) {
    case 'sleep':
      if (ev.eventId === 1101) strong.textContent = `Sleep started${ev.plannedHours != null ? ` (${ev.plannedHours.toFixed(1)}h planned)` : ''}`;
      else if (ev.eventId === 1102) strong.textContent = 'Shared sleep started';
      else if (ev.eventId === 1105) {
        strong.textContent = `Woke up — ${ev.hours?.toFixed(1)}h, quality ${Math.round(ev.quality ?? 0)}`;
        if (ev.interrupted) detail.textContent = '⚠️ interrupted';
      }
      else if (ev.eventId === 1106) { strong.textContent = 'Sleep interrupted'; detail.textContent = ev.cause ?? ''; }
      else if (ev.eventId === 1108) { strong.textContent = 'Nightmare'; detail.textContent = `stress at sleep: ${ev.stressAtSleep?.toFixed(0) ?? '?'}`; }
      else strong.textContent = ev.label ?? '';
      break;
    case 'physio-action':
    case 'behavior-action':
      strong.textContent = ev.label ?? ev.action ?? '?';
      if (ev.utility != null) detail.textContent = `utility ${ev.utility.toFixed(2)}${ev.duration ? ' · ' + ev.duration : ''}`;
      break;
    case 'relationship': {
      const pn = partnerName(ev.partnerId);
      if (ev.kind === 'event') {
        strong.textContent = `${ev.act ?? ev.eventType ?? 'Interaction'} with ${pn}`;
        detail.textContent = ev.outcome === 'accepted' ? '✅ accepted' : '❌ rejected';
      } else if (ev.kind === 'first_impression') {
        strong.textContent = `First impression: ${pn}`;
      } else if (ev.kind === 'apply' && ev.changes) {
        const sorted = Object.entries(ev.changes)
          .filter(([,v]) => v?.from != null)
          .sort((a,b) => Math.abs(b[1].to - b[1].from) - Math.abs(a[1].to - a[1].from));
        if (sorted.length) {
          const [dim, v] = sorted[0];
          const delta = v.to - v.from;
          strong.textContent = `Relationship changed: ${pn}`;
          detail.textContent = `${capitalise(dim)}: ${v.from.toFixed(1)} → ${v.to.toFixed(1)} (${delta >= 0 ? '+' : ''}${delta.toFixed(1)})`;
        } else strong.textContent = `Relationship update: ${pn}`;
      } else { strong.textContent = `${ev.kind ?? 'rel event'}: ${pn}`; }
      break;
    }
    case 'goal':
      if (ev.type === 'activated' || ev.type === 'seeded') {
        strong.textContent = `Goal activated: ${ev.label ?? ev.kind}`;
        detail.textContent = `${ev.origin ?? ''} · salience ${ev.salience?.toFixed(2) ?? '?'}`;
      } else if (ev.type === 'resolved') {
        strong.textContent = `Goal resolved: ${ev.label ?? ev.kind}`;
        detail.textContent = `${ev.resolutionIcon ?? ''} ${ev.resolution} · progress ${((ev.progress ?? 0) * 100).toFixed(0)}%`;
      } else { strong.textContent = `Goal: ${ev.label ?? ev.kind}`; }
      break;
    case 'memory':
      strong.textContent = `Memory encoded: ${ev.tag ?? '?'}`;
      detail.textContent = `salience ${ev.salience?.toFixed(2)} · ${ev.icon ?? ''} ${ev.emotion ?? ''}`;
      break;
    case 'repro':
      strong.textContent = ev.label ?? 'Reproductive event';
      if (ev.result) detail.textContent = ev.result;
      break;
    default:
      strong.textContent = ev.label ?? (ev.message ?? '').slice(0, 80);
  }
}

// ── Behavior: action sessions ─────────────────────────────────────────────────
