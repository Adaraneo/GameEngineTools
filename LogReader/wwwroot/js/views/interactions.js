// public/js/views/interactions.js
import * as api from '../api.js';
import { state } from '../core/state.js';
import { applyBrushFilter, markBrushable } from '../core/dashboard.js';
import { capitalise, fmtWt } from '../core/util.js';

export const SPEECH_ACT_ICONS = {
  SmallTalk:'💬', Question:'❓', SelfDisclosure:'💭', Validation:'👍',
  Boundary:'🚫', Humor:'😄', Meta:'🔍', Invite:'📩',
};

export const TOUCH_ICONS = { Light:'✋', Friendly:'🤝', Intimate:'💕' };

export async function loadInteractions() {
  const data = await api.getInteractions(state.currentCharId);
  // data = { interactions, byPartner, intentEvents, stats }

  renderIntStats(data.stats);
  populateIntFilters(data);
  renderIntList(data.interactions);
  renderDialogueList(data.utterances ?? []);

  // Filter button
  const filterBtn = document.getElementById('intFilterBtn');
  if (filterBtn) {
    // Clone to remove old listeners
    const newBtn = filterBtn.cloneNode(true);
    filterBtn.parentNode.replaceChild(newBtn, filterBtn);
    newBtn.addEventListener('click', () => {
      const partnerId = document.getElementById('intPartnerFilter')?.value ?? '';
      const act       = document.getElementById('intActFilter')?.value ?? '';
      const outcome   = document.getElementById('intOutcomeFilter')?.value ?? '';
      let filtered = data.interactions;
      if (partnerId) filtered = filtered.filter(i => i.partnerId === partnerId);
      if (act)       filtered = filtered.filter(i => (i.speechAct ?? i.type) === act);
      if (outcome)   filtered = filtered.filter(i => i.outcome === outcome);
      renderIntList(filtered);

      let filteredUtterances = data.utterances ?? [];
      if (partnerId) filteredUtterances = filteredUtterances.filter(u => u.partnerId === partnerId);
      renderDialogueList(filteredUtterances);
    });
  }

  // Clear button
  const clearBtn = document.getElementById('intClearBtn');
  if (clearBtn) {
    const newClear = clearBtn.cloneNode(true);
    clearBtn.parentNode.replaceChild(newClear, clearBtn);
    newClear.addEventListener('click', () => {
      const ps = document.getElementById('intPartnerFilter');
      const as = document.getElementById('intActFilter');
      const os = document.getElementById('intOutcomeFilter');
      if (ps) ps.value = '';
      if (as) as.value = '';
      if (os) os.value = '';
      renderIntList(data.interactions);
      renderDialogueList(data.utterances ?? []);
    });
  }
}

export function renderIntStats(stats) {
  const row = document.getElementById('intStatsRow');
  if (!row) return;
  while (row.firstChild) row.removeChild(row.firstChild);
  if (!stats) return;

  const acceptPct = stats.total > 0 ? Math.round(stats.accepted / stats.total * 100) : 0;

  const cards = [
    { label: 'Total interactions', value: stats.total.toLocaleString(), cls: 'primary' },
    { label: 'Accepted',           value: `${acceptPct}%`,              cls: acceptPct >= 60 ? 'success' : 'warning' },
    { label: 'Rejected',           value: String(stats.total - stats.accepted), cls: 'secondary' },
  ];
  for (const c of cards) {
    const col  = document.createElement('div'); col.className = 'col-md-2';
    const card = document.createElement('div'); card.className = 'card text-center';
    const body = document.createElement('div'); body.className = 'card-body py-2';
    const lbl  = document.createElement('div'); lbl.className = 'text-muted small'; lbl.textContent = c.label;
    const val  = document.createElement('div'); val.className = `fs-4 fw-bold text-${c.cls}`; val.textContent = c.value;
    body.appendChild(lbl); body.appendChild(val); card.appendChild(body); col.appendChild(card);
    row.appendChild(col);
  }

  // Top speech acts (up to 4)
  if (stats.bySpeechAct) {
    const sorted = Object.entries(stats.bySpeechAct)
      .sort((a, b) => b[1].total - a[1].total).slice(0, 4);
    for (const [act, s] of sorted) {
      const pct  = Math.round(s.accepted / s.total * 100);
      const col  = document.createElement('div'); col.className = 'col-md-2';
      const card = document.createElement('div'); card.className = 'card text-center';
      const body = document.createElement('div'); body.className = 'card-body py-2';
      const icon = SPEECH_ACT_ICONS[act] ?? '▶';
      const lbl  = document.createElement('div'); lbl.className = 'text-muted small'; lbl.textContent = `${icon} ${act}`;
      const vEl  = document.createElement('div'); vEl.className = `fw-bold ${pct >= 60 ? 'text-success' : 'text-warning'}`;
      vEl.textContent = `${s.total}x  ${pct}%`;
      body.appendChild(lbl); body.appendChild(vEl); card.appendChild(body); col.appendChild(card);
      row.appendChild(col);
    }
  }
}

export function populateIntFilters(data) {
  const partnerSel = document.getElementById('intPartnerFilter');
  const actSel     = document.getElementById('intActFilter');
  if (!partnerSel || !actSel) return;

  while (partnerSel.options.length > 1) partnerSel.remove(1);
  const partners = [...new Set(data.interactions.map(i => i.partnerId))];
  for (const pid of partners) {
    const npc = state.npcMapCache.get(pid);
    const opt = document.createElement('option'); opt.value = pid;
    opt.textContent = npc?.displayName ?? pid.slice(0, 8) + '...';
    partnerSel.appendChild(opt);
  }

  while (actSel.options.length > 1) actSel.remove(1);
  const acts = [...new Set(data.interactions.map(i => i.speechAct ?? i.type).filter(Boolean))];
  for (const act of acts) {
    const opt = document.createElement('option'); opt.value = act;
    opt.textContent = `${SPEECH_ACT_ICONS[act] ?? '▶'} ${act}`;
    actSel.appendChild(opt);
  }
}

export function renderIntList(interactions) {
  const el = document.getElementById('intList');
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);

  if (!interactions.length) {
    const p = document.createElement('p');
    p.className = 'text-muted small fst-italic m-3';
    p.textContent = 'No interactions found (EventId 1201/1202 or 2001 not in logs).';
    el.appendChild(p);
    return;
  }

  const table = document.createElement('table');
  table.className = 'table table-sm table-hover mb-0 small';

  const thead = document.createElement('thead'); thead.className = 'table-light';
  const hr = document.createElement('tr');
  ['Time', 'Partner', 'Type', 'Role', 'P%', 'Outcome', 'Key Change', 'Context'].forEach(h => {
    const th = document.createElement('th'); th.textContent = h; hr.appendChild(th);
  });
  thead.appendChild(hr); table.appendChild(thead);

  const tbody = document.createElement('tbody');
  for (const int of interactions) {
    const tr = document.createElement('tr');
    if (int.outcome === 'accepted')      tr.className = 'table-success';
    else if (int.outcome === 'rejected') tr.className = 'table-danger';

    const npc = state.npcMapCache.get(int.partnerId);
    const partnerName = npc?.displayName ?? (int.partnerId ? int.partnerId.slice(0, 8) + '...' : '?');

    // Type icon + label
    const typeIcon = int.type === 'touch'
      ? (TOUCH_ICONS[int.touchLevel] ?? '🤝')
      : int.type === 'sexual' ? '💕'
      : (SPEECH_ACT_ICONS[int.speechAct] ?? '💬');
    const typeLabel = int.speechAct ?? (int.touchLevel ? `Touch (${int.touchLevel})` : int.type ?? '?');

    // Key change (biggest absolute delta)
    let keyChange = '—';
    if (int.changes) {
      const entries = Object.entries(int.changes)
        .filter(([, v]) => typeof v === 'object' && v !== null && 'from' in v)
        .map(([k, v]) => ({ dim: k, delta: v.to - v.from }))
        .sort((a, b) => Math.abs(b.delta) - Math.abs(a.delta));
      if (entries.length) {
        const top  = entries[0];
        const sign = top.delta >= 0 ? '+' : '';
        keyChange  = `${capitalise(top.dim)} ${sign}${top.delta.toFixed(1)}`;
      }
    }

    // Context
    const ctxText = int.context
      ? `${int.context.location ?? ''} N:${int.context.noise?.toFixed(1) ?? '?'} C:${int.context.crowding?.toFixed(1) ?? '?'}`
      : '—';

    const cells = [
      fmtWt(int.worldTime),
      partnerName,
      `${typeIcon} ${typeLabel}`,
      null, // role badge — built separately
      int.probability != null ? `${Math.round(int.probability * 100)}%` : '—',
      int.outcome === 'accepted' ? 'accepted' : int.outcome === 'rejected' ? 'rejected' : '—',
      keyChange,
      ctxText,
    ];

    cells.forEach((text, i) => {
      const td = document.createElement('td');
      if (i === 3) {
        // Role badge
        if (int.role) {
          const rb = document.createElement('span');
          rb.className = `badge text-bg-${int.role === 'initiator' ? 'primary' : 'secondary'}`;
          rb.textContent = int.role;
          td.appendChild(rb);
        } else {
          td.textContent = '—';
        }
      } else if (i === 5) {
        // Outcome badge
        if (int.outcome === 'accepted') {
          const ob = document.createElement('span'); ob.className = 'badge text-bg-success'; ob.textContent = 'accepted'; td.appendChild(ob);
        } else if (int.outcome === 'rejected') {
          const ob = document.createElement('span'); ob.className = 'badge text-bg-danger'; ob.textContent = 'rejected'; td.appendChild(ob);
        } else {
          td.textContent = '—';
        }
      } else {
        td.textContent = text;
      }
      if (i === 2 || i === 7) td.className = 'text-muted small';
      tr.appendChild(td);
    });
    tbody.appendChild(tr);
  }
  table.appendChild(tbody);
  el.appendChild(table);
}

// ── Dialogue feed (EventId 1220 — uttered SpeechActs) ─────────────────────────
// TEMPORARY gloss preview (see TemporaryCzechActRealizer) — never simulation state.
export function renderDialogueList(utterances) {
  const el = document.getElementById('dialogueList');
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);

  if (!utterances.length) {
    const p = document.createElement('p');
    p.className = 'text-muted small fst-italic m-3';
    p.textContent = 'No utterances found (EventId 1220 not in logs).';
    el.appendChild(p);
    return;
  }

  const table = document.createElement('table');
  table.className = 'table table-sm table-hover mb-0 small';

  const thead = document.createElement('thead'); thead.className = 'table-light';
  const hr = document.createElement('tr');
  ['Time', 'Utterance', 'Turn', 'Kind', 'Predicate', 'Register', 'Directness'].forEach(h => {
    const th = document.createElement('th'); th.textContent = h; hr.appendChild(th);
  });
  thead.appendChild(hr); table.appendChild(thead);

  const tbody = document.createElement('tbody');
  for (const u of utterances) {
    const tr = document.createElement('tr');
    markBrushable(tr, u.worldTime);

    const npc = state.npcMapCache.get(u.partnerId);
    const speakerName   = u.speakerName ?? '?';
    const addresseeName = u.addresseeName ?? npc?.displayName ?? (u.partnerId ? u.partnerId.slice(0, 8) + '...' : '?');

    // An owed second-pair-part reads differently from an opening move — mark it in the feed.
    const cells = [
      fmtWt(u.worldTime),
      `${u.isResponse ? '↩️' : '🗨️'} ${speakerName} → ${addresseeName}: „${u.utterance}“`,
      u.isResponse ? `odpověď na ${u.respondingToKind ?? '?'}` : 'zahájení',
      u.relationalKind ?? '—',
      u.predicateLemma || '—',
      u.register ?? '—',
      u.directness ?? '—',
    ];
    cells.forEach((text, i) => {
      const td = document.createElement('td');
      td.textContent = text;
      if (i === 1) td.className = 'fst-italic';
      if (i >= 2) td.className = (td.className ? td.className + ' ' : '') + 'text-muted small';
      tr.appendChild(td);
    });
    tbody.appendChild(tr);
  }
  table.appendChild(tbody);
  el.appendChild(table);
  applyBrushFilter();   // honour an active brush for freshly rendered rows
}
