// public/js/views/profile.js
import * as api from '../api.js';
import { state } from '../core/state.js';
import { buildSnapBar, buildSnapshotBanner, snapBarColor } from '../core/dashboard.js';
import { appendStat, eyeColorHex, fmtWt, hairColorHex, makeCard, mkDiv, noDataEl, sectionTitle, skinToneHex, statPill } from '../core/util.js';
import { buildRelationshipMatrix } from './relationships.js';

// ── Profile tab ───────────────────────────────────────────────────────────────
export async function loadProfile() {
  const profile = state.npcMapCache.get(state.currentCharId.toLowerCase());
  const el = document.getElementById('profileTabContent');
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);
  if (!profile?.hasNpcData) {
    const alert = document.createElement('div');
    alert.className = 'alert alert-secondary';
    alert.textContent = 'No NPC data file found for this character.';
    el.appendChild(alert);
    return;
  }
  el.appendChild(buildProfileHTML(profile));
  // Key moments — async, non-blocking
  renderKeyMoments('keyMomentsBody').then(() => {
    const card = document.getElementById('keyMomentsCard');
    if (card) card.style.display = '';
  }).catch(() => {});
}

export function buildProfileHTML(p) {
  const root = document.createElement('div');

  // ── Snapshot banner + insight cards (only when snapshot exists) ───────────
  if (p.snapshot) {
    root.appendChild(buildSnapshotBanner(p.snapshot));
    const insights = buildInsightCards(p);
    if (insights) root.appendChild(insights);
  }

  // ── Row 1: Identity + Personality [+ Current State] ──────────────────────
  const row1 = mkDiv('row g-3 mb-3');
  // Identity and Personality columns become col-lg-4 when snapshot exists (3-col)
  // Otherwise col-md-4 / col-md-8 as before (2-col)
  const identityColClass     = p.snapshot ? 'col-lg-4' : 'col-md-4';
  const personalityColClass  = p.snapshot ? 'col-lg-4' : 'col-md-8';

  // Identity card
  const idCard = makeCard('Identity', identityColClass);
  const idBody = idCard.querySelector('.card-body');
  appendStat(idBody, 'Full name', p.displayName ?? '—');
  if (p.surnames?.other) appendStat(idBody, 'Alt. surname', p.surnames.other);
  if (p.familiarNames?.length) appendStat(idBody, 'Nicknames', p.familiarNames.join(', '));
  idBody.appendChild(document.createElement('hr'));
  appendStat(idBody, 'Biology', p.biology ?? '—');
  appendStat(idBody, 'Born', p.birthDateText ?? '—');
  row1.appendChild(idCard);

  // Personality card
  const persCard = makeCard('Personality', personalityColClass);
  const persBody = persCard.querySelector('.card-body');
  const pers = p.personality;
  if (pers) {
    // Big Five
    const bigFive = [
      { key: 'openness',          label: 'Openness',          color: 'info'    },
      { key: 'conscientiousness', label: 'Conscientiousness', color: 'primary' },
      { key: 'extraversion',      label: 'Extraversion',      color: 'success' },
      { key: 'agreeableness',     label: 'Agreeableness',     color: 'warning' },
      { key: 'neuroticism',       label: 'Neuroticism',       color: 'danger'  },
    ];
    const bfSection = mkDiv('mb-3');
    bfSection.appendChild(sectionTitle('Big Five'));
    for (const t of bigFive) {
      const val = pers[t.key];
      if (val == null) continue;
      const pct = Math.round(val * 100);
      const row = mkDiv('trait-row d-flex align-items-center gap-2');
      const lbl = document.createElement('span');
      lbl.className = 'trait-label text-muted'; lbl.textContent = t.label;
      const wrap = mkDiv('trait-bar-wrap');
      const prog = mkDiv('progress'); prog.style.height = '8px';
      const bar  = mkDiv(`progress-bar bg-${t.color}`);
      bar.style.width = pct + '%'; bar.title = pct + '%';
      prog.appendChild(bar); wrap.appendChild(prog);
      row.appendChild(lbl); row.appendChild(wrap);
      bfSection.appendChild(row);
    }
    persBody.appendChild(bfSection);

    // Style badges
    const styleDefs = [
      { label: 'Attachment',    value: pers.attachment,    colors: { Secure: 'success', Anxious: 'warning', Avoidant: 'secondary', Disorganized: 'danger' } },
      { label: 'Chronotype',    value: pers.chronotype,    colors: { Lark: 'warning', Neutral: 'primary', Owl: 'dark' } },
      { label: 'Communication', value: pers.communication, colors: {} },
      { label: 'Sociosexuality',value: pers.sociosexuality,colors: { Restricted: 'primary', Intermediate: 'info', Unrestricted: 'danger' } },
    ].filter(s => s.value);
    if (styleDefs.length) {
      const stySection = mkDiv('mb-3');
      stySection.appendChild(sectionTitle('Style'));
      const badgeRow = mkDiv('d-flex flex-wrap gap-2');
      for (const s of styleDefs) {
        const color = s.colors[s.value] ?? 'secondary';
        const b = document.createElement('span');
        b.className = `badge text-bg-${color}`;
        b.textContent = `${s.label}: ${s.value}`;
        badgeRow.appendChild(b);
      }
      stySection.appendChild(badgeRow);
      persBody.appendChild(stySection);
    }

    // Motivations
    if (pers.motivation) {
      const motKeys = [
        { key: 'affiliation', label: 'Affiliation' }, { key: 'achievement', label: 'Achievement' },
        { key: 'power',       label: 'Power'       }, { key: 'altruism',    label: 'Altruism'    },
        { key: 'competence',  label: 'Competence'  }, { key: 'autonomy',    label: 'Autonomy'    },
        { key: 'curiosity',   label: 'Curiosity'   }, { key: 'rest',        label: 'Rest'        },
        { key: 'sexuality',   label: 'Sexuality'   },
      ];
      const motSection = mkDiv('');
      motSection.appendChild(sectionTitle('Motivations'));
      const motGrid = mkDiv('row g-0');
      for (const m of motKeys) {
        const val = pers.motivation[m.key];
        if (val == null) continue;
        const pct = Math.round(val * 100);
        const col  = mkDiv('col-6');
        const row  = mkDiv('mot-row d-flex align-items-center gap-1');
        const lbl  = document.createElement('span');
        lbl.className = 'mot-label'; lbl.textContent = m.label;
        const wrap = mkDiv('mot-bar-wrap');
        const prog = mkDiv('progress'); prog.style.height = '6px';
        const bar  = mkDiv('progress-bar bg-info');
        bar.style.width = pct + '%'; bar.title = pct + '%';
        prog.appendChild(bar); wrap.appendChild(prog);
        row.appendChild(lbl); row.appendChild(wrap);
        col.appendChild(row); motGrid.appendChild(col);
      }
      motSection.appendChild(motGrid);
      persBody.appendChild(motSection);
    }
  } else {
    persBody.appendChild(noDataEl('No personality data available.'));
  }
  row1.appendChild(persCard);
  // Current State card (third column, only with snapshot)
  if (p.snapshot) row1.appendChild(buildCurrentStateCard(p.snapshot));

  root.appendChild(row1);

  // ── Row 2: Appearance + Attraction ───────────────────────────────────────
  const row2 = mkDiv('row g-3');

  // Appearance card
  const appCard = makeCard('Physical Appearance', 'col-md-6');
  const appBody = appCard.querySelector('.card-body');
  const app = p.appearance;
  if (app) {
    // ── Stat pills: height + hair ───────────────────────────────────────────
    const pills = mkDiv('d-flex flex-wrap gap-2 mb-3');
    if (app.heightCm     != null) pills.appendChild(statPill(Math.round(app.heightCm) + ' cm', 'Height'));
    if (app.hairLengthCm != null) pills.appendChild(statPill(Math.round(app.hairLengthCm) + ' cm', 'Hair len.'));
    if (app.ageYears     != null) pills.appendChild(statPill(app.ageYears, 'Age'));
    if (pills.children.length) appBody.appendChild(pills);

    // ── Colors ───────────────────────────────────────────────────────────────
    const colorDefs = [
      { label: 'Skin', value: app.skinTone,  hex: skinToneHex(app.skinTone)  },
      { label: 'Eyes', value: app.eyeColor,  hex: eyeColorHex(app.eyeColor)  },
      { label: 'Hair', value: [app.hairColor, app.hairType].filter(Boolean).join(', '), hex: hairColorHex(app.hairColor) },
    ].filter(c => c.value);
    if (colorDefs.length) {
      const colSection = mkDiv('mb-2');
      colSection.appendChild(sectionTitle('Coloring'));
      for (const c of colorDefs) {
        const row = mkDiv('d-flex align-items-center mb-1');
        if (c.hex) { const dot = mkDiv('color-dot'); dot.style.backgroundColor = c.hex; row.appendChild(dot); }
        const txt = document.createElement('span'); txt.className = 'small'; txt.textContent = `${c.label}: ${c.value}`;
        row.appendChild(txt); colSection.appendChild(row);
      }
      appBody.appendChild(colSection);
    }

    // ── Body latent traits ───────────────────────────────────────────────────
    const bodyTraits = [
      { label: 'Muscularity',    key: 'muscularity',        max: 1 },
      { label: 'Adiposity',      key: 'adiposity',          max: 1 },
      { label: 'Build/Robustness',key:'robustness',         max: 1 },
      { label: 'Posture',        key: 'posture',            max: 1 },
      { label: 'Soft fullness',  key: 'softTissueFullness', max: 1 },
      { label: 'Lower mass',     key: 'lowerMass',          max: 1 },
    ].filter(t => app[t.key] != null);
    if (bodyTraits.length) {
      const sec = mkDiv('mb-2');
      sec.appendChild(sectionTitle('Body'));
      const grid = mkDiv('row g-0');
      for (const t of bodyTraits) {
        const pct = Math.min(100, Math.round(app[t.key] * 100));
        const col = mkDiv('col-6');
        col.appendChild(buildSnapBar(t.label, pct, 100, ''));
        grid.appendChild(col);
      }
      // Sex dimorphism (−1 to +1) shown separately
      if (app.sexDimorphism != null) {
        const sd = app.sexDimorphism;
        const row = mkDiv('snap-bar-row d-flex align-items-center gap-1 mt-1');
        const lbl = document.createElement('span'); lbl.className = 'snap-bar-label'; lbl.textContent = 'Sex dimorphism';
        const txt = document.createElement('span'); txt.className = 'text-muted small';
        txt.textContent = sd >= 0 ? `+${sd.toFixed(2)} (masculine)` : `${sd.toFixed(2)} (feminine)`;
        row.appendChild(lbl); row.appendChild(txt);
        sec.appendChild(grid); sec.appendChild(row);
      } else {
        sec.appendChild(grid);
      }
      appBody.appendChild(sec);
    }

    // ── Face latent traits ───────────────────────────────────────────────────
    const faceTraits = [
      { label: 'Softness',       key: 'faceSoftness'      },
      { label: 'Angularity',     key: 'faceAngularity'    },
      { label: 'Robustness',     key: 'faceRobustness'    },
      { label: 'Midface proj.',  key: 'midfaceProjection' },
      { label: 'Brow prom.',     key: 'browProminence'    },
      { label: 'Nose scale',     key: 'noseScale'         },
      { label: 'Eye scale',      key: 'eyeScale'          },
      { label: 'Lip fullness',   key: 'lipFullness'       },
    ].filter(t => app[t.key] != null);
    if (faceTraits.length) {
      const sec = mkDiv('mb-2');
      sec.appendChild(sectionTitle('Face'));
      const grid = mkDiv('row g-0');
      for (const t of faceTraits) {
        const pct = Math.min(100, Math.round(app[t.key] * 100));
        const col = mkDiv('col-6');
        col.appendChild(buildSnapBar(t.label, pct, 100, ''));
        grid.appendChild(col);
      }
      sec.appendChild(grid);
      appBody.appendChild(sec);
    }

    // ── Aging / hair state ───────────────────────────────────────────────────
    const agingTraits = [
      { label: 'Grey fraction', key: 'greyFraction' },
      { label: 'Hair density',  key: 'hairDensity'  },
      { label: 'Wrinkle score', key: 'wrinkleScore' },
    ].filter(t => app[t.key] != null && app[t.key] > 0);
    if (agingTraits.length) {
      const sec = mkDiv('mb-2');
      sec.appendChild(sectionTitle('Aging'));
      for (const t of agingTraits) {
        sec.appendChild(buildSnapBar(t.label, Math.round(app[t.key] * 100), 100, ''));
      }
      appBody.appendChild(sec);
    }

    // ── Distinctive marks ────────────────────────────────────────────────────
    if (app.distinctiveMarks?.length) {
      const mSection = mkDiv('');
      mSection.appendChild(sectionTitle('Distinctive marks'));
      const ul = document.createElement('ul'); ul.className = 'mb-0 ps-3 small';
      for (const m of app.distinctiveMarks) { const li = document.createElement('li'); li.textContent = m; ul.appendChild(li); }
      mSection.appendChild(ul); appBody.appendChild(mSection);
    }
  } else {
    appBody.appendChild(noDataEl('No appearance data available.'));
  }
  row2.appendChild(appCard);

  // Attraction profile card
  const attrCard = makeCard('Attraction Profile', 'col-md-6');
  const attrBody = attrCard.querySelector('.card-body');
  const attr = p.attractionProfile;
  if (attr) {
    if (attr.orientation) {
      const badge = document.createElement('span');
      badge.className = 'badge text-bg-primary fs-6 mb-3 d-inline-block';
      badge.textContent = attr.orientation;
      attrBody.appendChild(badge);
    }
    if (attr.preferredHeightCm != null) {
      const tol = attr.heightToleranceCm ?? 0;
      appendStat(attrBody, 'Preferred height', `${Math.round(attr.preferredHeightCm)} cm ± ${Math.round(tol)} cm`);
    }
    if (attr.framePreference && attr.framePreference !== 'None') appendStat(attrBody, 'Frame preference', attr.framePreference);
    if (attr.preferredWhr     != null) appendStat(attrBody, 'Preferred WHR',    attr.preferredWhr.toFixed(2));
    if (attr.symmetryWeight   != null) appendStat(attrBody, 'Symmetry weight',  Math.round(attr.symmetryWeight * 100) + '%');

    const attrWeights = [
      { label: 'Female', val: attr.femaleAttraction },
      { label: 'Male',   val: attr.maleAttraction   },
      { label: 'Other',  val: attr.otherAttraction  },
    ].filter(a => a.val != null && a.val > 0);
    if (attrWeights.length) {
      const wSection = mkDiv('mt-3');
      wSection.appendChild(sectionTitle('Attraction weights'));
      for (const w of attrWeights) {
        const pct = Math.round(w.val * 100);
        const row  = mkDiv('mot-row d-flex align-items-center gap-1');
        const lbl  = document.createElement('span'); lbl.className = 'mot-label'; lbl.textContent = w.label;
        const wrap = mkDiv('mot-bar-wrap');
        const prog = mkDiv('progress'); prog.style.height = '6px';
        const bar  = mkDiv('progress-bar bg-primary');
        bar.style.width = pct + '%'; bar.title = pct + '%';
        prog.appendChild(bar); wrap.appendChild(prog);
        row.appendChild(lbl); row.appendChild(wrap);
        wSection.appendChild(row);
      }
      attrBody.appendChild(wSection);
    }
  } else {
    attrBody.appendChild(noDataEl('No attraction profile data available.'));
  }
  row2.appendChild(attrCard);
  root.appendChild(row2);

  // ── Relationship Matrix (full-width, only with snapshot) ──────────────────
  if (p.snapshot && Object.keys(p.snapshot.relationships).length > 0) {
    const matrix = buildRelationshipMatrix(p.snapshot, state.npcMapCache);
    if (matrix) root.appendChild(matrix);
  }

  return root;
}

export async function renderKeyMoments(containerId) {
  const el = document.getElementById(containerId);
  if (!el) return;
  while (el.firstChild) el.removeChild(el.firstChild);

  const [physio, psych, goals, rel] = await Promise.all([
    api.getPhysiology(state.currentCharId).catch(() => null),
    api.getPsychology(state.currentCharId).catch(() => null),
    api.getGoals(state.currentCharId).catch(() => null),
    api.getRelationships(state.currentCharId).catch(() => null),
  ]);

  const moments = [];

  if (psych?.snapshots?.length) {
    const peak = psych.snapshots.reduce((a, b) => (b.stress ?? 0) > (a.stress ?? 0) ? b : a);
    if (peak.stress > 50)
      moments.push({ icon:'🔥', text:`Peak stress: ${Math.round(peak.stress)}`, time: peak.worldTime, cls:'danger' });
  }
  if (physio?.sleepEvents?.length) {
    const woke = physio.sleepEvents.filter(e => e.eventId === 1105 && e.hours != null);
    if (woke.length) {
      const best = woke.reduce((a, b) => (b.hours ?? 0) > (a.hours ?? 0) ? b : a);
      moments.push({ icon:'😴', text:`Best sleep: ${best.hours?.toFixed(1)}h (quality ${Math.round(best.quality ?? 0)})`, time: best.worldTime, cls:'success' });
    }
  }
  if (goals?.activeGoals?.resolved?.length) {
    const done = goals.activeGoals.resolved.filter(g => g.resolution === 'Completed');
    if (done.length)
      moments.push({ icon:'✅', text:`${done.length} goal(s) completed: ${done.map(g => g.label ?? g.kind).join(', ')}`, time: done[0].resolvedAt, cls:'success' });
  }
  const allRelEvts = Object.values(rel?.relEvents ?? {}).flat();
  const withChanges = allRelEvts.filter(e => e.kind === 'apply' && e.changes);
  if (withChanges.length) {
    const scored = withChanges.map(e => {
      const deltas = Object.values(e.changes).filter(v => v?.from != null).map(v => v.to - v.from);
      return { ev: e, delta: deltas.length ? Math.max(...deltas) : 0 };
    }).sort((a, b) => b.delta - a.delta);
    if (scored[0]?.delta > 5) {
      const p = state.npcMapCache.get((scored[0].ev.partnerId ?? '').toLowerCase())?.displayName ?? scored[0].ev.partnerId?.slice(0,8) + '…';
      moments.push({ icon:'❤️', text:`Best interaction: +${scored[0].delta.toFixed(1)} with ${p}`, time: scored[0].ev.worldTime, cls:'primary' });
    }
  }

  if (!moments.length) { el.style.display = 'none'; return; }
  el.style.display = '';
  for (const m of moments.slice(0, 5)) {
    const row = mkDiv('d-flex align-items-center gap-2 mb-2 small');
    const icon = document.createElement('span'); icon.textContent = m.icon; icon.style.fontSize = '1rem';
    const strong = document.createElement('strong'); strong.textContent = m.text;
    const timeEl = document.createElement('span'); timeEl.className = 'text-muted';
    if (m.time) timeEl.textContent = ` (${fmtWt(m.time, {short:true})})`;
    const txt = mkDiv('');
    txt.appendChild(strong); txt.appendChild(timeEl);
    row.appendChild(icon); row.appendChild(txt);
    el.appendChild(row);
  }
}

// ── Locations tab ─────────────────────────────────────────────────────────────

export const SURFACE_KIND_ICONS = { Neutral:'🌍', Social:'🤝', Private:'🏠', Intimate:'💕', Rest:'🛏️', Public:'🌳', Work:'⚒️' };

export const MOVE_TARGET_COLORS = { Social:'primary', Private:'success', Work:'warning', Rest:'secondary', Public:'info' };

// ── computeInsights ───────────────────────────────────────────────────────────
export function computeInsights(p) {
  const pers = p.personality ?? {};
  const snap = p.snapshot    ?? {};
  const phys = snap.physiology  ?? {};
  const psych= snap.psychology  ?? {};
  const beh  = snap.behavior    ?? {};
  const rels = snap.relationships ?? {};

  const N = pers.neuroticism ?? 0;
  const stress = psych.stress ?? 0;
  const valence = psych.valence ?? 0;
  const energy  = phys.energy  ?? 100;
  const allostat= phys.allostaticLoad ?? 0;
  const cogLoad = psych.cognitiveLoad ?? 0;
  const hasLowCloseness = Object.values(rels).some(r => !r.isContemptuouslyDestroyed && (r.closeness ?? 100) < 20);

  const insights = [];
  const add = (icon, title, detail, level) => insights.push({ icon, title, detail, level });

  if (N >= 0.7) {
    const mult = (0.65 + N * 1.05).toFixed(2);
    add('⚡', 'High Neuroticism amplifies stress',
      `N=${(N*100).toFixed(0)}% → stress growth ×${mult}. Current stress: ${Math.round(stress)}`,
      stress >= 70 ? 'danger' : 'warning');
  }
  if (stress >= 70) {
    add('🚫', 'Stress blocks social interactions',
      `Stress ${Math.round(stress)} significantly lowers interaction acceptance probability`,
      'danger');
  }
  if (stress >= 70 && valence < -0.3) {
    add('🔁', 'Stress + low valence feedback loop',
      `Low valence (${valence.toFixed(2)}) → avoidance → less social → stress stays high`,
      'danger');
  }
  if (allostat >= 60) {
    add('📈', 'Allostatic load suppresses recovery',
      `Load ${Math.round(allostat)} elevates cortisol baseline, suppresses mood recovery`,
      'warning');
  }
  if (pers.attachment === 'Anxious') {
    add('🔗', 'Anxious attachment accelerates bonding',
      'Relationship learning rate ×1.30 — bonds form quickly but may feel intense',
      'info');
  }
  if (pers.attachment === 'Avoidant') {
    add('🔗', 'Avoidant attachment slows bonding',
      'Relationship learning rate ×0.75 — closeness resisted, forms slowly',
      'info');
  }
  if (pers.chronotype === 'Owl') {
    add('🌙', 'Owl chronotype — evening peak',
      `Evening arousal peak. Current arousal: ${psych.arousal?.toFixed(2) ?? '—'}`,
      'info');
  } else if (pers.chronotype === 'Lark') {
    add('🌅', 'Lark chronotype — morning peak',
      `Morning arousal peak. Current arousal: ${psych.arousal?.toFixed(2) ?? '—'}`,
      'info');
  }
  if (pers.sociosexuality === 'Restricted') {
    add('❤️', 'Restricted sociosexuality',
      'Romantic & sexual interest develop slowly with new partners',
      'info');
  }
  if (valence < -0.3 && insights.filter(i => i.level === 'danger' || i.level === 'warning').length < 4) {
    add('😔', 'Negative mood → avoidance tendency',
      `Valence ${valence.toFixed(2)} — higher probability of Idle/avoidance behaviour`,
      'warning');
  }
  if (energy < 30) {
    add('😴', 'Low energy limits behaviour options',
      `Energy ${Math.round(energy)} → NeedRest ${Math.round(beh.needRest ?? 0)} — physical actions deprioritised`,
      'warning');
  }
  if (hasLowCloseness) {
    add('⚠️', 'Low closeness = high rejection risk',
      'Some partners have Closeness < 20 → elevated interaction rejection probability',
      'warning');
  }
  if (cogLoad >= 70) {
    add('🧠', 'High cognitive load',
      `CogLoad ${Math.round(cogLoad)} — reduced decision quality, slower emotional regulation`,
      'warning');
  }

  // Sort: danger first, then warning, then info. Cap at 6.
  const order = { danger: 0, warning: 1, info: 2, success: 3, secondary: 4 };
  return insights.sort((a, b) => (order[a.level] ?? 5) - (order[b.level] ?? 5)).slice(0, 6);
}

export function buildInsightCards(p) {
  const insights = computeInsights(p);
  if (!insights.length) return null;
  const row = mkDiv('row g-2 mb-3');
  for (const ins of insights) {
    const col  = mkDiv('col-auto');
    const card = mkDiv(`card insight-card insight-${ins.level}`);
    const body = mkDiv('card-body');
    const icon = mkDiv('insight-icon'); icon.textContent = ins.icon;
    const text = mkDiv('insight-text');
    const strong = document.createElement('strong'); strong.textContent = ins.title;
    const det  = document.createElement('span');
    det.className = 'text-muted d-block'; det.textContent = ins.detail;
    text.appendChild(strong); text.appendChild(det);
    body.appendChild(icon); body.appendChild(text);
    card.appendChild(body); col.appendChild(card); row.appendChild(col);
  }
  return row;
}

// ── buildCurrentStateCard ─────────────────────────────────────────────────────
export function buildCurrentStateCard(snap) {
  const col  = mkDiv('col-lg-4');
  const card = mkDiv('card profile-card');
  const hdr  = mkDiv('card-header fw-semibold small d-flex align-items-center justify-content-between');
  const hdrtxt = document.createElement('span'); hdrtxt.textContent = 'Current State';
  const badge  = document.createElement('span'); badge.className = 'badge text-bg-secondary'; badge.textContent = 'Snapshot';
  hdr.appendChild(hdrtxt); hdr.appendChild(badge);
  const body = mkDiv('card-body');

  const phys  = snap.physiology  ?? {};
  const psych = snap.psychology  ?? {};
  const beh   = snap.behavior    ?? {};
  const surf  = snap.interactionSurface ?? {};

  // Physiology
  body.appendChild(sectionTitle('Physiology'));
  const physBars = [
    { label: 'Energy',         value: phys.energy,        max: 100, cls: snapBarColor(phys.energy, 100, [30,60]) },
    { label: 'Hunger',         value: phys.hunger,        max: 100, cls: snapBarColor(phys.hunger, 100, [40,70], true) },
    { label: 'Thirst',         value: phys.thirst,        max: 100, cls: snapBarColor(phys.thirst, 100, [40,70], true) },
    { label: 'Pain',           value: phys.pain,          max: 100, cls: snapBarColor(phys.pain, 100, [20,50], true) },
    { label: 'Immune Load',    value: phys.immuneLoad,    max: 100, cls: snapBarColor(phys.immuneLoad, 100, [40,70], true) },
    { label: 'Allostatic Load',value: phys.allostaticLoad,max: 100, cls: snapBarColor(phys.allostaticLoad, 100, [40,70], true) },
    { label: 'Sleep Debt (h)', value: phys.sleepDebtHours,max: 12,  cls: snapBarColor(phys.sleepDebtHours, 12, [3,6], true) },
  ];
  for (const b of physBars) { if (b.value != null) body.appendChild(buildSnapBar(b.label, b.value, b.max, b.cls)); }
  if (phys.cyclePhase) {
    const cd = mkDiv('snap-bar-row'); cd.style.marginBottom = '3px';
    const cl = document.createElement('span'); cl.className = 'snap-bar-label'; cl.textContent = 'Cycle phase';
    const cv = document.createElement('span'); cv.className = 'badge text-bg-secondary ms-1'; cv.textContent = phys.cyclePhase;
    cd.appendChild(cl); cd.appendChild(cv); body.appendChild(cd);
  }

  // Psychology
  body.appendChild(sectionTitle('Psychology'));
  const psychBars = [
    { label: 'Stress',      value: psych.stress,       max: 100, cls: snapBarColor(psych.stress,       100, [40,70], true) },
    { label: 'Mood Baseline',value: psych.moodBaseline, max: 100, cls: snapBarColor(psych.moodBaseline, 100, [30,60]) },
    { label: 'Cog. Load',   value: psych.cognitiveLoad,max: 100, cls: snapBarColor(psych.cognitiveLoad, 100, [50,75], true) },
  ];
  for (const b of psychBars) { if (b.value != null) body.appendChild(buildSnapBar(b.label, b.value, b.max, b.cls)); }

  // Psych needs mini-grid
  const needKeys = [
    ['needSocial','Social'], ['needIntimacy','Intimacy (P)'],
    ['needAchievement','Achievement'], ['needCare','Care'],
    ['needSafety','Safety'],
  ];
  const needGrid = mkDiv('row g-0 mt-1 mb-1');
  for (const [key, lbl] of needKeys) {
    const v = psych[key];
    if (v == null) continue;
    const c = mkDiv('col-6');
    c.appendChild(buildSnapBar(lbl, v, 100, snapBarColor(v, 100, [40,70], true)));
    needGrid.appendChild(c);
  }
  if (needGrid.children.length) body.appendChild(needGrid);

  // Behavior needs
  body.appendChild(sectionTitle('Active Needs'));
  const behBars = [
    { label: 'Rest',       value: beh.needRest,       max: 100 },
    { label: 'Food',       value: beh.needFood,       max: 100 },
    { label: 'Water',      value: beh.needWater,      max: 100 },
    { label: 'Belonging',  value: beh.needBelonging,  max: 100 },
    { label: 'Competence', value: beh.needCompetence, max: 100 },
    { label: 'Intimacy',   value: beh.needIntimacy,   max: 100 },
  ];
  for (const b of behBars) {
    if (b.value == null) continue;
    const cls = snapBarColor(b.value, b.max, [40,70], true);
    body.appendChild(buildSnapBar(b.label, b.value, b.max, cls));
  }

  // Active plan + intent
  if (beh.currentPlanName) {
    body.appendChild(sectionTitle('Active Plan'));
    const planRow = mkDiv('d-flex flex-wrap gap-1 align-items-center');
    const planBadge = document.createElement('span'); planBadge.className = 'badge text-bg-primary';
    planBadge.textContent = beh.currentPlanName;
    planRow.appendChild(planBadge);
    if (beh.currentPlanUtility != null) {
      const ub = document.createElement('span'); ub.className = 'text-muted small';
      ub.textContent = `utility ${beh.currentPlanUtility.toFixed(2)}`;
      planRow.appendChild(ub);
    }
    if (beh.activeIntentKind) {
      const ib = document.createElement('span'); ib.className = 'badge text-bg-secondary';
      ib.textContent = `Intent: ${beh.activeIntentKind}`;
      planRow.appendChild(ib);
    }
    body.appendChild(planRow);
  }

  // Environment
  if (surf.location || surf.noise != null) {
    body.appendChild(sectionTitle('Environment'));
    if (surf.location) {
      const lr = mkDiv('snap-bar-row');
      const ll = document.createElement('span'); ll.className = 'snap-bar-label'; ll.textContent = 'Location';
      const lv = document.createElement('span'); lv.className = 'small fw-semibold'; lv.textContent = surf.location;
      const pr = surf.hasPrivacy != null
        ? (document.createElement('span'))
        : null;
      if (pr) { pr.className = `badge text-bg-${surf.hasPrivacy ? 'success' : 'secondary'} ms-1`; pr.textContent = surf.hasPrivacy ? 'Private' : 'Public'; }
      lr.appendChild(ll); lr.appendChild(lv); if (pr) lr.appendChild(pr);
      body.appendChild(lr);
    }
    if (surf.noise != null)    body.appendChild(buildSnapBar('Noise',    surf.noise * 100,    100, snapBarColor(surf.noise * 100, 100, [30,60], true)));
    if (surf.crowding != null) body.appendChild(buildSnapBar('Crowding', surf.crowding * 100, 100, snapBarColor(surf.crowding * 100, 100, [30,60], true)));
  }

  card.appendChild(hdr); card.appendChild(body); col.appendChild(card);
  return col;
}
