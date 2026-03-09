# GameEngineTools — README pro konzolové aplikace

> **50PSoftware · Vigilia Insectianis Engine**
> Verze dokumentu: 2026-03 · Autor: generováno ze zdrojových kódů projektu

---

## Obsah

1. [Co je GameEngineTools?](#co-je-gameenginetools)
2. [Architektura enginu](#architektura-enginu)
3. [Rychlý start — minimální konzolová aplikace](#rychlý-start)
4. [GameEngineToolsRuntime — spuštění a dispose](#gameenginetoolsruntime)
5. [Manager — správa postav](#manager)
6. [Vytvoření a generování postav](#vytvoření-postav)
7. [Tick — herní smyčka](#tick--herní-smyčka)
8. [Co s postavou můžu dělat — akce a interakce](#co-s-postavou-můžu-dělat)
   - [Fyziologické akce (Physiology)](#fyziologické-akce)
   - [Spánek a sleep systém](#spánek-a-sleep-systém)
   - [Psychologické stavy](#psychologické-stavy)
   - [Behavior engine — autonomní chování](#behavior-engine)
   - [Interakce mezi postavami](#interakce-mezi-postavami)
   - [Vztahy (Relationships)](#vztahy)
   - [Paměť (Memory)](#paměť)
9. [Události (Domain Events) — kompletní seznam](#události--domain-events)
10. [Snapshot — čtení stavu postavy](#snapshot--čtení-stavu-postavy)
11. [Konfigurace (appsettings.json)](#konfigurace)
12. [Fyzická postava (CharacterBase, NPC, PC)](#fyzická-postava)
13. [Armory — zbraně a brnění](#armory)
14. [Tipy a časté chyby](#tipy-a-časté-chyby)

---

## Co je GameEngineTools?

GameEngineTools je C# knihovna pro simulaci postav (NPC i PC) v herním světě. Každá postava má **šest propojených enginů** které běží v pevném pořadí při každém tiku:

```
Physiology → Psychology → Behavior → Interactions → Relationships → Memory
```

Enginy spolu komunikují výhradně přes **Domain Events** — žádný přímý přístup mezi enginy. Výsledkem je postava, která:
- má fyzické potřeby (hlad, žízeň, únava, bolest...)
- reaguje emocionálně na situace a interakce
- autonomně volí akce podle svých potřeb a osobnosti
- buduje vztahy s ostatními postavami
- pamatuje si důležité události

---

## Architektura enginu

```
GameEngineToolsRuntime         ← vstupní bod (DI kontejner, spuštění)
        │
        └─► GameEngineToolsManager     ← správce všech postav
                │
                └─► IHuman (OrchestratedHuman)   ← jedna postava
                        │
                        ├─► IPhysiologyEngine     ← energie, hlad, žízeň, bolest, cyklus
                        ├─► IPsychologyEngine      ← emoce, stres, valence
                        ├─► IBehaviorEngine        ← výběr akcí, spánek
                        ├─► IInteractionEngine     ← kontext prostředí, nabídky interakcí
                        ├─► IRelationshipsEngine   ← hrany vztahů (Like, Trust, Closeness...)
                        └─► IMemoryEngine          ← epizodická a sémantická paměť
```

**Klíčové principy:**
- **Double-buffering**: enginy čtou z minulého snapshotu, zapisují do nového → žádné race conditions
- **Event-driven**: enginy si navzájem posílají `IDomainEvent`, ne volají metody
- **DI bez IHost**: `ServiceCollection` + `BuildServiceProvider()`, žádný hosting overhead

---

## Rychlý start

```csharp
// Program.cs — minimální konzolová aplikace

using GameEngineTools;
using GameEngineTools.World.Utils.Time;

// 1. Načti WorldTimeSpec (kalendář, tiky) bez spuštění DI
var spec = GameEngineToolsRuntime.LoadSpec();

// 2. Spusť runtime (DI kontejner + initialize)
await using var runtime = await GameEngineToolsRuntime.StartAsync(
    consoleLogs: true   // zapni konzolové logy při vývoji
);

// 3. Získej manager
var manager = runtime.GameEngineToolsManager;

// 4. Vygeneruj náhodnou postavu
var npc = manager.RandomizePerson();

// 5. Přidej postavu do světa
manager.NPPCs.Add(new NPC(100, npc));

// 6. Herní smyčka
var clock = runtime.Clock;
var dt = WTimeSpan.FromHours(1);   // jeden herní tick = 1 hodina

for (int i = 0; i < 24; i++)
{
    var now = clock.Now;
    npc.Tick(now, dt);             // postava žije

    // Čti stav
    var snap = npc.Snapshot;
    Console.WriteLine($"[{now}] Energie: {snap.Physiology.Energy:F1}, " +
                      $"Emoce: {snap.Psychology.DominantEmotion}");
}
```

---

## GameEngineToolsRuntime

### `GameEngineToolsRuntime.LoadSpec()`
Načte `WorldTimeSpec` (kalendář Vigilia Insectianis) **bez** spuštění DI kontejneru. Použij tehdy, když potřebuješ pracovat s `WDateTime` dříve, než voláš `StartAsync`.

```csharp
var spec = GameEngineToolsRuntime.LoadSpec();
// spec.TicksPerDay, spec.HoursPerDay, spec.Calendar...
```

### `GameEngineToolsRuntime.StartAsync(...)`

| Parametr | Typ | Popis |
|---|---|---|
| `startTime` | `WDateTime?` | Počáteční čas světa. `null` = začátek roku 1322 |
| `consoleLogs` | `bool` | Zapne konzolové logování |
| `logsRoot` | `string?` | Kořen adresáře pro log soubory |
| `generatedFileOptions` | `GeneratedFileOptions?` | Cesty pro player/NPC soubory |

Vrací `GameEngineToolsRuntimeHandle` — implementuje `IAsyncDisposable`.

```csharp
await using var runtime = await GameEngineToolsRuntime.StartAsync(
    startTime: WDateTime.New(WDateOnly.New(1322, 1, 1)),
    consoleLogs: false,
    logsRoot: "logs/",
    generatedFileOptions: new GeneratedFileOptions
    {
        PlayerDirectory = "data/player",
        NPCDirectory    = "data/npcs"
    }
);

// Přístup ke klíčovým službám:
var manager = runtime.GameEngineToolsManager;
var clock   = runtime.Clock;
var sp      = runtime.Services;   // IServiceProvider pro ostatní služby
```

### Uložení a načtení herního času

```csharp
// Ulož tiky při ukončení
File.WriteAllText("gametime.txt", clock.Now.Ticks.ToString());

// Načti při příštím spuštění
var spec = GameEngineToolsRuntime.LoadSpec();
var defaultTicks = spec.Calendar.DaysFromDate(1, 1, 1) * spec.TicksPerDay;
var initTicks = File.Exists("gametime.txt") && long.TryParse(File.ReadAllText("gametime.txt"), out var t)
    ? t : defaultTicks;

await using var runtime = await GameEngineToolsRuntime.StartAsync(new WDateTime(initTicks));
```

---

## Manager

`IGameEngineToolsManager` (implementace `GameEngineToolsManager`) je centrální správce.

### Vlastnosti

| Vlastnost | Typ | Popis |
|---|---|---|
| `NPPCs` | `List<CharacterBase>` | Všechny aktivní postavy ve světě |
| `Items` | `Dictionary<Type, object>` | Zbraně, brnění a jiné herní objekty |

### Metody

```csharp
// Inicializace (volá se automaticky při StartAsync, manuálně při resetu)
manager.Initialize();

// Vygeneruj náhodnou postavu (výchozí věk)
IHuman npc = manager.RandomizePerson();

// Vygeneruj postavu s věkovým rozsahem
IHuman child = manager.RandomizePerson(minAge: 5, maxAge: 12);
IHuman adult = manager.RandomizePerson(minAge: 18, maxAge: 65);

// Přidej postavu do světa
manager.NPPCs.Add(new NPC(100, npc));

// Získej zbraně a brnění (po Initialize)
var weapons   = (List<Weapon>)manager.Items[typeof(Weapon)];
var armorParts = (List<ArmorPart>)manager.Items[typeof(ArmorPart)];
```

---

## Vytvoření postav

### Náhodná postava (doporučeno)

```csharp
var npc = manager.RandomizePerson();
// npc.Identity.FirstName, npc.Identity.LastName, npc.Biology, npc.Personality...
```

### Manuální blueprint

```csharp
var factory   = sp.GetRequiredService<IHumanFactory>();
var generator = sp.GetRequiredService<IHumanBlueprintGenerator>();

// Vygeneruj blueprint a uprav dle potřeby
var blueprint = generator.Generate();
// blueprint.Identity, blueprint.Biology, blueprint.Personality, blueprint.PhysicalAppearance

var human = factory.Create(blueprint);
```

### Import ze souboru (PC — hráčská postava)

```csharp
var gf = (GeneratedFile)sp.GetRequiredService<IGeneratedFile>();
var pc = gf.ImportPC("player.json");
manager.NPPCs.Add(pc);
```

---

## Tick — herní smyčka

Metoda `Tick(WDateTime now, WTimeSpan dt)` je **jediný způsob**, jak postavu posunout v čase. Musíš ji volat pravidelně.

```csharp
var dt = WTimeSpan.FromHours(1);   // krok = 1 herní hodina

// Základní smyčka
while (gameRunning)
{
    var now = clock.Now;

    foreach (var character in manager.NPPCs)
    {
        character.Person.Tick(now, dt);
    }

    // Posuň hodiny (pokud nemáš automatické hodiny)
    // clock.Advance(dt);
}
```

### Vnitřní pořadí Tick fází

```
Fáze A: Doruč naplánované akce (Scheduler.Due)
Fáze A: Doruč inbox události (ReceiveEvent queue)
Fáze B: Tick enginů v pořadí:
         Behavior → Physiology → Psychology → Interactions → Relationships → Memory
Fáze C: Self-deliver vlastních událostí (paměť, vztahy na sebe sama)
Publikace událostí na EventBus (ostatní postavy je dostanou v příštím ticku)
```

> **Proč je to důležité?** Fáze B čte ze **starého** snapshotu a zapisuje do nového. Díky tomu žádný engine neovlivňuje jiný engine v rámci stejného ticku — simulace je konzistentní.

---

## Co s postavou můžu dělat

### Fyziologické akce

Physiology engine automaticky řídí: energii, spánkový dluh, hlad, žízeň, bolest, imunitní systém a (volitelně) menstruační cyklus.

**Drift hodnot za hodinu (bez akce):**

| Hodnota | Výchozí stav | Drift bez akce |
|---|---|---|
| `Energy` | 70 | −2 / hodinu |
| `Hunger` | 25 | +6 / hodinu |
| `Thirst` | 20 | +8 / hodinu |
| `SleepDebtHours` | 2 | +0.6 / hodinu |
| `Pain` | 5 | žádný drift (řeší SelfCare) |
| `ImmuneLoad` | 10 | −0.3 / hodinu |

**Akce a jejich vliv na fyziologii:**

```
Sleep:    Energy +15/h, SleepDebt −0.9/h, Hunger +2/h, Thirst +1/h
Eat:      Hunger −40/h, Energy +5/h
Drink:    Thirst −50/h
SelfCare: Pain −10/h, ImmuneLoad −0.5/h, Energy −0.5/h
```

**Přímé vyslání ActionCommitted (simulace provedené akce):**

```csharp
// Postava snědla — fyzicky sníž hlad
npc.ReceiveEvent(new ActionCommitted(
    OccurredAt: clock.Now,
    Human:      npc.Id,
    ActionName: ActionNames.Eat,
    Duration:   WTimeSpan.FromMinutes(30)
));
```

**Dostupné `ActionNames`:**

| Konstanta | Popis |
|---|---|
| `ActionNames.Sleep` | Spánek |
| `ActionNames.Eat` | Jídlo |
| `ActionNames.Drink` | Voda |
| `ActionNames.ReachOut` | Sociální kontakt |
| `ActionNames.Work` | Práce |
| `ActionNames.Create` | Kreativní činnost |
| `ActionNames.SelfCare` | Péče o sebe |
| `ActionNames.InviteIntimacy` | Nabídka intimity |
| `ActionNames.Idle` | Nečinnost |

---

### Spánek a sleep systém

Spánek je řízen jako víceúrovňový stav machine: **Falling → Light → Deep → REM → probuzení**.

#### Postup spánkového cyklu

```
1. BehaviorEngine vyhodnotí NeedRest >= SleepPromptThreshold (výchozí: 70)
2. Engine vyšle SleepPromptRequested → ty jako hráč/systém musíš odpovědět
3. Odpovíš SleepConfirmed nebo SleepDeclined
4. Při SleepConfirmed se spustí DefaultSleepSession
5. Session automaticky tickuje fáze: Falling (15 min) → Light (45 min) → Deep (2.5 h) → REM (1.5 h)
6. Po probuzení se vyšle SleepEnded (s Quality a TotalHoursSlept)
```

#### Potvrzení spánku

```csharp
// Hráč/systém potvrzuje spánek
npc.ReceiveEvent(new SleepConfirmed(
    OccurredAt:    clock.Now,
    Human:         npc.Id,
    PlannedWakeUp: clock.Now + WTimeSpan.FromHours(8),
    Companion:     null,           // volitelný doprovod (jiný IHuman)
    SharedType:    SharedSleepType.None
));

// Hráč odmítá spánek (postava se penalizuje stresem)
npc.ReceiveEvent(new SleepDeclined(
    OccurredAt: clock.Now,
    Human:      npc.Id
));
```

#### Přerušení spánku zvenčí

```csharp
// Přepad, útok, hlasitý zvuk...
npc.ReceiveEvent(new SleepInterrupted(
    OccurredAt: clock.Now,
    Human:      npc.Id,
    Cause:      "ambush"
));
```

#### Konfigurace spánku (appsettings.json)

```json
"Sleep": {
  "SleepPromptThreshold": 70.0,
  "SleepGraceHours": 4.0,
  "MaxDeclineCount": 3,
  "DeclinePenaltyStressPerHour": 2.0,
  "FallingDurationHours": 0.25,
  "LightDurationHours": 0.75,
  "DeepDurationHours": 2.5,
  "RemDurationHours": 1.5,
  "AmbushBaseChancePerHour": 0.03,
  "NightmareChanceHighStress": 0.25
}
```

---

### Psychologické stavy

Psychology engine řídí emocionální stav postavy. Čtení:

```csharp
var psych = npc.Snapshot.Psychology;

Console.WriteLine($"Valence (nálada):      {psych.Valence:F2}   // -1..+1");
Console.WriteLine($"Arousal (aktivace):    {psych.Arousal:F2}   //  0..1");
Console.WriteLine($"Dominance:             {psych.Dominance:F2}  //  0..1");
Console.WriteLine($"Stres:                 {psych.Stress:F1}    //  0..100");
Console.WriteLine($"Kognitivní zátěž:      {psych.CognitiveLoad:F1}");
Console.WriteLine($"Dominantní emoce:      {psych.DominantEmotion}");
```

**`DiscreteEmotion` enum:**

```
Neutral, Joy, Sadness, Anger, Fear, Disgust, Surprise, Tenderness, Pride, Shame
```

**Jak se mění emoce:**
- Dobrý spánek → Valence roste, Stres klesá
- Odmítnutá interakce → Valence klesá (podle Neuroticism osobnosti)
- MicroPositive event → Valence +0.05
- MicroNegative event → Valence −0.06, Stres +2
- Vzpomínka na pozitivní epizodu → Valence +0.05

---

### Behavior engine

Behavior engine autonomně vybírá akci každý tick na základě potřeb. Můžeš číst aktuální plán:

```csharp
var behavior = npc.Snapshot.Behavior;

Console.WriteLine($"Aktuální plán:    {behavior.CurrentPlan?.Name ?? "žádný"}");
Console.WriteLine($"Potřeba odpočinku: {behavior.NeedRest:F1}");
Console.WriteLine($"Potřeba jídla:     {behavior.NeedFood:F1}");
Console.WriteLine($"Potřeba vody:      {behavior.NeedWater:F1}");
Console.WriteLine($"Potřeba kontaktu:  {behavior.NeedBelonging:F1}");
Console.WriteLine($"Potřeba kompetence:{behavior.NeedCompetence:F1}");
Console.WriteLine($"Potřeba intimity:  {behavior.NeedIntimacy:F1}");
Console.WriteLine($"Čeká na spánek:    {behavior.WaitingForSleepConfirmation}");
```

**Jak behavior vybírá akce (utility scoring):**

Každá akce má vypočítanou "utilitu" = jak moc ji postava potřebuje × osobnostní váha. Vybere se akce s nejvyšší utilitou s inercií (váha 0.25 pro aktuálně běžící akci).

**Cooldowny:** Po `ReachOut` je 4 h cooldown, po `InviteIntimacy` 6 h cooldown.

---

### Interakce mezi postavami

Interakce probíhají přes události. **Ty jako herní systém navrhneš interakci, engine rozhodne o přijetí.**

#### Navrhni interakci

```csharp
// Postava A chce mluvit s postavou B
npcB.ReceiveEvent(new InteractionProposed(
    OccurredAt: clock.Now,
    From:       npcA.Id,
    To:         npcB.Id,
    Act:        SpeechAct.SmallTalk,   // viz SpeechAct enum
    Content:    "Ahoj, jak se máš?"
));
```

**`SpeechAct` enum:**

| Hodnota | Popis |
|---|---|
| `SmallTalk` | Běžný rozhovor |
| `Question` | Otázka |
| `SelfDisclosure` | Sdílení osobního |
| `Validation` | Potvrzení, pochvala |
| `Boundary` | Stanovení hranice |
| `Humor` | Vtip |
| `Meta` | Metakomunikace |
| `Invite` | Pozvání |

#### Fyzický kontakt

```csharp
npcB.ReceiveEvent(new TouchAttempted(
    OccurredAt: clock.Now,
    From:       npcA.Id,
    To:         npcB.Id,
    Level:      TouchLevel.Friendly
));
```

**`TouchLevel` enum:** `None, Light, Friendly, Intimate`

#### Pravděpodobnost přijetí interakce

Engine automaticky vypočítá `p(přijetí)` na základě:
- Closeness, Comfort, Trust v hraně vztahu (výchozí 30 pokud neznají)
- Valence nálady (postava v dobré náladě = více přijímá)
- Soukromí prostředí (+0.05 pokud HasPrivacy)
- Přeplněnosti (Crowding snižuje)
- Stresu postavy (snižuje)
- Misattribution rate (při stresu postava špatně interpretuje signály)

#### Změna prostředí (ovlivňuje interakce)

```csharp
npc.ReceiveEvent(new ContextChanged(
    OccurredAt: clock.Now,
    Human:      npc.Id,
    Location:   "Hospoda",
    HasPrivacy: false,
    Noise:      0.7,      // 0..1
    Crowding:   0.8       // 0..1
));
```

---

### Vztahy

Každá postava má hrany vztahů ke všem, koho potkala. Každá hrana má 6 dimenzí (0–100):

| Dimenze | Výchozí | Popis |
|---|---|---|
| `Like` | 45 | Obecná sympatie |
| `Trust` | 45 | Důvěra |
| `Attraction` | 35 | Přitažlivost |
| `Closeness` | 10 | Blízkost / intimita |
| `Respect` | 55 | Respekt |
| `Comfort` | 40 | Komfort v přítomnosti |

#### Čtení vztahů

```csharp
var rels = npc.Snapshot.Relationships.Edges;

foreach (var (otherId, edge) in rels)
{
    Console.WriteLine($"Vztah k {otherId.Value}:");
    Console.WriteLine($"  Sympatie: {edge.Like:F1}, Důvěra: {edge.Trust:F1}");
    Console.WriteLine($"  Blízkost: {edge.Closeness:F1}, Komfort: {edge.Comfort:F1}");
}
```

#### Manuální ovlivnění vztahů

```csharp
// Pozitivní mikro-interakce (kompliment, pomoc...)
npc.ReceiveEvent(new MicroPositive(
    OccurredAt: clock.Now,
    A:    npc.Id,
    B:    otherNpc.Id,
    What: "kompliment"
));
// → Like +2, Trust +1, Closeness +1.5, Comfort +2

// Negativní mikro-interakce (urážka, lež...)
npc.ReceiveEvent(new MicroNegative(
    OccurredAt: clock.Now,
    A:    npc.Id,
    B:    otherNpc.Id,
    What: "urážka"
));
// → Like −2.5, Trust −2, Comfort −2

// První dojem (při prvním setkání)
npc.ReceiveEvent(new FirstImpressionFormed(
    OccurredAt: clock.Now,
    A:          npc.Id,
    B:          otherNpc.Id,
    Like:       60.0,
    Attraction: 40.0
));

// Pokus o opravu vztahu
npc.ReceiveEvent(new RepairAttempt(
    OccurredAt: clock.Now,
    A:          npc.Id,
    B:          otherNpc.Id,
    Accepted:   true
));
// → Trust +4, Closeness +3 (nebo −4, −3 pokud odmítnuto)
```

#### Automatický decay vztahů

Každý tick vztahy pomalu konvergují k neutrálním hodnotám (bez interakce vztahy slábnou):
- `Like` → 50, `Trust` → 50, `Closeness` → 35, `Respect` → 55, `Comfort` → 45
- Rychlost: `DecayPerDay = 1.5` (konfigurovatelné)

---

### Paměť

Paměťový engine ukládá **epizodické** vzpomínky na události a **sémantické** fakty.

#### Čtení paměti

```csharp
var memory = npc.Snapshot.Memory;

// Epizodické vzpomínky
foreach (var episode in memory.Episodes)
{
    Console.WriteLine($"[{episode.OccurredAt}] {episode.Description}");
    Console.WriteLine($"  Síla: {episode.Strength:F2}, Salience: {episode.Salience:F2}");
    Console.WriteLine($"  Emoce: {episode.Emotion}");  // Positive / Negative / Neutral
}

// Sémantické fakty
foreach (var (key, fact) in memory.Semantics)
{
    Console.WriteLine($"{key}: {fact.Value}");
}
```

#### Vyvolání vzpomínky (ovlivní psychologii)

```csharp
// Vzpomínka na konkrétní epizodu zvýší/sníží valenci
npc.ReceiveEvent(new MemoryRecalled(
    OccurredAt: clock.Now,
    Human:      npc.Id,
    EpisodeId:  someEpisodeGuid
));
```

#### Automatické chování paměti

- Každý tick: vzpomínky slábnou (`ForgettingRate = 0.06 / den`)
- Každých 24 hodin: 10 nejsalientnějších vzpomínek se posílí (`SleepConsolidationBoost = 0.12`)
- Vzpomínky pod prahem `PruneThreshold = 0.01` jsou odstraněny
- Interakce, MicroPositive/MicroNegative a akce jsou automaticky ukládány

---

## Události — Domain Events

### Kompletní seznam vysílaných a přijímaných událostí

#### Physiology

| Událost | Kdy | Co způsobí |
|---|---|---|
| `MensesStarted` | Začátek menstruace | Psych: Valence −0.05 |
| `MensesEnded` | Konec menstruace | — |
| `OvulationWindowOpened` | Ovulační okno | Psych: Arousal +0.05 |
| `CycleDayAdvanced` | Každý herní den | Info o fázi cyklu |

#### Psychology

| Událost | Kdy |
|---|---|
| `EmotionShifted` | Při změně dominantní emoce |
| `StressSpiked` | Při prudkém nárůstu stresu |

#### Behavior

| Událost | Kdy |
|---|---|
| `ActionProposed` | Behavior navrhl akci |
| `ActionCommitted` | Akce byla zahájena |

#### Sleep

| Událost | Kdy |
|---|---|
| `SleepPromptRequested` | Potřeba spánku překročila threshold |
| `SleepConfirmed` | **Ty posíláš** → potvrzení spánku |
| `SleepDeclined` | **Ty posíláš** → odmítnutí |
| `SleepInterrupted` | **Ty posíláš** → přerušení zvenčí |
| `SleepPhaseChanged` | Automaticky při přechodu fáze |
| `SleepEnded` | Konec spánku (přirozený nebo přerušený) |
| `NightmareTriggered` | Noční můra (při vysokém stresu) |

#### Interactions

| Událost | Kdy / Co |
|---|---|
| `ContextChanged` | **Ty posíláš** → změna prostředí |
| `InteractionProposed` | **Ty posíláš** → navrhni interakci |
| `TouchAttempted` | **Ty posíláš** → fyzický kontakt |
| `InteractionOutcome` | Engine rozhodl: accepted/declined |

#### Relationships

| Událost | Kdy / Co |
|---|---|
| `FirstImpressionFormed` | **Ty posíláš** → první dojem |
| `MicroPositive` | **Ty posíláš** → pozitivní mikro-interakce |
| `MicroNegative` | **Ty posíláš** → negativní mikro-interakce |
| `RepairAttempt` | **Ty posíláš** → pokus o opravu vztahu |

#### Memory

| Událost | Kdy |
|---|---|
| `MemoryRecalled` | **Ty posíláš** → vyvolej vzpomínku |
| `MemoryConsolidated` | Automaticky každých 24 h |

---

## Snapshot — čtení stavu postavy

`npc.Snapshot` vrací `EnginesSnapshot` — read-only pohled na stav všech enginů.

```csharp
var snap = npc.Snapshot;

// Fyziologie
var ph = snap.Physiology;
// ph.Energy, ph.SleepDebtHours, ph.Hunger, ph.Thirst, ph.Pain
// ph.ImmuneLoad, ph.BodyTempDelta, ph.Cycle (MenstrualCycleState?)

// Psychologie
var ps = snap.Psychology;
// ps.Valence, ps.Arousal, ps.Dominance, ps.Stress
// ps.CognitiveLoad, ps.DominantEmotion

// Behavior
var bh = snap.Behavior;
// bh.CurrentPlan?.Name, bh.NeedRest, bh.NeedFood, bh.NeedWater
// bh.NeedBelonging, bh.NeedCompetence, bh.NeedIntimacy
// bh.WaitingForSleepConfirmation, bh.SleepDeclineCount

// Interactions
var is_ = snap.InteractionSurface;
// is_.Location, is_.HasPrivacy, is_.Noise, is_.Crowding

// Relationships
var rs = snap.Relationships;
// rs.Edges: IReadOnlyDictionary<HumanId, RelationshipEdge>

// Memory
var mem = snap.Memory;
// mem.Episodes: IReadOnlyList<EpisodicMemory>
// mem.Semantics: IReadOnlyDictionary<string, SemanticFact>
```

### `npc.LastOutbox`

Po každém `Tick()` je dostupný seznam událostí, které postava v tom tiku vygenerovala:

```csharp
npc.Tick(now, dt);

foreach (var ev in npc.LastOutbox)
{
    switch (ev)
    {
        case SleepPromptRequested spr:
            Console.WriteLine($"Postava chce spát! NeedRest: {spr.NeedRest:F1}");
            // Tady zareaguj: pošli SleepConfirmed nebo SleepDeclined
            break;

        case EmotionShifted es:
            Console.WriteLine($"Emoce se změnila na: {es.To}");
            break;

        case ActionCommitted ac:
            Console.WriteLine($"Postava dělá: {ac.ActionName}");
            break;
    }
}
```

---

## Konfigurace

Soubory `appsettings.json` (nebo `appsettings.Characters.json`) konfigurují všechny enginy.

```json
{
  "Characters": {
    "Physiology": {
      "RestingMetabolicRate": 1600.0,
      "MaxSleepDebtHours": 12.0,
      "EnableMenstrualCycle": true
    },
    "MenstrualCycle": {
      "MeanCycleLengthDays": 28,
      "VariabilityDaysStdDev": 2.0,
      "MensesMeanDays": 5,
      "PmsRisk": 0.35,
      "EnableOvulationWindowEvents": true,
      "EnableSymptoms": true
    },
    "Psychology": {
      "BaselineAffectVariance": 0.02,
      "StressRecoveryRatePerHour": 1.5,
      "SleepQualityAffectWeight": 0.5
    },
    "Behavior": {
      "InertiaWeight": 0.25,
      "NoveltyPenalty": 0.1,
      "PlanningHorizonHours": 2.0,
      "BaseSleepHours": 8,
      "MinSleepHours": 4,
      "MaxSleepHours": 12,
      "SleepCooldownHours": 16
    },
    "Sleep": {
      "SleepPromptThreshold": 70.0,
      "SleepGraceHours": 4.0,
      "MaxDeclineCount": 3,
      "DeclinePenaltyStressPerHour": 2.0,
      "FallingDurationHours": 0.25,
      "LightDurationHours": 0.75,
      "DeepDurationHours": 2.5,
      "RemDurationHours": 1.5,
      "AmbushBaseChancePerHour": 0.03,
      "CompanionGuardModifier": 0.4,
      "NightmareChanceHighStress": 0.25,
      "NightmareChanceNormal": 0.05
    },
    "Interactions": {
      "MisattributionRateBase": 0.15
    },
    "Relationships": {
      "DecayPerDay": 1.5,
      "RepairGain": 6.0,
      "RupturePenalty": 8.0
    },
    "Memory": {
      "BaseEncoding": 0.5,
      "SleepConsolidationBoost": 0.12,
      "ForgettingRate": 0.06,
      "PruneThreshols": 0.01
    }
  }
}
```

---

## Fyzická postava

`CharacterBase` (abstraktní) → `NPC` nebo `PC` (playable character).

```csharp
// Vytvoření
var npcCharacter = new NPC(maxHealth: 100, person: npc);
var pcCharacter  = new PC(maxHealth: 100, person: player);

// Zdraví
double hp     = npcCharacter.Health;
double maxHp  = npcCharacter.MaxHealth;

npcCharacter.DecreaseHealth(25.0);   // útok, poranění
npcCharacter.IncreaseHealth(10.0);   // léčení

// Zbroj
npcCharacter.Armor = someArmorSet;
double protection  = npcCharacter.Protection;

// Zbraň
npcCharacter.Weapon = someSword;

// Person (IHuman)
var personName = npcCharacter.Person.Identity.FirstName;
```

---

## Armory

Zbraně a brnění se načítají z souborů při `manager.Initialize()`.

```csharp
// Přístup po Initialize
var weapons    = (List<Weapon>)manager.Items[typeof(Weapon)];
var armorParts = (List<ArmorPart>)manager.Items[typeof(ArmorPart)];

// Přiřazení zbraně postavě
npcCharacter.Weapon = weapons.First();

// Přiřazení brnění (ArmorSet obaluje ArmorPart kolekci)
npcCharacter.Armor = new ArmorSet(selectedArmorParts);
double totalProtection = npcCharacter.Protection; // z ArmorSet.Protection
```

---

## Tipy a časté chyby

### ✅ Správně

```csharp
// Vždy await using — dispose uvolní DI kontejner
await using var runtime = await GameEngineToolsRuntime.StartAsync();

// Čti Snapshot PO Tick, ne před
npc.Tick(now, dt);
var state = npc.Snapshot;   // ✅ aktuální stav

// Posílej události přes ReceiveEvent (ne přímé volání enginů)
npc.ReceiveEvent(new MicroPositive(...));
```

### ❌ Chyby

```csharp
// ❌ Nikdy nepiš do Snapshot přímo — je read-only
npc.Snapshot.Physiology = ...;   // chyba

// ❌ Nevolej Tick bez správného dt — nulový dt způsobí nulové drifty
npc.Tick(now, WTimeSpan.Zero);   // fyzio se nepohne

// ❌ Nezapomeň na SleepPromptRequested v LastOutbox
// Pokud engine vyšle prompt a ty na něj neodpovíš,
// postava zůstane navždy v WaitingForSleepConfirmation = true!
```

### Doporučená velikost ticku

| Scénář | Doporučený dt |
|---|---|
| Detailní simulace | 15–30 minut |
| Standardní hra | 1 hodina |
| Rychlé přeskočení | 4–8 hodin (spánek) |
| Testování | libovolné |

### Jak restorovat stav po načtení uložené hry

```csharp
// Načti uložený EnginesSnapshot (ze JSON nebo jiného formátu)
var savedSnapshot = LoadFromSave();

// Obnov stav postavy
npc.RestoreSnapshot(savedSnapshot);
// Všechny enginy se resetují na uložené hodnoty
```

---

*GameEngineTools · 50PSoftware · Vigilia Insectianis · © 2026*
