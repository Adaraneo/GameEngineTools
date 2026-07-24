// WorldHostedService.cs
// Copyright (c) 50PSoftware

namespace WorldObserver.Simulation
{
    using System.IO;
    using System.Linq;
    using GameEngineTools;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Bereavement;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.GameObjects;
    using GameEngineTools.Characters.Generation;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.FileSystem;
    using GameEngineTools.Narrative;
    using GameEngineTools.World.Simulation;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.AspNetCore.SignalR;
    using Microsoft.Extensions.DependencyInjection;
    using WorldObserver.Dtos;
    using WorldObserver.Hubs;

    /// <summary>
    /// Hosts the engine and drives the world in realtime. Runs the (synchronous) <see cref="SimulationScene"/>
    /// loop on a dedicated background thread; pacing, pausing and state-push all happen from the scene's
    /// <c>OnTick</c> / <c>OnNarrative</c> hooks.
    /// </summary>
    public sealed class WorldHostedService : BackgroundService
    {
        /// <summary>How long the world may run before the bounded scene loop ends (effectively forever).</summary>
        private const long SimulationDays = 3_650_000; // ~10 000 years

        /// <summary>Minimum wall-clock gap between two world-state pushes, to avoid flooding the socket.</summary>
        private static readonly TimeSpan PushInterval = TimeSpan.FromMilliseconds(200);

        private readonly IHubContext<WorldHub> _hub;
        private readonly SimulationControl _control;
        private readonly ILogger<WorldHostedService> _log;
        private readonly WorldObserverOptions _options;
        private readonly CharacterPort _port;
        private WDateTime _startTime; // world clock captured at launch
        private long _realStartTicks; // Environment.TickCount64 at launch — basis for real wall-clock elapsed
        private volatile IReadOnlyList<IHuman>? _liveChars; // latest per-tick character list (for export)

        /// <summary>Per-character movement trail (oldest → newest distinct locations), updated every tick.</summary>
        private readonly Dictionary<HumanId, List<string>> _trails = new();

        /// <summary>Rolling per-character conversation log (last few utterances said + heard).</summary>
        private readonly Dictionary<HumanId, List<WorldObserver.Dtos.DialogueLineDto>> _dialogueLog = new();

        /// <summary>Last interaction event recorded per speaker — dedups a lingering outbox.</summary>
        private readonly Dictionary<HumanId, object> _lastRecordedAct = new();

        private long _lastPushTicks;

        /// <summary>Creates the service with the SignalR hub context, control state and options.</summary>
        public WorldHostedService(
            IHubContext<WorldHub> hub,
            SimulationControl control,
            ILogger<WorldHostedService> log,
            Microsoft.Extensions.Options.IOptions<WorldObserverOptions> options,
            CharacterPort port)
        {
            _hub = hub;
            _control = control;
            _log = log;
            _options = options.Value;
            _port = port;
        }

        /// <inheritdoc/>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _log.LogInformation("Starting GameEngineTools runtime…");

            // The world SQLite database self-seeds at SourceFiles\World\world.db relative to the working
            // directory, but its parent directory must exist first or Open() fails (SQLite 14).
            var worldDir = Path.Combine(Directory.GetCurrentDirectory(), "SourceFiles", "World");
            Directory.CreateDirectory(worldDir);
            // Start each run from a fresh DB: the bootstrap inserts its own modern locations, and a
            // persisted DB would accumulate them / collide across runs. This world is ephemeral anyway.
            foreach (var f in new[] { "world.db", "world.db-wal", "world.db-shm" })
            {
                try { File.Delete(Path.Combine(worldDir, f)); } catch (IOException) { /* in use — leave it */ }
            }

            await using var runtime = await GameEngineToolsRuntime.StartAsync(
                consoleLogs: false,
                writeJsonLines: false,
                writeTextLogs: false);

            var characterCount = Math.Max(1, _options.CharacterCount);
            var ctx = WorldBootstrap.Build(runtime, characterCount);
            _startTime = ctx.Clock.Now; // world clock at launch — basis for game "elapsed since start"
            _realStartTicks = Environment.TickCount64; // wall-clock basis for real run time
            _log.LogInformation("World ready: {Count} characters.", ctx.Characters.Count);

            // Character export/import (folder chosen in the browser; import applied on the sim thread).
            var genFile = (GeneratedFile)runtime.Services.GetRequiredService<IGeneratedFile>();
            _liveChars = ctx.Characters;
            // Export bundles the current world clock (portable WorldTicks) with the characters.
            _port.Configure(() => new ExportBundleDto(ctx.Clock.Now.WorldTicks, ExportLiveCharacters(genFile)));

            // Mutable so newborns can be added to the narrative resolver as they are born.
            var names = ctx.Characters.ToDictionary(
                c => c.Id,
                c => new NarrativeCharacterInfo(c.Identity.FirstName.Original, c.Biology));

            // Captured by OnTick; assigned right after the options are built (before RunAsync).
            SimulationScene scene = null!;

            var options = new SimulationSceneOptions
            {
                Characters = ctx.Characters,
                LocationService = ctx.Locations,
                SimulationDays = SimulationDays,
                TickStep = WTimeSpan.FromMinutes(Math.Max(1, _options.TickStepMinutes)),
                InternalSubstep = WTimeSpan.FromMinutes(15),
                // World tempo is live-adjustable from the browser (game-minutes advanced per tick).
                TickStepProvider = () => WTimeSpan.FromMinutes(_control.SimMinutesPerTick),
                DefaultCharacterLod = GameEngineTools.Characters.Hosting.CognitiveResolutionLevel.Nearby,
                ObjectSnapshotCache = ctx.ObjectCache,
                WriteBuffer = ctx.WriteBuffer,
                RespawnScheduler = ctx.Respawn,
                NarrativeFormatter = new WorldObserver.Narrative.WorldObserverNarrativeFormatter(),
                ResolveCharacter = id => names.TryGetValue(id, out var info)
                    ? info
                    : new NarrativeCharacterInfo(id.Value.ToString()[..8], SexBiology.Unknown),

                OnTick = (now, chars) =>
                {
                    // (1) Pace / pause / single-step. Throws on shutdown to unwind the loop.
                    _control.WaitForTurn(stoppingToken);

                    // (2) Drive perception, first impressions and movement routing.
                    ctx.Orchestrator.OnTick(now, chars);

                    // (3) Wire any newborns into the live world.
                    HandleBirths(now, chars, runtime.Services, ctx, scene, names);

                    // (3b) Track the live roster (for export) and apply any queued character imports.
                    _liveChars = chars;
                    if (_port.TryTakeImportBatch(out var importFiles, out var replace, out var worldTimeTicks))
                    {
                        if (replace) ResetWorld(ctx, scene, names);

                        // Restore the saved world clock on a replace (world is restored, not reloaded).
                        if (replace && worldTimeTicks > 0)
                        {
                            ctx.Clock.SetNow(new WDateTime(worldTimeTicks));
                            _startTime = ctx.Clock.Now; // re-baseline the "elapsed since start" readout
                            _log.LogInformation("World clock restored to {Time}.", ctx.Clock.Now);
                        }

                        var batch = new HashSet<HumanId>();
                        foreach (var f in importFiles)
                        {
                            try { ApplyImport(f, genFile, ctx, scene, names, replace, batch); }
                            catch (Exception ex) { _log.LogWarning(ex, "Import of a character failed."); }
                        }
                    }

                    // (3c) Push bereavement domain events as Czech narrative lines.
                    PushBereavementNarrative(now, chars, names);

                    // (4) Record movement trails + conversation lines every tick (push is throttled).
                    UpdateTrails(chars);
                    RecordDialogue(now, chars);

                    // (5) Throttled world-state push.
                    PushStateThrottled(now, chars, ctx);
                },

                OnNarrative = entry => PushNarrative(entry, names),
            };

            scene = new SimulationScene(ctx.Clock, options, ctx.Lod);

            try
            {
                await Task.Run(() =>
                {
                    try { scene.RunAsync().GetAwaiter().GetResult(); }
                    catch (OperationCanceledException) { /* expected on shutdown */ }
                }, CancellationToken.None);
            }
            catch (OperationCanceledException) { /* also fine */ }

            _log.LogInformation("Simulation loop ended.");
        }

        /// <summary>Formats a real (wall-clock) duration in milliseconds as "Xh Ym Zs" (or "Ym Zs").</summary>
        private static string FormatRealElapsed(long ms)
        {
            if (ms < 0) ms = 0;
            var total = ms / 1000;
            var h = total / 3600;
            var m = (total % 3600) / 60;
            var s = total % 60;
            return h > 0 ? $"{h}h {m}m {s}s" : $"{m}m {s}s";
        }

        private void PushStateThrottled(WDateTime now, IReadOnlyList<IHuman> chars, WorldContext ctx)
        {
            var nowTicks = Environment.TickCount64;
            var last = Interlocked.Read(ref _lastPushTicks);
            if (nowTicks - last < PushInterval.TotalMilliseconds)
                return;

            Interlocked.Exchange(ref _lastPushTicks, nowTicks);

            var dto = WorldStateProjector.Project(
                now, chars, ctx.Locations, _control, _trails,
                travelDestinationOf: id =>
                {
                    var dest = ctx.Orchestrator.GetTravelDestination(id);
                    return dest is null ? null : (ctx.Locations.GetDescriptor(dest)?.DisplayName ?? dest);
                },
                startTime: _startTime,
                realElapsed: FormatRealElapsed(Environment.TickCount64 - _realStartTicks),
                mapLocationIds: ctx.KnownLocations,
                mapConnections: ctx.Connections,
                transitOf: id => ctx.Orchestrator.GetTransit(id, now),
                regions: ctx.Regions,
                objectProvider: ctx.ObjectCache,
                statusLedger: ctx.StatusLedger,
                // Per-push copy so SendAsync serialization is not racing the sim thread's appends.
                recentDialogue: _dialogueLog.ToDictionary(
                    kv => kv.Key, kv => (IReadOnlyList<WorldObserver.Dtos.DialogueLineDto>)kv.Value.ToArray()));
            _ = _hub.Clients.All.SendAsync("Tick", dto);
        }

        /// <summary>
        /// Appends each character's current location to its trail when it changes (deduped), keeping the
        /// most recent <c>TrailLength</c> entries.
        /// </summary>
        private void UpdateTrails(IReadOnlyList<IHuman> chars)
        {
            var trailLength = Math.Max(1, _options.TrailLength);

            foreach (var c in chars)
            {
                var loc = c.Snapshot.InteractionSurface.Location;
                if (string.IsNullOrEmpty(loc) || loc == "Unknown")
                    continue;

                if (!_trails.TryGetValue(c.Id, out var trail))
                {
                    trail = new List<string>(trailLength);
                    _trails[c.Id] = trail;
                }

                if (trail.Count == 0 || trail[^1] != loc)
                {
                    trail.Add(loc);
                    while (trail.Count > trailLength)
                        trail.RemoveAt(0);
                }
            }
        }

        /// <summary>
        /// Appends this tick's uttered dialogue to a rolling per-character log — one line on the
        /// speaker's side (outgoing) and one on the addressee's (incoming), both carrying the same
        /// TEMPORARY mode-2 direct-speech gloss. Only fresh acts (this tick, initiated by the speaker)
        /// are recorded; the last few lines per character are kept.
        /// </summary>
        private void RecordDialogue(WDateTime now, IReadOnlyList<IHuman> chars)
        {
            const int MaxLines = 12;
            var byId = chars.ToDictionary(c => c.Id, c => c);
            var timeStr = $"{now.Day}. {now.Hour}h";

            foreach (var c in chars)
            {
                var outbox = c.LastOutbox;
                for (var i = 0; i < outbox.Count; i++)
                {
                    if (outbox[i] is not GameEngineTools.Characters.Engines.Interactions.InteractionProposed ip)
                        continue;
                    if (ip.From != c.Id)
                        continue;
                    // Dedup a lingering outbox (same event object across ticks) without a timestamp filter.
                    if (_lastRecordedAct.TryGetValue(c.Id, out var prev) && ReferenceEquals(prev, ip))
                        continue;
                    _lastRecordedAct[c.Id] = ip;
                    if (!byId.TryGetValue(ip.To, out var target))
                        continue;

                    var text = WorldStateProjector.RealizeUtterance(ip.Content.SpeechAct, c, target);
                    if (string.IsNullOrEmpty(text))
                        continue;

                    AppendDialogue(c.Id, new WorldObserver.Dtos.DialogueLineDto(timeStr, true, target.Identity.FirstName.Original, text), MaxLines);
                    AppendDialogue(ip.To, new WorldObserver.Dtos.DialogueLineDto(timeStr, false, c.Identity.FirstName.Original, text), MaxLines);
                }
            }
        }

        private void AppendDialogue(HumanId id, WorldObserver.Dtos.DialogueLineDto line, int cap)
        {
            if (!_dialogueLog.TryGetValue(id, out var list))
            {
                list = new List<WorldObserver.Dtos.DialogueLineDto>(cap);
                _dialogueLog[id] = list;
            }

            list.Add(line);
            while (list.Count > cap)
                list.RemoveAt(0);
        }

        /// <summary>
        /// Scans every character's outbox for bereavement domain events and pushes them to the
        /// narrative feed as Czech-language lines. Called each tick from <c>OnTick</c>.
        /// </summary>
        private void PushBereavementNarrative(
            WDateTime now,
            IReadOnlyList<IHuman> chars,
            IReadOnlyDictionary<HumanId, NarrativeCharacterInfo> names)
        {
            string ResolveName(HumanId id)
                => names.TryGetValue(id, out var info) ? info.Name : id.Value.ToString()[..8];

            foreach (var c in chars)
            {
                foreach (var ev in c.LastOutbox)
                {
                    string? text = null;
                    string priority = "Medium";

                    switch (ev)
                    {
                        case BereavementOnset onset:
                            text = $"{ResolveName(onset.Human)} truchlí za {ResolveName(onset.Deceased)} (síla pouta {onset.BondStrength:F0}, příčina: {onset.Cause})";
                            priority = "High";
                            break;
                        case GriefTrajectoryAssigned traj:
                            text = $"{ResolveName(traj.Human)}: přiřazena trajektorie žalu — {traj.Trajectory}";
                            break;
                        case FuneralHeld funeral:
                            text = $"Pohřeb: {ResolveName(funeral.Human)} pohřbil(a) {ResolveName(funeral.Deceased)} ({funeral.Attendees} účastníků)";
                            priority = "High";
                            break;
                        case Buried burial:
                            text = $"{ResolveName(burial.Human)} pohřbil(a) {ResolveName(burial.Deceased)}";
                            priority = "High";
                            break;
                        case GraveVisited visit:
                            text = $"{ResolveName(visit.Human)} navštívil(a) hrob {ResolveName(visit.Deceased)}";
                            break;
                    }

                    if (text is not null)
                    {
                        var dto = new NarrativeDto(now.ToString(), ResolveName(c.Id), text, priority);
                        _ = _hub.Clients.All.SendAsync("Narrative", dto);
                    }
                }
            }
        }

        private void PushNarrative(NarrativeEntry entry, IReadOnlyDictionary<HumanId, NarrativeCharacterInfo> names)
        {
            var subject = names.TryGetValue(entry.Subject, out var info)
                ? info.Name
                : entry.Subject.Value.ToString()[..8];

            var dto = new NarrativeDto(entry.OccurredAt.ToString(), subject, entry.Text, entry.Priority.ToString());
            _ = _hub.Clients.All.SendAsync("Narrative", dto);
        }

        /// <summary>
        /// Scans this tick's outboxes for <see cref="ChildBorn"/> and brings each newborn into the live
        /// world: generates it from both parents, wires family bonds, places it at the mother's location,
        /// registers its name for narration, and adds it to the running scene. Needs both parents present.
        /// </summary>
        /// <summary>Serializes the current live characters to CharacterData JSON (one entry each).</summary>
        private List<CharacterFileDto> ExportLiveCharacters(GeneratedFile genFile)
        {
            var chars = _liveChars;
            var list = new List<CharacterFileDto>();
            if (chars is null) return list;

            // data/npcs is used ONLY as a scratch dir: each character is written, read back into the
            // returned bundle, and its file deleted immediately — nothing is persisted or depended on
            // (the whole dir may be deleted/overwritten between runs; it is re-created on demand).
            Directory.CreateDirectory(genFile.NPCDirectory);

            foreach (var person in chars)
            {
                string json;
                var written = string.Empty;
                try
                {
                    // Export writes a CharacterData file into NPCDirectory and returns its name;
                    // read it back from there (the returned value is not a cwd-rooted full path).
                    written = Path.Combine(genFile.NPCDirectory, Path.GetFileName(genFile.Export(new NPC(100, person))));
                    json = File.ReadAllText(written);
                }
                catch (Exception ex) { _log.LogWarning(ex, "Export of {Id} failed.", person.Id.Value); continue; }
                finally { if (written.Length > 0) { try { File.Delete(written); } catch { /* best-effort */ } } }
                // Unique, human-readable file name (id suffix avoids collisions between same-named characters).
                var fileName = Sanitize(person.Identity.FirstName.Original) + "_" + person.Id.Value.ToString()[..8] + ".json";
                list.Add(new CharacterFileDto(fileName, json));
            }
            return list;
        }

        /// <summary>
        /// Clears the entire live world (a "replace" import): removes every current character from the
        /// scene, location service, narrative names and movement trails. The scene actually empties at
        /// the next substep boundary; the world-side cleanup happens here.
        /// </summary>
        private void ResetWorld(WorldContext ctx, SimulationScene scene, IDictionary<HumanId, NarrativeCharacterInfo> names)
        {
            var current = _liveChars;
            if (current is not null)
                foreach (var c in current)
                    ctx.Locations.RemoveCharacter(c.Id);

            scene.ResetCharacters();
            names.Clear();
            _trails.Clear();
            _dialogueLog.Clear();
            _log.LogInformation("World reset for replace-import ({Count} characters removed).", current?.Count ?? 0);
        }

        /// <summary>Reconstructs one character from its CharacterData JSON and adds it to the live world.</summary>
        private bool ApplyImport(
            CharacterFileDto f,
            GeneratedFile genFile,
            WorldContext ctx,
            SimulationScene scene,
            IDictionary<HumanId, NarrativeCharacterInfo> names,
            bool replace,
            HashSet<HumanId> batch)
        {
            // Reuse the engine's own (de)serializer + factory by round-tripping through a temp file.
            var dir = genFile.NPCDirectory;
            Directory.CreateDirectory(dir);
            var tmp = "import_" + Guid.NewGuid().ToString("N") + ".json";
            var tmpPath = Path.Combine(dir, tmp);
            File.WriteAllText(tmpPath, f.Json);

            NPC npc;
            try { npc = genFile.ImportNPC(tmp); }
            finally { try { File.Delete(tmpPath); } catch { /* best-effort cleanup */ } }

            var person = npc.Person;
            if (person is null) return false;

            // Avoid duplicate ids: never import the same id twice in one batch; and (additive import
            // only) skip ids already live. On replace the old world is being cleared, so live ids are ok.
            if (!batch.Add(person.Id)) return false;
            if (!replace && _liveChars?.Any(c => c.Id == person.Id) == true) return false;

            person.FlushInbox();

            // Place at the character's home if it exists in this world, else a sensible default.
            var home = person.Identity.HomeLocationId;
            var loc = (home is not null && ctx.KnownLocations.Contains(home)) ? home
                    : (ctx.KnownLocations.Count > 0 ? ctx.KnownLocations[0] : null);
            if (loc is not null)
            {
                ctx.Locations.MoveCharacter(person.Id, loc);
                ctx.Orchestrator.InvalidateLocation(loc);
            }

            names[person.Id] = new NarrativeCharacterInfo(person.Identity.FirstName.Original, person.Biology);
            scene.AddCharacter(person);
            _log.LogInformation("Imported character {Name} ({Id}).", person.Identity.FirstName.Original, person.Id.Value);
            return true;
        }

        /// <summary>Reduces a string to a safe file-name fragment.</summary>
        private static string Sanitize(string s)
        {
            var chars = s.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
            var cleaned = new string(chars).Trim('_');
            return string.IsNullOrEmpty(cleaned) ? "postava" : cleaned;
        }

        private static void HandleBirths(
            WDateTime now,
            IReadOnlyList<IHuman> chars,
            IServiceProvider services,
            WorldContext ctx,
            SimulationScene scene,
            IDictionary<HumanId, NarrativeCharacterInfo> names)
        {
            foreach (var mother in chars)
            {
                ChildBorn? birth = null;
                foreach (var ev in mother.LastOutbox)
                {
                    if (ev is ChildBorn cb) { birth = cb; break; }
                }

                if (birth is null)
                    continue;

                var father = chars.FirstOrDefault(c => c.Id == birth.ParentB);
                if (father is null)
                    continue;

                var childGen = services.GetRequiredService<IChildBlueprintGenerator>();
                var factory = services.GetRequiredService<IHumanFactory>();
                var familyGraph = services.GetRequiredService<FamilyGraph>();

                var blueprint = childGen.Generate(parentA: father, parentB: mother, bornOn: now.Date, seed: null);
                var newborn = factory.Create(blueprint);

                FamilyBuilder.WireNewborn(familyGraph, father, mother, newborn, now);
                newborn.FlushInbox();

                var motherLocation = ctx.Locations.GetLocation(mother.Id);
                if (motherLocation is not null)
                {
                    ctx.Locations.MoveCharacter(newborn.Id, motherLocation);
                    ctx.Orchestrator.InvalidateLocation(motherLocation);
                }

                names[newborn.Id] = new NarrativeCharacterInfo(newborn.Identity.FirstName.Original, newborn.Biology);
                scene.AddCharacter(newborn);
            }
        }
    }
}
