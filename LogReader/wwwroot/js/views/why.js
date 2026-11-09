// public/js/views/why.js
import { state } from '../core/state.js';
import { mkDiv } from '../core/util.js';
import { eventName } from './story.js';

// ── Why panel — causal inference over the tick-store ─────────────────────────────
// A "driver" is the need/state field whose pressure explains an action.
// Keyed by affordance domain so it works for both plain actions (Eat) and the
// affordance-driven object actions (UseObject:Rest) + ObjectUsed events.
export const DOMAIN_DRIVERS = {
  Hunger:        { src:'physio',   field:'hunger',     label:'Hunger',         high:true },
  Thirst:        { src:'physio',   field:'thirst',     label:'Thirst',         high:true },
  Pain:          { src:'physio',   field:'pain',       label:'Pain',           high:true },
  Rest:          { src:'behavior', field:'rest',       label:'NeedRest',       high:true },
  Social:        { src:'behavior', field:'belonging',  label:'NeedBelonging',  high:true },
  Intimacy:      { src:'behavior', field:'intimacy',   label:'NeedIntimacy',   high:true },
  Work:          { src:'behavior', field:'competence', label:'NeedCompetence', high:true },
  Entertainment: { src:'behavior', field:'competence', label:'NeedCompetence', high:true },
};

// Plain action name → affordance domain(s).
export const ACTION_DOMAINS = {
  Eat:'Hunger', Drink:'Thirst', SelfCare:'Pain', Sleep:'Rest', MoveToRest:'Rest',
  ReachOut:'Social', InviteIntimacy:'Intimacy', Work:'Work', Create:'Entertainment',
};

/** Set of affordance domains an event touches (from action name or ObjectUsed affordances). */
export function eventDomains(ev) {
  const out = new Set();
  if (ev.detail?.affordances) for (const a of String(ev.detail.affordances).split('+')) out.add(a);
  const action = ev.detail?.action ?? null;
  if (action) {
    if (action.includes(':')) out.add(action.split(':')[1]);   // UseObject:Rest, MoveTo:Social…
    else if (ACTION_DOMAINS[action]) out.add(ACTION_DOMAINS[action]);
  }
  return out;
}

/** Driver descriptors for an event (deduped by field). */
export function driversFor(ev) {
  const seen = new Set(), out = [];
  for (const dom of eventDomains(ev)) {
    const d = DOMAIN_DRIVERS[dom];
    if (d && !seen.has(d.field)) { seen.add(d.field); out.push(d); }
  }
  return out;
}

// Number formatter: 2 decimals for small magnitudes (valence, salience), else rounded.
export function whyNum(v) {
  if (v == null) return '?';
  return Math.abs(v) < 10 ? (+v).toFixed(2) : String(Math.round(v));
}

export const whySigned = v => (v >= 0 ? '+' : '') + whyNum(v);

export const whyName   = id => state.npcMapCache.get(String(id ?? '').toLowerCase())?.displayName ?? String(id ?? '?').slice(0, 8);

/**
 * Self-explanation from an event's own payload — most psychology / relationship /
 * memory / goal / values events log the mechanism that caused them, so we surface
 * those fields directly rather than inferring across events.
 */
export function explainEvent(ev) {
  const d = ev.detail ?? {};
  const causes = [], effects = [];
  switch (ev.eventId) {
    // ── Psychology ──────────────────────────────────────────────────────────
    case 5103: // EmotionTransition
      causes.push({ icon:'🧠', text:`PAD state: valence ${whyNum(d.valence)}, arousal ${whyNum(d.arousal)}, stress ${whyNum(d.stress)}` });
      effects.push({ icon:'😶', text:`Dominant emotion ${d.fromEmotion} → ${d.toEmotion}` });
      break;
    case 5104: // MoodBaselineShifted
      effects.push({ icon: d.delta >= 0 ? '😊' : '😞', text:`MoodBaseline ${whyNum(d.oldBaseline)} → ${whyNum(d.newBaseline)} (Δ${whySigned(d.delta)})` });
      break;
    case 5105: // AllostaticLoadMilestone
      causes.push({ icon:'📈', text:`Allostatic load crossed ${whyNum(d.threshold)}` });
      effects.push({ icon:'💧', text:`Load ${whyNum(d.oldLoad)} → ${whyNum(d.newLoad)}, cortisol ${whyNum(d.cortisol)}` });
      break;
    case 5101: // Nightmare effect
      causes.push({ icon:'👻', text:'Nightmare during sleep' });
      effects.push({ icon:'📈', text:`Stress +${whyNum(d.stressSpike)}, valence −${whyNum(d.valencePenalty)}` });
      break;
    case 5102: // Poor sleep → stress
      causes.push({ icon:'😪', text:`Poor sleep (quality ${whyNum(d.sleepQuality)})` });
      effects.push({ icon:'📈', text:`Stress +${whyNum(d.stressDelta)}` });
      break;
    // ── Memory ──────────────────────────────────────────────────────────────
    case 3000: // Encoded
      causes.push({ icon:'❗', text:`Salient experience (salience ${whyNum(d.salience)}, ${d.emotion})` });
      effects.push({ icon:'💭', text:`Encoded memory "${d.tag}"` });
      break;
    case 3002: // Reconsolidated
      effects.push({ icon:'🔄', text:`"${d.tag}" drifted ${d.fromEmotion} → ${d.toEmotion}` });
      break;
    case 3003: // Belief updated
      effects.push({ icon:'🧩', text:`Belief ${d.beliefKind} ${whyNum(d.oldStrength)} → ${whyNum(d.newStrength)} (evidence ${d.evidenceCount})` });
      break;
    // ── Goals ───────────────────────────────────────────────────────────────
    case 1300: // Activated
      causes.push({ icon:'🌱', text:`Origin: ${d.origin}` });
      effects.push({ icon:'🎯', text:`Goal "${d.kind}" active (salience ${whyNum(d.salience)})` });
      break;
    case 1301: // Progressed
      effects.push({ icon:'📈', text:`"${d.kind}" progress ${whyNum(d.progressOld)} → ${whyNum(d.progressNew)}` });
      break;
    case 1302: // Resolved
      effects.push({ icon: d.resolutionIcon ?? '🎯', text:`"${d.kind}" ${d.resolution} (progress ${whyNum(d.progress)}, frustration ${whyNum(d.frustration)})` });
      break;
    // ── Values ──────────────────────────────────────────────────────────────
    case 1211: // Congruence violation
      effects.push({ icon:'⚖️', text:`Congruence ${whyNum(d.congruence)}, utility Δ${whySigned(d.utilityDelta)}` });
      break;
    case 1212: // Guilt spike
      causes.push({ icon:'⚖️', text:`Violated value ${d.violatedValue}` });
      effects.push({ icon:'😔', text:`Valence Δ${whySigned(d.deltaValence)}` });
      break;
    // ── Relationships ─────────────────────────────────────────────────────────
    case 2010: // Milestone
      effects.push({ icon:'🏆', text:`${d.dimension} crossed ${whyNum(d.threshold)} (${whyNum(d.oldValue)} → ${whyNum(d.newValue)})` });
      break;
    case 2011: // Third-party reputation
      causes.push({ icon:'👁️', text:'Observed a third party act' });
      effects.push({ icon:'🔁', text:`${d.dimension} ${whyNum(d.oldValue)} → ${whyNum(d.newValue)} (weight ${whyNum(d.weight)})` });
      break;
    case 2012: // Jealousy
      if (d.intimateActor)
        causes.push({ icon:'👀', text:`Observed ${whyName(d.intimateActor)} with ${whyName(d.intimateTarget)}` });
      effects.push({ icon:'😤', text:`Transgression residue ${whyNum(d.oldResidue)} → ${whyNum(d.newResidue)}` });
      break;
    case 2008: // Sexual encounter outcome
      effects.push({ icon:'💕', text:`${d.outcome}: intimacy ${whyNum(d.intimateAffinity)}, closeness ${whyNum(d.closeness)}` });
      break;
    case 2007: // First impression
      effects.push({ icon:'👋', text:`First impression: Like ${whyNum(d.like)}` });
      break;
    // ── Sleep ───────────────────────────────────────────────────────────────
    case 1105: // Woke up
      effects.push({ icon:'🌅', text:`Slept ${whyNum(d.hours)}h, quality ${whyNum(d.quality)}${d.interrupted ? ' (interrupted)' : ''}` });
      break;
    case 1106: // Interrupted
      causes.push({ icon:'🚨', text:`Interrupted by ${d.cause}${d.phase ? ' in ' + d.phase : ''}` });
      break;
    // ── Behavior decision ─────────────────────────────────────────────────────
    case 1001: // Decision
      causes.push({ icon:'⚖️', text:`Won arbitration with utility ${whyNum(d.utility)}` });
      break;
    // ── Death ─────────────────────────────────────────────────────────────────
    case 5007:
      causes.push({ icon:'💀', text:`Cause: ${d.cause} (age ${d.age})` });
      break;
  }
  // Relationship apply/event: explicit per-dimension changes.
  if (d.changes && typeof d.changes === 'object') {
    for (const [dim, ch] of Object.entries(d.changes)) {
      if (ch && ch.from != null && ch.to != null)
        effects.push({ icon:'❤️', text:`${dim} ${whyNum(ch.from)} → ${whyNum(ch.to)}` });
    }
  }
  // Relationship interaction received: the act + outcome that drove a change.
  if (d.kind === 'event' && (d.act || d.outcome))
    causes.push({ icon:'💬', text:`Interaction ${d.act ?? ''}${d.outcome ? ' → ' + d.outcome : ''}` });

  return { causes, effects };
}

/** Build {causes, effects} for an event at the current tick by inference. */
export function computeCausality(ev) {
  const ticks = state.tickData?.ticks ?? [];
  const tick  = ticks[state.tickIndex];
  if (!tick) return { causes: [], effects: [] };
  const next  = ticks[state.tickIndex + 1] ?? null;
  const snaps = tick.snapshots ?? {};
  const causes = [], effects = [];

  // Self-explanation from the event's own payload (covers psychology / relationship /
  // memory / goal / values / sleep events that log their own mechanism).
  const self = explainEvent(ev);
  causes.push(...self.causes);
  effects.push(...self.effects);

  const isObject  = ev.eventId === 1500;
  const action    = ev.detail?.action ?? null;
  const drivers   = driversFor(ev);
  const domains   = eventDomains(ev);
  const snapVal = (src, field) => {
    const s = src === 'physio' ? snaps.physio : src === 'behavior' ? snaps.behavior : snaps.psych;
    return s ? s[field] : null;
  };
  const sharesDomain = other => {
    const od = eventDomains(other);
    for (const d of od) if (domains.has(d)) return true;
    return false;
  };

  // ── Causes ──────────────────────────────────────────────────────────────────
  // 1) The need/state pressure behind it.
  for (const d of drivers) {
    const v = snapVal(d.src, d.field);
    if (v == null) continue;
    const max = d.max ?? 100;
    const pressing = d.high ? (v / max) >= 0.5 : (v / max) <= 0.5;
    causes.push({ icon: pressing ? '🔴' : '⚪',
      text: `${d.label} was ${Math.round(v)} ${pressing ? '(pressing)' : '(not pressing)'}` });
  }
  // 2) For an ObjectUsed, the committed action that drove it (same domain).
  if (isObject) {
    for (const other of tick.events) {
      if (other === ev) continue;
      if ((other.subsystem === 'behavior' || other.subsystem === 'physio') &&
          other.detail?.action && sharesDomain(other))
        causes.push({ icon:'⚙️', text:`Committed action ${other.detail.action}` });
    }
  }
  // 3) Same-tick goal bias targeting this action.
  if (action) {
    for (const other of tick.events) {
      if (other === ev) continue;
      if (other.subsystem === 'goal' && other.detail?.type === 'bias' && other.detail.action === action) {
        const b = other.detail.bias;
        causes.push({ icon:'🎯', text:`Goal "${other.detail.kind}" biased ${action} (+${typeof b === 'number' ? b.toFixed(2) : b})` });
      }
    }
  }

  // ── Effects ──────────────────────────────────────────────────────────────────
  // 1) For an action, the same-tick object it used — matched by affordance domain
  //    (never object→object, never an unrelated object).
  if (!isObject && domains.size) {
    for (const other of tick.events) {
      if (other === ev || other.eventId !== 1500) continue;
      if (sharesDomain(other))
        effects.push({ icon:'📦',
          text:`Used ${other.detail?.displayName ?? other.detail?.objectId} (${other.detail?.affordances ?? ''})${other.detail?.consumed ? ' — consumed' : ''}` });
    }
  }
  // 2) Next-tick movement of the driver need.
  if (next) {
    for (const d of drivers) {
      const before = snapVal(d.src, d.field);
      const afterS = d.src === 'physio' ? next.snapshots?.physio : d.src === 'behavior' ? next.snapshots?.behavior : next.snapshots?.psych;
      const after  = afterS ? afterS[d.field] : null;
      if (before == null || after == null) continue;
      const delta = after - before;
      if (Math.abs(delta) >= 1)
        effects.push({ icon: delta < 0 ? '✅' : '⤴️',
          text:`${d.label} ${Math.round(before)} → ${Math.round(after)} (${delta > 0 ? '+' : ''}${Math.round(delta)})` });
    }
  }
  // 3) Stressors → next-tick stress shift.
  const isStressor = [1207,1208,5101].includes(ev.eventId)
    || (ev.subsystem === 'relationship' && ev.detail?.outcome === 'rejected');
  if (isStressor && next?.snapshots?.psych && snaps.psych) {
    const ds = (next.snapshots.psych.stress ?? 0) - (snaps.psych.stress ?? 0);
    if (Math.abs(ds) >= 1)
      effects.push({ icon: ds > 0 ? '📈' : '📉',
        text:`Stress ${Math.round(snaps.psych.stress)} → ${Math.round(next.snapshots.psych.stress)} (${ds > 0 ? '+' : ''}${Math.round(ds)})` });
  }

  // Dedup by text (the same logical action appears as behavior + physio rows).
  const dedup = arr => { const s = new Set(); return arr.filter(x => !s.has(x.text) && s.add(x.text)); };
  return { causes: dedup(causes), effects: dedup(effects) };
}

let _whyModal = null;

export function openWhyPanel(ev) {
  document.getElementById('whyTitle').textContent =
    `❓ Why: ${ev.icon ?? ''} ${ev.label || eventName(ev.eventId)}`;
  const { causes, effects } = computeCausality(ev);

  const fill = (containerId, items, emptyMsg) => {
    const el = document.getElementById(containerId);
    while (el.firstChild) el.removeChild(el.firstChild);
    if (!items.length) {
      const p = document.createElement('p');
      p.className = 'text-muted small fst-italic mb-0';
      p.textContent = emptyMsg; el.appendChild(p); return;
    }
    for (const it of items) {
      const row = mkDiv('d-flex align-items-start gap-2 mb-1');
      const ic  = document.createElement('span'); ic.textContent = it.icon ?? '•';
      const tx  = document.createElement('span'); tx.className = 'small'; tx.textContent = it.text;
      row.appendChild(ic); row.appendChild(tx); el.appendChild(row);
    }
  };
  fill('whyCauses',  causes,  'No upstream cause inferred for this event.');
  fill('whyEffects', effects, 'No downstream effect inferred (try the next tick).');

  _whyModal ??= new bootstrap.Modal(document.getElementById('whyModal'));
  _whyModal.show();
}
