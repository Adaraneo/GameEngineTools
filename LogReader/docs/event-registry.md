# Event registry (P0)

`LogReader/src/eventRegistry.json` is the **canonical catalog of every simulation event** —
one entry per `[LoggerMessage]` in `CoreLog.cs`:

```jsonc
{ "id": 1500, "kind": "ObjectUsed", "engine": "World", "scope": "character",
  "level": "Information",
  "payload": [ { "name": "ObjectId", "type": "string" }, … ] }
```

## Single source of truth — do NOT hand-edit the json

`CoreLog.cs` is the only place you author events. The json is **generated from it by reflection**
and kept honest by a drift-guard test.

| | |
|---|---|
| **Generator + guard** | `EngineTests/EventRegistryTests.cs → EventRegistry_MatchesCoreLog` |
| **Default run** | Asserts the committed json matches `CoreLog.cs`. Fails if they drift. |
| **Regenerate** | Set `GENERATE_REGISTRY=1`, run the test, commit the new json. |

```bash
# After adding/changing an event in CoreLog.cs:
GENERATE_REGISTRY=1 "…/vstest.console.exe" \
  "EngineTests/bin/Debug/net8.0/EngineTests.dll" \
  --tests:EventRegistry_MatchesCoreLog
# → rewrites eventRegistry.json; review & commit.
```

`engine` is derived from the EventId range; `scope` is `world` for events logged without a
character scope (respawn 1501, scheduler 4100–4103), else `character`.

## Consumers

- **Server** (`server.js`) — imports `src/eventRegistry.js`; validates the hand-curated
  consumer Sets (`PHYSIO_IDS`, `BEHAV_IDS`, …) against the catalog at startup via
  `assertKnown()`, so a typo or a removed CoreLog event fails fast instead of silently
  returning empty results. Exposes `GET /api/event-registry`.
- **Client** (`app.js`) — fetches `/api/event-registry` at startup and uses `idNames`
  for id → name resolution (replaced the old hand-maintained `EVENT_ID_NAMES`, which had
  already drifted: bogus `4000`, duplicate `2003`).

## P1 — structured emit (done)

The C# file logger now emits structured data into every JSONL entry, with **zero call-site
changes** (extracted from the `[LoggerMessage]` state):

- `Payload` — typed event fields, e.g. `{ "Action": "Eat", "Utility": 0.87 }`, JSON types
  preserved (number/bool/string). Mirrors this catalog's `payload` schema.
- `WorldTick` — numeric `WDateTime.WorldTicks`, the grouping key for the v2 tick-store.

The existing `Message` string is unchanged, so today's regex parsers keep working — the new
fields are purely additive.

## P2 — payload-first parsing (in progress)

Parsers read `entry.Payload` directly when present, falling back to the legacy regex on
`entry.Message` for pre-P1 logs — so old logs keep working unchanged, new logs skip regex
entirely. Payload field names = CoreLog parameter names (verified against this catalog).

**Data flow note:** subsystem parsers receive full entries (`JSON.parse`), so `Payload`/`WorldTick`
reach them. The compact `events-raw` cache (summary/frequency/raw-table) deliberately does
*not* carry payload — those consumers don't need it.

### Converted (dual-path)
All field-extracting parsers now read `Payload` first: physiology/psychology/behavior
snapshots (5000/5100/5200), repro (5003-5006), death (5007), psych events (5101-5105),
sleep (1100-1113) + cycle/sleep-ended (5001/5002), behavior decisions & habits (1000-1016, 5201),
intent (1005-1010), goals (1300-1304), schedule (1400-1404), interactions (1200-1209),
values (1210-1212), memory (3000-3003), relationships (2005, 2008, 2010-2013), objects (1500/1501).

**Verification:** an automated check (Python, run ad-hoc) scans every `p.<Field>` access across
`src/parsers/*.js` and asserts it exists in the catalog — **268 accesses, zero unknown fields**.
This is the only real risk of blind conversion (a misspelled field name) and it is covered.

### Intentionally left on regex
`2001/2002/2006/2009` (RelEvent/RelApply/RelDecay) — their structured payload field is itself a
pre-joined `Changes` string, so payload gives no decomposition over the existing regex.

### Not runtime-verified yet
Conversions are correct-by-construction (field names verified) and fully backward-compatible
(pre-P1 logs hit the regex fallback unchanged), but have **not** been run live — needs node/Docker
+ regenerated logs. Dual-path verbosity is deliberate: the regex branch is byte-identical to the
prior behavior, so nothing can regress on old logs.

### Tick scrubber (P2, done — unverified)
`GET /api/logs/:id/ticks` (`buildTickStore` in server.js) re-indexes the already-parsed
per-subsystem caches by `worldTime` tick: every event-bearing tick carries the nearest
physiology/psychology/behavior snapshot at-or-before it. New client tab **🎞️ Ticks** = a
slider + state panel + "what happened this tick" list — all subsystems for one moment on one
screen (directly addresses the tab-jumping pain).

- Grouping key is `worldTime` text (every parser carries it) → **works on pre-P1 logs too**,
  so the scrubber is visible even before regenerating.
- Reuses Story's exact cache-access patterns; additive (new endpoint + tab, nothing else touched).
- Bounded to 6000 event-bearing ticks (truncation flagged in the response).

### Why panel (P2, done — causal inference)
Clicking any event in the Ticks tab opens **❓ Why** (client-side modal, no server change).
`computeCausality()` infers, from the tick-store alone:

- **Caused by ↑** — the need/state driver behind an action (Eat ← Hunger=82 pressing),
  read from the same tick's snapshot; plus same-tick goal bias targeting the action.
- **Led to ↓** — same-tick physical result (ObjectUsed), the driver need's reduction at the
  next event-tick (Hunger 82→31), and for stressors (norm violation, nightmare, rejection)
  the next-tick stress shift.

Links are labelled as inferred (same-tick state + pipeline order + next-tick deltas), not
stored ground truth. Causality is **affordance-domain aware**: object actions (`UseObject:Rest`)
and `ObjectUsed` events resolve their driver via the affordance (Rest→NeedRest, Work→NeedCompetence…),
and the "→ object used" effect only links objects whose affordance domain matches the action
(no object→object, no unrelated cross-links). Causes/effects are deduped by text (the same
logical action appears as a behavior + a physio row in one tick).

### Diary — removed
A per-day digest tab (`/episodes`, `buildDayDigests`) was built and then a prose variant,
but it was redundant with **Story** (event log) and **Ticks** (moment inspector) and didn't
earn its place — removed entirely (endpoint, builder, tab, client code). The timeline views
are now just **Story (event feed)** + **Ticks (moment + Why)**.

### Wider state in charts (HPA axis & mood)
Snapshots were widened to carry the core stress/mood scalars that previously existed only as
single live values: **PhysiologySnapshot (5000) += AllostaticLoad, Cortisol**;
**PsychologySnapshot (5100) += MoodBaseline**. New Psychology chart **HPA Axis & Mood** plots
Stress + Cortisol + Allostatic Load + MoodBaseline over time (merged by worldTime — both
engines snapshot in the same tick). This was a clean vertical slice through the whole pipeline:
add `[LoggerMessage]` params → regen registry (drift-guard enforced) → payload-first parsers
read the new fields → chart. **Requires regenerated logs** — the new fields only exist on
post-change snapshots; on older logs the chart shows only the Stress line.

### Sex hormones & menstrual cycle in charts
Same pipeline slice again: **PhysiologySnapshot (5000) += Testosterone**;
**PhysiologyCycle (5001) += Libido, Ovulation, Pain, Bloat, Tender, Pmdd** (was only Phase + Day).
New sex-adaptive Physiology chart **Sex Hormones & Cycle**: testosterone line for males,
and for fertile females the cycle metrics — libido on a secondary axis, ovulation windows
shaded, cramp/bloat as lines. Merged by worldTime across the two event types.
Notes: testosterone is 0 for females (line stays flat); 5001 is Debug-level and emitted only
while the cycle is active (non-paused). **Requires regenerated logs.**

### Relationship depth dimensions (edge snapshot 2005)
The edge snapshot logged only the 10 surface dims; extended with the depth model:
**RelEdgeUpdated (2005) += Communal, Exchange, Residue (grudge), Responsive (desire),
Dominance, Prestige, Contempt (bool)**. The relationship timeline chart now plots these as
dashed series (shown only when present): communal vs. exchange relationship type, transgression
residue (repair model), responsive desire (Basson), perceived dominance vs. prestige (status).
`contemptDestroyed` is parsed into the edge data (terminal flag, not a chart line). **Requires
regenerated logs.**

### Psychology motivations (snapshot 5100)
**PsychologySnapshot (5100) += NeedSocial, NeedIntimacy, NeedAchievement, NeedCare,
NeedSafety, SicknessWithdraw** (Motivations was null in the log). New Psychology chart
**Motivations** plots the five drives over time — the psychological "what pulls them" layer,
distinct from the Behavior needs. **Requires regenerated logs.**

### Nutrition (snapshot 5000)
**PhysiologySnapshot (5000) += Calories, VitaminD, Iron, Protein, Glucose** (was a live-only
snapshot card). New Physiology **Nutrition** chart plots them over time — VitaminD drives mood,
Iron post-menses recovery, glucose the energy/cog-load dips. **Requires regenerated logs.**

### Physical aging (snapshot 5000)
**PhysiologySnapshot (5000) += Grey, Wrinkle, HairDensity, Muscle, Bone**. New Physiology
**Physical Aging** chart plots them over time (0–1 fractions shown as %); meaningful on long
runs — watch grey/wrinkles rise (cortisol-accelerated) and muscle/bone decline with age.
**Requires regenerated logs.**

### Active intent (snapshot 5200)
**BehaviorSnapshot (5200) += IntentKind, IntentStrength, IntentCommitment**. Surfaced in the
**Ticks** scrubber Behavior panel as a `🧭 Kind NN% ·cN` badge (the stabilised direction behind
the plan) rather than a chart — intent kind is categorical, and intent *changes* already log as
events (1005–1010). **Requires regenerated logs.**

### Logging-coverage audit — complete ✅
All identified state is now logged as time series / scrubber state. Snapshot field counts after
the audit: **5000 (physiology) = 20 scalars**, **5100 (psychology) = 13**, **5200 (behavior) =
10 + intent**, **2005 (relationship edge) = full edge**.

### Remaining (P2 cont.)
- Enrich Why causes: add habit/schedule/intent event streams to the tick-store.
- Optional: propagate numeric `WorldTick` through parsers for robust sort keys.
