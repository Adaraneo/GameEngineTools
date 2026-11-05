# WorldMonitor

Runs WorldObserver's world headlessly (no web host, no pacing) and reports how it behaves over time: stress distribution by day and time of day, where characters are and sleep, interactions, pregnancies, deaths.

```
msbuild WorldMonitor/WorldMonitor.csproj -t:Build -p:Configuration=Debug
WorldMonitor/bin/Debug/net8.0/WorldMonitor.exe                      # profile from appsettings.json
WorldMonitor/bin/Debug/net8.0/WorldMonitor.exe --profile Full
WorldMonitor/bin/Debug/net8.0/WorldMonitor.exe --days 30 --characters 8 --tick 64
```

Speed and size come from `appsettings.json` (`WorldMonitor:Profiles`: characters, days, tick and substep minutes; `Quick` is a few minutes, `Full` about ten, `Long` much longer). Command-line `--profile`, `--days`, `--characters`, `--tick`, `--substep` and `--out` override it. A tick is observed once per `TickStepMinutes`, so counts of actions and events are sampled, not exhaustive.

While it runs it shows a progress bar with the current world day, elapsed time, an ETA and the latest mean stress (one line per world day when the output is redirected).

Each run writes `report-<profile>-<time>.md` and `samples-<profile>-<time>.csv` into `WorldMonitor/reports/` (ignored by git). `WorldMonitor:Thresholds` turns the report into checks (median stress in the second half, share of sleep with privacy, deaths); the exit code is 2 when one fails, 1 for a configuration error, 0 otherwise.

Newborns are counted but not added to the scene, as WorldObserver's own birth handling lives in its hosted service.
