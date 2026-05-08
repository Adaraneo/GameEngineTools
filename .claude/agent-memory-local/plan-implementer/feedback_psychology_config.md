---
name: PsychologyConfig parameterless constructor misalignment
description: The parameterless PsychologyConfig() constructor positional args may be misaligned with named parameter defaults when new params are added to the record
type: feedback
---

When writing tests that rely on specific PsychologyConfig field values (e.g. EmotionDecayFear, EmotionDecaySadness), do NOT use `new PsychologyConfig()` and then read fields — the parameterless constructor hardcodes a positional list that can drift from the named defaults when new parameters are added to the record.

**Why:** `new PsychologyConfig()` uses `: this(0.02, 1.5, ...)` with 93 positional args. If any new parameter is added to the middle of the record, all subsequent positional args shift. The test expected Fear=3.0 but got Fear=0.7 (position mapped to Tenderness instead).

**How to apply:** Always use named parameters when constructing PsychologyConfig for tests that check specific field values:
```csharp
var cfg = new PsychologyConfig(EmotionDecayFear: 3.0, EmotionDecaySadness: 0.06);
```
