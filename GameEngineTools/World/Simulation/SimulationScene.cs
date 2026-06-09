// SimulationScene.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Simulation
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Sleep;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.Narrative;
    using GameEngineTools.Universe;
    using GameEngineTools.World.Core.Astro;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// A generic simulation scene for any number of characters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Scene responsibilities:</b>
    /// <list type="bullet">
    ///   <item>Correct character tick order (in the order of <see cref="SimulationSceneOptions.Characters"/>)</item>
    ///   <item>Automatic routing of <see cref="InteractionOutcome"/> back to the initiator</item>
    ///   <item>Handling sleep prompts per the per-character strategy</item>
    ///   <item>Advancing the game clock by <c>SimulationSceneOptions.ClockAdvance</c> each iteration</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>What the scene deliberately does NOT do:</b>
    /// <list type="bullet">
    ///   <item>Does not handle <c>ReachOut → InteractionProposed</c> routing — that is the calling layer's scenario</item>
    ///   <item>Does not export data or print headers — that belongs in GameSandbox / Unity</item>
    ///   <item>Does not distinguish player from NPC — all characters are equal</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Order of steps in each iteration:</b>
    /// <code>
    /// 1. OnTick callback      — scenario + ReachOut routing (sees LastOutbox from the previous tick)
    /// 2. Tick each character   — engines recompute state
    /// 3. Route outcomes       — InteractionOutcome → the initiator receives a response
    /// 4. Sleep prompty        — per-postava dle SleepPromptHandlers
    /// 5. ClockAdvance         — the clock is advanced
    /// </code>
    /// </para>
    /// </remarks>
    public sealed class SimulationScene
    {
        #region Privátní pole

        /// <summary>Game clock — requires <c>Advance()</c> to advance simulation time.</summary>
        private readonly SystemClock _clock;

        /// <summary>Scene configuration passed in from outside.</summary>
        private readonly SimulationSceneOptions _options;

        private readonly ICognitiveResolutionLevelRuntime _lodRuntime;

        /// <summary>Null if the astronomical logic is not configured.</summary>
        private readonly CelestialContextComputer? _celestialComputer;

        /// <summary>Astronomy config — non-null exactly when <see cref="_celestialComputer"/> is non-null.</summary>
        private readonly AstroConfig? _astroConfig;

        /// <summary>Phase 2 planetary objects — null if <c>UniverseConfig</c> is not set.</summary>
        private readonly (StarPhysics Star, OrbitalElements Orbit, PlanetConfig Planet)? _universeObjects;

        /// <summary>Characters that have died and must no longer tick.</summary>
        private readonly HashSet<HumanId> _deadCharacters = new();

        #endregion Privátní pole

        #region Konstruktor

        /// <summary>
        /// Creates a new <see cref="SimulationScene"/> instance.
        /// </summary>
        /// <param name="clock">
        /// The game clock. Must be a <see cref="SystemClock"/> —
        /// the scene needs <c>Advance()</c> to drive simulation time.
        /// </param>
        /// <param name="options">Scene configuration — characters, timing, handlers.</param>
        /// <exception cref="ArgumentNullException">
        /// Pokud je <paramref name="clock"/> nebo <paramref name="options"/> null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// If <see cref="SimulationSceneOptions.Characters"/> is an empty list.
        /// </exception>
        public SimulationScene(SystemClock clock, SimulationSceneOptions options, ICognitiveResolutionLevelRuntime characterLodRuntime)
        {
            ArgumentNullException.ThrowIfNull(clock);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(characterLodRuntime);

            if (options.Characters.Count == 0)
                throw new ArgumentException(
                    "SimulationScene vyžaduje alespoň jednu postavu v Characters.", nameof(options));

            _clock = clock;
            _options = options;
            _lodRuntime = characterLodRuntime;

            // Restore dead-character set from persisted snapshot.
            // Characters that died in a previous session have Status = Dead in their PhysiologyState.
            // Without this, a reimported dead character would tick once before dying again.
            foreach (var character in options.Characters)
            {
                if (character.Snapshot.Physiology.Status == StatusType.Dead)
                    _deadCharacters.Add(character.Id);
            }

            if (options.AstroConfig is { } astroCfg)
            {
                _astroConfig = astroCfg;
                _celestialComputer = new CelestialContextComputer(new SunModel());
            }

            if (options.UniverseConfig is { } uc)
                _universeObjects = (uc.ToStarPhysics(), uc.ToOrbitalElements(), uc.ToPlanetConfig());
        }

        #endregion Konstruktor

        #region Veřejné API

        /// <summary>
        /// Starts the simulation asynchronously.
        /// Ends after <c>SimulationSceneOptions.SimulationYears</c> has elapsed.
        /// </summary>
        /// <returns>A completed task once the simulation ends.</returns>
        public Task RunAsync()
            => SimulateAsync();

        #endregion Veřejné API

        #region Simulační smyčka

        /// <summary>
        /// Main simulation loop — iterates until the configured number of game years has elapsed.
        /// </summary>
        private Task SimulateAsync()
        {
            var startTime = _clock.Now;
            var endTime = _clock.Now.AddDays(_options.SimulationDays);
            var chars = _options.Characters;
            var macroStep = _options.TickStep;
            var internalSubstep = ResolveInternalSubstep(macroStep);

            while (_clock.Now < endTime)
            {
                var macroRemaining = macroStep;

                while (macroRemaining > WTimeSpan.Zero && _clock.Now < endTime)
                {
                    var remainingToEnd = endTime - _clock.Now;
                    var dt = WTimeSpan.Min(internalSubstep, WTimeSpan.Min(macroRemaining, remainingToEnd));
                    SimulateSingleStep(startTime, chars, dt);
                    macroRemaining -= dt;
                }
            }

            return Task.CompletedTask;
        }

        private void SimulateSingleStep(WDateTime startTime, IReadOnlyList<IHuman> chars, WTimeSpan dt)
        {
            // ── Cache refresh — must happen first, before any character reads objects ──
            // Loads objects for all active locations in O(distinct locations) queries
            // instead of O(characters) queries. No-op when cache is not configured.
            if (_options.ObjectSnapshotCache is { } cache)
            {
                _options.WriteBuffer?.Flush();

                var activeLocations = chars
                    .Select(c => _options.LocationService?.GetLocation(c.Id))
                    .OfType<string>();

                cache.Refresh(activeLocations);
            }

            ApplyCharacterLods(chars);

            var now = _clock.Now;

            // ── Step 0b: Astronomical context ──────────────────────────────────────
            // Computes the CelestialContext once per tick and injects it into each character
            // before its tick — so the engines see the current irradiance, temperature and season.
            if (_celestialComputer is not null)
            {
                var celestial = _universeObjects is { } u
                    ? _celestialComputer.Compute(now, _astroConfig!, u.Star, u.Orbit, u.Planet)
                    : _celestialComputer.Compute(now, _astroConfig!);

                foreach (var character in chars)
                    character.SetAmbientContext(celestial.BaseAmbientTempCelsius, celestial);
            }

            _options.LocationService?.DispatchContextEvents(now, chars, forceAll: now == startTime);

            // ── Step 1: OnTick callback (scenario + ReachOut / movement routing) ──────────
            // NOTE: each character's LastOutbox is still from the PREVIOUS tick here —
            // this is the correct place to detect ReachOut and route movement.
            // Snapshot locations first so we can tell whom RouteMoveTo relocates during OnTick.
            var locationsBeforeMovement = SnapshotLocations(chars);
            _options.OnTick?.Invoke(now, chars);

            // ── Step 1b: Post-movement perception refresh ─────────────────────────────────
            // RouteMoveTo (inside OnTick) may relocate a character AFTER the pre-tick cache
            // refresh and context dispatch have already run. Re-sync perception so a character
            // that just arrived (e.g. at food) perceives it THIS substep and can act on arrival,
            // instead of re-issuing another MoveTo and looping forever.
            RefreshPerceptionAfterMovement(now, startTime, chars, locationsBeforeMovement);

            // ── Step 2: Tick all characters ────────────────────────────────────────
            // All characters advance their state before any outcomes are routed.
            // Ensures outcome delivery is independent of character list order.
            foreach (var character in chars)
            {
                if (_deadCharacters.Contains(character.Id)) continue;
                character.Tick(now, dt);
            }

            // ── Step 2b: Routing outcomes ─────────────────────────────────────────
            // Outcomes are enqueued into recipient inboxes and processed at the START
            // of their next tick (Phase A) — regardless of position in the character list.
            foreach (var character in chars)
            {
                if (_deadCharacters.Contains(character.Id)) continue;
                RouteOutcomes(character, chars);
            }

            // ── Step 2c: Respawn of consumed objects ──────────────────────────────
            // Runs after all characters have ticked so that an object consumed in this
            // step cannot be reclaimed by the same step.
            _options.RespawnScheduler?.Tick(now);

            // --- Narrative scan ----
            if (_options.NarrativeFormatter is { } formatter && _options.OnNarrative is { } onNarrative)
            {
                var resolver = _options.ResolveCharacter ?? (id => new NarrativeCharacterInfo(id.Value.ToString(), SexBiology.Unknown));

                foreach (var character in chars)
                {
                    foreach (var ev in character.LastOutbox)
                    {
                        var entry = formatter.Format(ev, resolver);
                        if (entry is not null)
                        {
                            onNarrative(entry);
                        }
                    }
                }
            }

            // Detect characters that died this tick — they must not tick in subsequent steps.
            foreach (var character in chars)
            {
                if (character.LastOutbox.OfType<CharacterDied>().Any())
                    _deadCharacters.Add(character.Id);
            }

            // ── Krok 3: Sleep prompty ───────────────────────────────────────────────
            foreach (var character in chars)
            {
                if (_deadCharacters.Contains(character.Id)) continue;
                HandleSleepPrompt(now, character);
            }

            // ── Krok 4: Posun hodin ────────────────────────────────────────────────
            _clock.Advance(dt);
        }

        private WTimeSpan ResolveInternalSubstep(WTimeSpan macroStep)
        {
            var configured = _options.InternalSubstep;
            if (configured is null || configured.Value <= WTimeSpan.Zero)
            {
                return macroStep;
            }

            return WTimeSpan.Min(configured.Value, macroStep);
        }

        #endregion Simulační smyčka

        #region Routování výstupů

        /// <summary>
        /// Routes an <see cref="InteractionOutcome"/> from the sender's outbox
        /// back to the initiator of the interaction.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Why do we route the outcome back to <c>From</c>?<br/>
        /// <c>InteractionEngine</c> postavy B zpracuje <c>InteractionProposed</c> od A
        /// and emits an <c>InteractionOutcome</c> in B's outbox.
        /// For A to know whether it was accepted or declined, we must deliver the outcome to A.
        /// </para>
        /// <para>
        /// <see cref="IHuman.ReceiveEvent"/> is a synchronous enqueue —
        /// A processes the outcome at the start of its <em>next</em> tick.
        /// </para>
        /// </remarks>
        /// <param name="sender">The character whose outbox we are currently scanning.</param>
        /// <param name="all">All characters in the scene, for initiator lookup.</param>
        private static void RouteOutcomes(IHuman sender, IReadOnlyList<IHuman> all)
        {
            foreach (var outcome in sender.LastOutbox.OfType<InteractionOutcome>())
            {
                // Find the initiator — the one who originally sent InteractionProposed
                var initiator = all.FirstOrDefault(c => c.Id == outcome.From);
                initiator?.ReceiveEvent(outcome);
            }

            // Touch outcome
            foreach (var outcome in sender.LastOutbox.OfType<TouchOutcome>())
            {
                var initiator = all.FirstOrDefault(c => c.Id == outcome.From);
                initiator?.ReceiveEvent(outcome);
            }

            foreach (var outcome in sender.LastOutbox.OfType<SexualEncounterOutcome>())
            {
                var initiator = all.FirstOrDefault(c => c.Id == outcome.From);
                initiator?.ReceiveEvent(outcome);
            }
        }

        #endregion Routování výstupů

        #region Sleep handling

        /// <summary>
        /// Handles a character's sleep prompt per the configured strategy.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Strategie dle <see cref="SimulationSceneOptions.SleepPromptHandlers"/>:
        /// <list type="bullet">
        ///   <item>
        ///     A handler found for the character → calls the callback; the result (<c>bool</c>)
        ///     decides whether to confirm or decline.
        ///   </item>
        ///   <item>
        ///     No handler found → automatic confirmation (NPC default).
        ///   </item>
        /// </list>
        /// </para>
        /// </remarks>
        /// <param name="now">Current game time.</param>
        /// <param name="character">The character whose outbox we are scanning.</param>
        private void HandleSleepPrompt(WDateTime now, IHuman character)
        {
            var prompt = character.LastOutbox.OfType<SleepPromptRequested>().FirstOrDefault();
            if (prompt == null)
                return;

            // Determine whether this character has its own handler
            var shouldSleep = true; // výchozí: auto-potvrdit (NPC chování)

            if (_options.SleepPromptHandlers != null
                && _options.SleepPromptHandlers.TryGetValue(character.Id, out var handler))
            {
                shouldSleep = handler(prompt);
            }

            if (shouldSleep)
            {
                // Confirm sleep — the engine computes the duration itself based on debt
                character.ReceiveEvent(new SleepConfirmed(
                    OccurredAt: now,
                    Human: prompt.Human,
                    PlannedWakeUp: default));
            }
            else
            {
                // Decline sleep — the engine logs the penalty
                character.ReceiveEvent(new SleepDeclined(
                    OccurredAt: now,
                    Human: prompt.Human,
                    character.Snapshot.Behavior.SleepDeclineCount));
            }
        }

        #endregion Sleep handling

        #region Private Helpers

        private void ApplyCharacterLods(IReadOnlyList<IHuman> characters)
        {
            foreach (var c in characters)
            {
                var lod = _options.ResolveCharacterLod?.Invoke(c) ?? _options.DefaultCharacterLod;
                _lodRuntime.Set(c.Id, lod);
            }
        }

        /// <summary>
        /// Captures the current location id of every character, so movement performed during
        /// the <see cref="SimulationSceneOptions.OnTick"/> callback can be detected afterwards.
        /// </summary>
        /// <param name="chars">Characters to snapshot.</param>
        /// <returns>Map of character id to location id (<c>null</c> when the character is unplaced).</returns>
        private Dictionary<HumanId, string?> SnapshotLocations(IReadOnlyList<IHuman> chars)
        {
            var map = new Dictionary<HumanId, string?>(chars.Count);
            foreach (var c in chars)
                map[c.Id] = _options.LocationService?.GetLocation(c.Id);

            return map;
        }

        /// <summary>
        /// Re-synchronises perception with the world after movement routing has run inside
        /// <see cref="SimulationSceneOptions.OnTick"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Why this exists:</b> the movement router (<c>RouteMoveTo</c>) relocates characters
        /// during <c>OnTick</c> — after the pre-tick object-cache refresh and context dispatch
        /// have already happened. Without this step a relocated character would still perceive
        /// its OLD location for one more substep and re-issue a <c>MoveTo</c> instead of acting
        /// on arrival. Because movement recurs every substep, the character never settles — in
        /// the worst case it reaches food repeatedly yet starves, never committing <c>Eat</c>.
        /// </para>
        /// <para>
        /// <b>Guarded:</b> the cache re-query and re-dispatch run only when at least one character
        /// actually changed location. When nobody moved, the pre-movement refresh/dispatch is still
        /// valid and both operations are skipped (avoids a redundant SQLite round-trip per substep).
        /// </para>
        /// </remarks>
        /// <param name="now">Current simulation time.</param>
        /// <param name="startTime">Simulation start — forces a full dispatch on the very first tick.</param>
        /// <param name="chars">All characters in the scene.</param>
        /// <param name="locationsBeforeMovement">Locations captured immediately before <c>OnTick</c> ran.</param>
        private void RefreshPerceptionAfterMovement(
            WDateTime now,
            WDateTime startTime,
            IReadOnlyList<IHuman> chars,
            IReadOnlyDictionary<HumanId, string?> locationsBeforeMovement)
        {
            if (_options.LocationService is not { } locationService)
                return;

            // Detect whether movement routing relocated anyone during the OnTick callback.
            var anyMoved = false;
            foreach (var c in chars)
            {
                var before = locationsBeforeMovement.GetValueOrDefault(c.Id);
                var after = locationService.GetLocation(c.Id);
                if (!string.Equals(before, after, StringComparison.Ordinal))
                {
                    anyMoved = true;
                    break;
                }
            }

            // Nobody moved → pre-movement cache refresh and context dispatch are still correct.
            if (!anyMoved)
                return;

            // Rebuild the object snapshot for the post-movement locations so relocated characters
            // read the objects actually present where they now stand.
            if (_options.ObjectSnapshotCache is { } cache)
            {
                var activeLocations = chars
                    .Select(c => locationService.GetLocation(c.Id))
                    .OfType<string>();

                cache.Refresh(activeLocations);
            }

            // Re-dispatch context so relocated characters perceive their new location before Tick().
            // DispatchContextEvents only emits to characters whose location actually changed.
            locationService.DispatchContextEvents(now, chars, forceAll: now == startTime);
        }

        #endregion Private Helpers
    }
}
