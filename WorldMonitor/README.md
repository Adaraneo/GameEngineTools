# WorldMonitor

Runs WorldObserver's world headlessly (no web host, no pacing) and reports how it behaves over time: stress distribution by day and time of day, where characters are and sleep, interactions, pregnancies, deaths.

```
msbuild WorldMonitor/WorldMonitor.csproj -t:Build -p:Configuration=Debug
WorldMonitor/bin/Debug/net8.0/WorldMonitor.exe                      # profile from appsettings.json
WorldMonitor/bin/Debug/net8.0/WorldMonitor.exe --profile Full
WorldMonitor/bin/Debug/net8.0/WorldMonitor.exe --days 30 --characters 8 --tick 64
```

Speed and size come from `appsettings.json` (`WorldMonitor:Profiles`: characters, days, tick and substep minutes; `Quick` is about half a minute to a few minutes, `Full` 5-8 minutes, `Long` much longer). Command-line `--profile`, `--days`, `--characters`, `--couples`, `--tick`, `--substep`, `--out`, `--world-db` and `--barter true` (runs with `Economy:BarterOnly`) and `--seasons true` (turns on the celestial model, so fields get the weather of the year) override it (`Couples` is the number of pairs that start as an established couple sharing a flat). `--world-db FILE` runs the same characters in a ready world (for example one made by WorldGen, to see whether its food and water suffice) instead of the built-in city; it must contain locations, otherwise the city seed is loaded. A tick is observed once per `TickStepMinutes`, so counts of actions and events are sampled, not exhaustive.

While it runs it shows a progress bar with the current world day, elapsed time, an ETA and the latest mean stress (one line per world day when the output is redirected).

Each run writes `report-<profile>-<time>.md`, `samples-<profile>-<time>.csv` and `personality-<profile>-<time>.csv` into `WorldMonitor/reports/` (ignored by git). `WorldMonitor:Thresholds` turns the report into checks (median stress in the second half, share of readings above `ChronicStressLevel` - the median alone hides a stuck minority -, share of sleep with privacy, deaths); the exit code is 2 when one fails, 1 for a configuration error, 0 otherwise.

The personality part (`## Personality`, from `PersonalityReport.cs`) lists every trait of every character: Big Five, attachment, chronotype and communication style, the nine drives, SOI-R and dual-control sexuality, ToM ceiling, comparison orientation, discount rate, regulatory focus, wanting/liking, FFFS and the dark core, plus the Schwartz values, RIASEC interests and self-concept as they stood at the end of the run with the change since the start. It also shows how varied the population is (mean, SD, min, max per trait), which traits stand out per character (z of 1.3 or more), and a table of Pearson correlations between each trait and stress, accepted interactions, relationships, closest bond and invitations tried; with a dozen characters those correlations are hints only. The same data goes into `personality-<profile>-<time>.csv`, one row per character.

The report also lists every character (personality, relationships, accepted/rejected interactions, stress over the second half) with the chronically stressed ones marked, and the circumstances of each death. A character that has died is dropped from the statistics from then on, because the scene keeps passing it with its last state frozen.

Several monitors can run at the same time (each uses about 80-90 MB and one core): every run copies the config and SourceFiles into its own temporary folder, because the engine keeps its world database at a fixed path under the working directory and runs sharing a folder would rewrite each other's world. Report files carry the process id so parallel runs never collide.

Runs are not reproducible: the world and the orchestrator use unseeded random sources, so two runs of one profile differ (a stress mean of 3 or 23 on the last day is within what was seen).

The intimacy part lists outcomes by speech act (invitations split by privacy) and the characters' desire, closeness and invitations tried; a further table lists the closest pairs with the hours they were awake together and how many of those were in a place with privacy. The death log also carries each character's recent plan and place changes, so the cause of a death can be read off the report.

Newborns are counted but not added to the scene, as WorldObserver's own birth handling lives in its hosted service.

The food and water table gives per world day the mean hunger and thirst, the share of readings at 80 or more, and the meals and drinks (committed Eat, EatStored and Drink actions) per character.

The `Year` profile runs 360 days with seasons on and adds a farms table by 30-day period: grain in the food stores and seed bins, unthreshed sheaves, how many
fields are in each stage (fallow, ploughed, sown, ripe, reaped), the jobs done (plough, sow, reap, thresh) and the hunger and meals of that period.
Use it with a world made by `worldgen --farm-people N` for the number of characters you run.
