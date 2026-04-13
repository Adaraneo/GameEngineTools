# GameEngineTools

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![License](https://img.shields.io/badge/license-Proprietary-red)

> **Copyright © 50PSoftware**  
> C# knihovna pro simulaci vnitřního světa herních postav.

---

## Co to je a proč to vzniklo

Většina her simuluje postavy jako automaty: NPC stojí na místě, čeká na hráče a pak
odehraje předepsanou animaci. Jakmile hráč odejde, NPC zmizí ze světa.

**GameEngineTools** funguje jinak. Každá postava — hráč i NPC — žije vlastním životem
i bez přítomnosti hráče. Má hlad, únavu a stres. Pamatuje si, co se jí stalo.
Buduje nebo ničí vztahy. Rozhoduje se na základě svého vnitřního stavu, ne na základě
skriptovaných triggerů.

Cílem je, aby se postavy chovaly věrohodně — ne jako loutky, ale jako lidé s vnitřním světem.

---

## Jak postava „funguje" uvnitř

Každá postava prochází při každém herním tiknutí pipeline sedmi modulů, které se nazývají **enginy**.
Každý engine má na starosti jeden aspekt existence postavy a předává výsledky dál:

```
Physiology → Psychology → Behavior → Interactions → Relationships → Memory → SemanticMemory
```

### 1. Physiology — tělo

Sleduje fyzický stav: energii, spánkový dluh, hlad, žízeň, bolest, teplotu, imunitní zátěž.
U ženských postav simuluje i menstruační cyklus, který jemně ovlivňuje náladu a arousal.

Fyzický stav přímo ovlivňuje psychiku — vyhládlá nebo unavená postava je podrážděnější,
méně sociabilní, hůře se soustředí.

### 2. Psychology — mysl

Používá [PAD model](https://en.wikipedia.org/wiki/PAD_emotional_state_model) —
tři osy, které dohromady popisují emocionální stav:
- **Valence** — jak příjemně se postava cítí (−1 = bídně, +1 = skvěle)
- **Arousal** — jak je vzrušená nebo ospalá (0 = apatická, 1 = hyperaktivní)
- **Dominance** — jak moc se cítí v kontrole situace

Na to se navrstvuje stres a kognitivní zátěž. Stres roste ze spánkového dluhu,
bolesti a neúspěšných sociálních interakcí. Čas, jídlo a spánek ho postupně snižují.

Neuroticism (součást osobnosti Big Five) určuje, jak rychle stres roste a jak pomalu opadá.

### 3. Behavior — rozhodování

Postava si nevybírá akci náhodně — vybírá si ji přes **utility funkci**.
Každá možná akce dostane skóre na základě aktuálních potřeb postavy:

- Je unavená? → Spánek dostane vysoké skóre.
- Má hlad a zároveň je osaměla? → Eat vs. ReachOut — záleží na tom, co víc tlačí.
- Dělala tu samou věc už dlouho? → Setrvačnostní boost ji v ní podrží, ale kognitivní switching cost ji zpomalí při přepínání kategorií.

Postava může dělat: `Work`, `Create`, `Eat`, `Drink`, `SelfCare`, `ReachOut`, `InviteIntimacy`, `Idle` — nebo spát.

#### Spánek

Spánek je záměrně vyřazen z běžného výběru akcí a řeší se jako separátní session.
Postava prochází fázemi: **Falling asleep → Light sleep → Deep sleep → REM**.
V REM fázi může zažít sen nebo noční můru. Noční můra přeruší spánek a zvýší stres.

Při opakovaném odmítání spánku (hráčem) se zapíná penalizace — stres roste rychleji.
Po dostatečně vysokém spánkovém dluhu engine bypasse cooldown a postava prostě musí spát.

#### Vliv vzpomínek a atraktivity na rozhodování

Behavior engine se při výběru akce ptá dvou modulů:

**Paměť:** Postava si vzpomíná na relevantní zážitky. Pokud zažila sociální trauma → utilita `ReachOut` klesne.
Pokud ji někdo odmítl při intimní iniciativě → `InviteIntimacy` dostane penaltu.
Pozitivní vzpomínky na společný čas → sociální aktivity dostávají bonus.

**Atraktivita:** Přítomnost fyzicky přitažlivé osoby v dohledu zvyšuje motivaci pro `ReachOut` a `InviteIntimacy`.
Absence sexuálního kontaktu zvyšuje **NeedIntimacy**, která posouvá rozhodování k intimitě-orientovaným akcím.

### 4. Interactions — sociální kontakt

Když se postava A pokusí o interakci s B, engine B vyhodnotí, jestli ji přijme nebo odmítne.
Záleží na: jak blízký je jejich vztah, jaká je nálada B, jestli je přeplněno nebo hlučno,
jestli mají soukromí, a na misattribution — stres způsobuje chybné čtení záměrů.

Typy interakcí (`SpeechAct`): `SmallTalk`, `Question`, `SelfDisclosure`, `Validation`,
`Humor`, `Meta`, `Invite`, `Boundary`.

Existuje i fyzický kontakt (`TouchAttempted`): Light, Friendly, Intimate — každá úroveň
vyžaduje odpovídající hloubku vztahu a soukromí.

### 5. Relationships — vztahy

Vztahy jsou **asymetrické** — A může mít B ráda víc, než B má ráda A.
Každá postava vede orientovaný graf: pro každou osobu, se kterou se setkala, drží hranu
se šesti dimenzemi:

| Dimenze | Co měří |
|---|---|
| Like | Jak moc ji má ráda celkově |
| Trust | Jak moc jí věří |
| Attraction | Fyzická přitažlivost |
| Closeness | Emocionální intimita |
| Respect | Respekt k hodnotám a schopnostem |
| Comfort | Jak příjemně se s ní cítí |

Kromě toho vede **DomainBreakdown** — granulární obraz ČEHO si na druhém váží:
intelekt, humor, estetika, hodnoty, fyzičnost. Každá interakce posouvá konkrétní domény.

Vztahy časem **decay** — bez kontaktu se pomalu blíží k neutrálním hodnotám.

**První dojem (Attraction engine):** Když se dvě postavy setkají poprvé, spočítá se jejich
**fyzická přitažlivost** na základě `AttractionProfile` (preference k rysom tváře, postavě, věku, specifickým morfologickým znakům)
a **vnímané fyzičnosti** v danou chvíli (únava, stav zdraví, emociální stav). Tento první dojem
ovlivní iniciální hodnoty dimenzí vztahu.

### 6. Memory — paměť

Paměťový engine modeluje tři kognitivní principy:

**Ebbinghausova křivka zapomínání** — vzpomínky se nerozpadají lineárně,
ale exponenciálně. Čerstvá vzpomínka ztrácí sílu rychleji než ta dobře zakotvená.
Negativní vzpomínky (stres, odmítnutí) se rozpadají *pomaleji* než pozitivní.

**Spánková konsolidace** — po probuzení engine posílí nejsalientnější vzpomínky.
Nedostatečný spánek tak přímo narušuje paměť — přesně jako v reálném životě.

**Spacing effect** — opakovaný zážitek stejného druhu nevytváří duplicitní záznam,
ale posiluje stávající vzpomínku. Postava, která se opakovaně setkává s příjemnou osobou,
má tuto osobu čím dál hlouběji zakořeněnou v paměti.

### 7. SemanticMemory — sémantické rozhodování

Dodatečný engine pro **sociální targeting a rozhodování** nad hrubými možnostmi.
Udržuje abstraktní znalosti o jednotlivcích: "Kdo je můj nejbližší?", "Kdo by se mi jist neusmál?",
"Koho bych měl vyhledat pro zábavu?"

SemanticMemory analyzuje síť vztahů a vzpomínek, aby postavě doporučil, s kým
a jak být sociálně aktivní, s ohledem na její psychologický stav a historii.

---

## Lokace a vnímání

Svět se skládá z **lokací** (`LocationDescriptor`), které mají atributy:
- **Hluk** — baseline šum + přispění za osobu
- **Kapacita** — max osob na místě
- **Soukromí** — jestli se dá dosáhnout intimní komunikace
- **Typ** — Social, Work, Rest, Private, Public

Postavy se pohybují mezi lokacemi na základě své potřeby (hledají klid na odpočinek,
nebo akci v sociální oblasti). **CharacterPerceptionResolver** filtruje, které postavy
se navzájem vidí — např. hlučná lokace či nízká fidelita snižuje vnímání.

---

## Herní čas

Svět nepoužívá reálný `DateTime`. Místo toho běží vlastní herní čas definovaný přes
`WorldTimeSpec` — kalendář s libovolným počtem měsíců, dní v měsíci a hodin v dni.

Aktuální konfigurace: **10 měsíců × 36 dní, 26 hodin denně** (svět Vigilia Insectianis).

Čas je plně konfigurovatelný a nezávislý na reálném čase — simulace může běžet
stokrát rychleji než realita nebo v přesném lockstepu s ní.

---

## Narativní výstup

Engine sám o sobě generuje jen doménové události (interakce proběhla, vzpomínka uložena,
postava usnula…). K tomu existuje **Narrative vrstva**: formatter, který přeloží tyto
technické události na čitelný text.

Výchozí implementace `DefaultNarrativeFormatter` píše v češtině a využívá vlastní
morfologickou knihovnu **Grammar Modular** pro správné skloňování jmen, sloves a rodů.

Každá narativní věta má prioritu — High, Medium nebo Low — takže si hráč může
nastavit, co chce vidět: jen klíčové momenty, nebo celý proud vědomí postavy.

---

## Nástrojová sada (Tooling)

### CharacterGenerator

Samostatná konzolová aplikace pro interaktivní generování postav:

```csharp
// Spuštění:
// CharacterGenerator.exe

// Interaktivní kroky:
// 1. Pokusy o import existujících postav; pokud neexistují, vytvoří adresářovou strukturu
// 2. Generuje hráče (PC), jejich SO a přítele + jeho SO
// 3. Nabídne možnost vygenerovat další NPCs (počet zadá uživatel)
// 4. Exportuje všechny postavy do JSON
// 5. Vyexportuje info .txt soubory a portréty pro AI image-gen prompty
```

### GameSandbox

Plně vybavená sandbox aplikace pro spuštění komplexní simulace:

```csharp
// Spuštění:
// GameSandbox.exe

// Interaktivní kroky:
// 1. Ptá se, jestli exportovat prompty po skončení
// 2. Ptá se, jestli exportovat info hráče a SO
// 3. Ptá se, jestli exportovat deník
// 4. Ptá se, kolik let simulovat
// 5. Spouští scénu s hlavními postavami (PC, SO, přítel, SO přítele)
// 6. Spouští druhou scénu s dalšími NPC v pozadí
// 7. Exportuje výsledky na plochu
```

Klíčové rysy:
- **Dvě simulační scény**: hlavní 4-postavy (plná fidelita) + background NPC (5 let v pozadí)
- **Per-tick hooks**: `FireFirstImpressions`, `RouteMoveTo`, `DynamicReachOutRouting`, `OrganicMicroPositives`
- **Lokace**: vesnice, hrad, chlévy, kováčna, les — všechny s atributy hluku/kapacity/privacy
- **Persistence**: čas se ukládá do `gametime.bin` na ploše pro obnovení při příštím spuštění
- **Exporty**: deník, info, prompty

### LogsResolver

WPF aplikace pro analýzu výstupu engine:

Načítá JSONL logy (`logs/Characters/Characters.jsonl` a scoped soubory):
- **Session Summary** — přehled osob, subsystémů, souborů
- **Events Explorer** — filtrování a vyhledávání v rámci 1000+ loggovaných záznamů
- **Event Details** — detailní pohled na konkrétní návrh, řešení, vztah
- **Character Timeline** — vizuální časová osa pro NPC: kdy se narodil, kdy se s kým potkal, atd.
- **Diagnostics** — log integrity checks, race conditions, duplicitní event IDs
- **Raw File Viewer** — čtení surového JSONL obsahu
- **NPC Character Loader** — import JSON exportů postavy pro referenci v timelinech

---

## Jak to zapojit do hry

Knihovna je navržena tak, aby šla použít ve dvou scénářích:

### Konzolová aplikace / standalone

```csharp
// Spuštění runtime bez parametru startTime — čas se nastaví později
await using var runtime = await GameEngineToolsRuntime.StartAsync(
    consoleLogs: false,
    logsRoot: "logs",
    generatedFileOptions: new GeneratedFileOptions
    {
        PlayerDirectory = "generated/player",
        NPCDirectory = "generated/npcs"
    });

var clock = runtime.Clock;
var manager = runtime.GameEngineToolsManager;

// Obnovení uloženého času (např. ze souboru)
if (long.TryParse(File.ReadAllText("gametime.bin"), out var savedTicks))
{
    clock.SetNow(new WDateTime(savedTicks));
}

var scene = new SimulationScene(clock, new SimulationSceneOptions
{
    Characters      = [playerHuman, npcHuman],
    SimulationYears = 2,
    TickStep        = WTimeSpan.FromHours(0.5),
    LocationService = locationService,
    DefaultCharacterLod = CognitiveResolutionLevel.Foreground,
    
    OnNarrative = entry =>
    {
        if (entry.Priority == NarrativePriority.High)
            Console.WriteLine(entry.Text);
    }
});

await scene.RunAsync();
```

### Unity

Pro Unity existuje VContainer adaptér — díky Adapter patternu fungují všechny
registrační metody beze změny. Místo `IHost` (který Unity nepotřebuje) se engine
bootstrapuje přes `ServiceCollection` přímo.

```csharp
var services = new ServiceCollection();
// ... register all GameEngineTools services ...
var provider = services.BuildServiceProvider();
WWorld.Configure(spec, clock);
```

---

## Persistence — ukládání postav

Postava se serializuje do JSON a načítá zpátky s plným zachováním stavu —
vzpomínky, vztahy, fyzický stav, osobnost, atraktivita. Herní čas se ukládá zvlášť,
takže simulace při příštím spuštění pokračuje přesně tam, kde skončila.

Exporty (`GeneratedFile.Export()` a `GeneratedFile.ExportNPPCs()`) uloží:
- `Characters/{PersonId}.json` — kompletní stav postavy
- Doprovodné soubory: portrait prompts, npc info soubory

---

## Struktura řešení

| Projekt | Typ | Účel |
|---|---|---|
| `GameEngineTools` | Library (.NET 8) | Jádro enginu — všechna simulační logika |
| `CharacterGenerator` | Console App | Interaktivní generátor postav |
| `GameSandbox` | Console App | Sandbox pro spuštění komplexních simulací |
| `EngineTests` | MSTest | Unit a integrační testy |
| `LogsResolver` | WPF App | JSONL log viewer a diagnostika |
| `LogsResolverTests` | MSTest | Testy pro LogsResolver |
| `RelationshipsGame` | WPF App | Early-stage prototype |

---

## Kam projekt míří

**GameEngineTools** je základ pro **Vigilia Insectianis** — RPG s přežitím a narativními prvky
zasazené do světa se dvěma civilizacemi: feudální lidé a Insectiani — 3mm telepatické bytosti
žijící v symbióze s mravenci.

Zároveň je knihovna navržena jako kandidát pro samostatnou distribuci:
vývojáři, kteří chtějí do své hry vnést věrohodné chování NPC, si ji mohou
vzít jako hotový základ a zaměřit se na herní obsah místo simulační infrastruktury.

---

## Technické informace

| | |
|---|---|
| Jazyk | C# |
| Framework | .NET 8 |
| DI | Microsoft.Extensions.DependencyInjection |
| Unity DI | VContainer (adaptér) |
| Morfologie | 50PSoftware.GrammarModular.Czech (vlastní balíček) |
| Logování | `[LoggerMessage]` source generator (nulové alokace) + vlastní `CharactersFileLogger` |
| Testy | MSTest |
| Nástroje | CharacterGenerator, GameSandbox, LogsResolver (WPF) |

---

*GameEngineTools — 50PSoftware*
