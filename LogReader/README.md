# GET Log Reader

ASP.NET Core viewer for GameEngineTools simulation logs. It streams JSONL logs (multi-GB is fine), parses them into per-character data and serves a Bootstrap + Chart.js single-page UI from `wwwroot/`. No Node.js and no Docker.

## Run

Copy `appsettings.Default.json` to `appsettings.json` (git-ignored, local paths) and fill in the `LogReader` paths, then:

```bash
"/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe" LogReader/LogReader.csproj -restore -t:Build -p:Configuration=Debug -verbosity:quiet
cd LogReader/bin/Debug/net8.0 && ./LogReader.exe
```

Open http://localhost:3000 (change it with `Urls` in `appsettings.json`).

| Key | Meaning |
|-----|---------|
| `LogRoot` | Log directory with `Person/<uuid>/*.jsonl` and/or `Characters.jsonl` (required) |
| `NpcRoot` | Directory scanned recursively for NPC JSON files (required) |
| `WorldDbPath` | Optional engine `world.db`; powers the Map tab |
| `CacheTtlMs` | In-memory cache TTL (snapshot-derived data lives 6 h regardless) |
| `LoadBatchSize` | Characters per batch during load-all (RAM vs. I/O) |
| `Calendar` | World calendar sent to the UI (Vigilia Insectianis by default) |

## Layout

- `Program.cs` — endpoints (`/api/...`), static files, SSE for `load-all` and per-character `load`.
- `Parsing/` — one parser per log domain; each returns a `JsonObject` whose shape is the contract with the UI.
- `Services/` — `LogStore` (cache, run detection, cold-path builders), `CharAccumulator` (single-pass accumulator), `LogFiles` (streaming), `LogIndex` (SQLite accumulator state for incremental load-all), `NpcLoader`, `WorldDb`.
- `wwwroot/` — the UI (`js/core`, `js/views`, `vendor/` holds the browser libraries).
- `src/eventRegistry.json` — generated from `CoreLog.cs` by `EngineTests/EventRegistryTests.cs`; copied next to the executable.

## Behaviour notes

- Snapshot events (5000/5100/5200 …) live only in the global `Characters.jsonl`; run **load-all** once (the UI does it on start) to fill the charts. Later runs read only appended bytes.
- When the logs are deleted and regenerated, the server notices (`X-Run-Stamp`), flushes caches and the UI offers a reload.
- Accumulator state is persisted in `.cache/log-index.db` next to the content root.
