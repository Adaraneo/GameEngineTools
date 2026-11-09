# LogReader v2 — návrh architektury

> Cíl: **pochopit simulaci**, ne ji jen vizualizovat.
> Tři bolesti, které tohle řeší: (1) nechápu kauzalitu, (2) odpověď je rozházená přes taby, (3) chybí příběh.
> Logy lze přegenerovat → greenfield formát, žádná zpětná kompatibilita.

---

## 0. Centrální myšlenka

Simulace je **deterministická per tick**: sedm enginů běží v pevném pořadí ve třech fázích (A=Handle, B=Tick, C=SelfDeliver). Dnešní log tuhle strukturu zahazuje — zploští všechno na nezávislé řádky s textovou zprávou a kauzalita se dohaduje přes časové razítko (`findNearestSnapshot`).

**Atomická jednotka v2 = tick.** Uvnitř ticku = kauzální řetěz. Log má zrcadlit pipeline, ne ji rozbít.

```
PROČ: Hunger=82 (Physiology, fáze B)
  └─ způsobí → Eat (Behavior, fáze B, utility 87)
       ├─ zkonzumuje → apple_03 (World, EventId 1500)
       └─ způsobí → Hunger 82→31 (Physiology, příští tick)
```

Tohle je rozdíl mezi *„ve 12:20 se stalo X, Y, Z"* a *„X způsobilo Y, které vedlo k Z."*

---

## 1. Formát logu (greenfield)

Každý záznam nese pozici v pipeline a kauzální háčky:

```jsonc
{
  "tickId":    8472193,                 // = WorldTicks (long pozadí WDateTime), monotónní
  "worldTime": "0095-04-03T12:20:00",
  "personId":  "d3bc...",               // null = world-scoped (respawn, počasí…)
  "phase":     "B",                     // A | B | C
  "engine":    "Behavior",              // Physiology|Psychology|Behavior|Interactions|
                                        //   Relationships|Memory|SemanticMemory|World|Scheduler
  "seq":       14,                      // pořadí uvnitř ticku pro tuto postavu
  "eventId":   1003,
  "kind":      "ActionChosen",          // lidský název ze sdíleného registru
  "payload":   { "action": "Eat", "utility": 0.87, "duration": "PT8M" },
  "id":        "evt#88213",             // volitelné — když se na něj někdo odkazuje
  "causes":    ["dec#4471"]             // volitelné — explicitní příčina (cross-tick)
}
```

**Co je nové oproti dnešku:**
- `payload` je **typed objekt**, ne formátovaná věta → mizí celá `src/parsers/` regex vrstva (a s ní bugy typu `\s` vs `\\s`).
- `phase` + `engine` + `seq` → rekonstruovatelná pozice v pipeline.
- `id` + `causes` → kauzalita jako data, ne jako odhad.

Lidsky čitelnou větu LogReader **složí z payloadu** (narativní formatter, který už máš v `Narrative/`), ne rozkládá zpátky.

### Sdílený registr EventId

Jeden generovaný soubor = jediná pravda pro C# i JS. Zabije ruční opisování ID do `Set`ů v `server.js` (a varování „dej pozor na unikátnost"). Generovat z `CoreLog.cs` při buildu, nebo z jednoho `events.json`, ze kterého se generuje obojí.

---

## 2. Kauzalita — jak ji udělat skutečnou (a levně)

Klíčové zjištění: **většina kauzality je odvoditelná**, když máš `tick + phase + engine + seq`, protože pipeline je deterministická a doménová pravidla jsou známá. Nemusíš protahovat ID přes celý engine kód.

Strategie = **inference při ingestu + selektivní explicitní ID**:

| Typ vazby | Jak se získá |
|---|---|
| Stav → rozhodnutí (stejný tick) | **Inference**: Eat v Behavioru fáze B byl způsoben Hunger ze snapshotu Physiology téhož ticku. Pravidlo z domény. |
| Rozhodnutí → fyzický důsledek (stejný tick) | **Inference**: ActionChosen(Eat) ↔ ObjectUsed(1500) — stejný tick, stejná postava. |
| Akce → změna stavu (příští tick) | **Pending-effects matcher** při ingestu: akce nadhodí „očekávaný efekt", příští tick ho spáruje s reálnou změnou. |
| Cross-character (B vidí A na C) | **Inference z payloadu**: `ThirdPartyActionObserved` už nese actor/target. |
| Vzácné, neodvoditelné | **Explicitní `causes`** — jen tam, kde inference selže. |

Source-side změna je tím pádem **skromná**: emituj strukturovaný payload + tick/phase/engine/seq. Kauzální graf se staví až v ingestu.

---

## 3. Ingest — „tick store" místo per-subsystem cache

Jeden průchod → jeden tick-indexovaný store na postavu:

```js
ticks[tickId] = {
  worldTime,
  snapshots: { physio, psych, behav },   // stav po tomto ticku
  events:    [...],                        // seřazené dle phase+seq
  decisions: [...],
}
causalGraph = { nodes: [...], edges: [caused-by] }   // odvozený
narrative   = [...]                                    // významné události + próza
```

Tohle nahradí dnešní `physio-full:`, `psych-full:`, `behav-full:`… cache jedním modelem. Mizí dual-source reader (scoped + global workaround) — world-scoped události mají prostě `personId: null` a indexují se podle ticku.

---

## 4. UI — tři views, každý řeší jednu bolest

Dnešní taby kopírují **engine architekturu** (Physiology, Psychology…). Ale otázky nezní „ukaž psychologii", ale *„proč ho odmítla?"*.

### 4.1 Tick Scrubber → zabíjí skákání mezi taby
Posuvník časem nahoře. Scrub na libovolný moment → pod ním **plný stav postavy přes všechny enginy najednou** (kompaktně) + seřazený seznam co se v tom ticku stalo v pořadí pipeline. Jedna obrazovka, všechny subsystémy, jeden okamžik.

### 4.2 Why Panel → zabíjí mezeru v kauzalitě
Klikneš na jakoukoli událost/rozhodnutí → rozbalí se kauzální strom **nahoru** (co k tomu vedlo) i **dolů** (co to způsobilo).
Příklad: klik na „odmítla ReachOut" → nahoru: nízká closeness + vysoký stres + anxious attachment; dolů: pokles nálady + zakódovaná vzpomínka + decay vztahové hrany.

### 4.3 Narrative / Episodes → dodává příběh
Story tab už máš — je to nejhodnotnější věc v celém LogReaderu. Rozšířit:
- Seskupit do **epizod** (jeden spánek, jeden rozhovor, jedno jídlo) → každá jako odstavec s rozbalitelným detailem.
- Filtr podle dvojice postav, podle valence („ukaž mi špatné dny").
- Próza z payloadu přes `Narrative/` formatter.

---

## 5. Fázování (postup, až dáš zelenou)

| Fáze | Obsah | Riziko | Zisk |
|---|---|---|---|
| **P0** | Schema logu + sdílený EventId/kind registr (generovaný pro C# i JS) | nízké | základ |
| **P1** | Source-side: emituj typed payload + tick/phase/engine/seq | střední (mechanická změna call-sites) | mizí parser vrstva |
| **P2** | Ingest: tick store + kauzální inference + pending-effects matcher | střední | kauzální graf vzniká |
| **P3** | UI: scrubber → Why panel → narrative epizody | nízké (frontend zůstává Bootstrap+Chart.js) | porozumění |

### Levný časný zisk (de-risk)
**Tick scrubber jde postavit na DNEŠNÍCH datech** — `worldTime` už umožňuje indexaci podle ticku. Adresuje „skákání mezi taby" *bez* přepisu pipeline. Dobrý první krok, který ukáže hodnotu dřív, než sáhnu na C#.

---

## 6. Co se NEpřepisuje
- Frontend stack (Bootstrap + Chart.js) — funguje, přepis = týden za nulu.
- TTL cache koncept — v pořádku.
- Per-character scoping princip — správný.
- Story tab — naopak se rozšiřuje, je to správný směr.

## 7. Co zůstává těžké (bez příkras)
- **Cross-tick kauzální párování** — pending-effects matcher je netriviální, hlavně u akcí s odloženým efektem (spánek, těhotenství).
- **„Významnost" pro narativ** — co stojí za vyprávění? Peak-end? Změna nad práh? Potřebuje scoring, ne jen filtr.
- **Objem dat** — tick-indexace celého běhu může být velká; nejspíš bude potřeba lazy-load oken kolem scrubber pozice.
