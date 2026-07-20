// SimulationSceneOptions.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Simulation
{
    using System;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Sleep;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.Narrative;
    using GameEngineTools.World.Core.Astro;
    using GameEngineTools.World.Location;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Configuration for <see cref="SimulationScene"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why an Options object instead of constructor parameters?</b><br/>
    /// With many characters the constructor would be unwieldy. An Options object
    /// gives every value a name and lets new properties be added without a breaking change.
    /// </para>
    /// <para>
    /// <b>Key difference from the old <c>InteractionSceneOptions</c>:</b><br/>
    /// Instead of a <c>Player / Npc</c> pair you pass a generic list of <see cref="Characters"/>.
    /// The scene knows nothing about who is the player and who is an NPC — that is the
    /// concern of the calling layer (Program.cs, Unity GameManager, …).
    /// </para>
    /// <para>
    /// <b>ReachOut routing is deliberately up to you</b> — the scene does not handle it
    /// automatically, because target selection depends on game logic (location, intent…).
    /// By detecting ReachOut in <see cref="OnTick"/> you see the <c>LastOutbox</c>
    /// from the previous tick and can decide yourself.
    /// </para>
    /// </remarks>
    public sealed class SimulationSceneOptions
    {
        #region Participants

        /// <summary>
        /// All characters participating in the simulation.
        /// </summary>
        /// <remarks>
        /// The order determines tick order — the first in the list ticks first.
        /// If you have a player, the convention is to place it at index 0.
        /// </remarks>
        public IReadOnlyList<IHuman> Characters { get; init; } = Array.Empty<IHuman>();

        #endregion Participants

        /// <summary>
        /// Optional location service. When provided, the scene calls
        /// <see cref="ILocationService.DispatchContextEvents"/> at the start
        /// of every tick — before the OnTick callback.
        /// </summary>
        public ILocationService? LocationService { get; init; }

        #region Simulation timing

        /// <summary>
        /// Default value for the number of simulation days.
        /// </summary>
        public long SimulationDays { get; init; } = 20;

        /// <summary>
        /// Length of a single outer simulation step — how far the world should ideally advance
        /// in one iteration of the main loop.
        /// Default value: <c>0.5 game hours</c>.
        /// </summary>
        /// <remarks>
        /// When <see cref="InternalSubstep"/> is set, the scene splits this step into finer
        /// sub-steps for lower inter-character latency and more accurate timing.
        /// </remarks>
        public WTimeSpan TickStep { get; init; } = WTimeSpan.FromHours(0.5);

        /// <summary>
        /// Optional provider for a <b>dynamic</b> outer step, re-read once per main-loop iteration.
        /// When set, its return value is used instead of the fixed <see cref="TickStep"/>, letting a
        /// host change how much world time passes per tick at runtime (e.g. a live "world tempo" /
        /// fast-forward control) without restarting the scene. A non-positive result falls back to
        /// <see cref="TickStep"/>. Default <c>null</c> preserves the fixed-step behavior.
        /// </summary>
        public Func<WTimeSpan>? TickStepProvider { get; init; }

        /// <summary>
        /// Optional finer sub-step for the scene.
        /// </summary>
        /// <remarks>
        /// If smaller than <see cref="TickStep"/>, the scene performs several internal steps within
        /// a single outer tick. This reduces interaction latency and improves planning accuracy without
        /// having to shrink the main <see cref="TickStep"/> for the whole sandbox.
        /// </remarks>
        public WTimeSpan? InternalSubstep { get; init; }

        /// <summary>Default LOD tier assigned to characters when no resolver is provided.</summary>
        public CognitiveResolutionLevel DefaultCharacterLod { get; init; } = CognitiveResolutionLevel.Nearby;

        /// <summary>Optional per-character LOD resolver overriding <see cref="DefaultCharacterLod"/>.</summary>
        public Func<IHuman, CognitiveResolutionLevel>? ResolveCharacterLod { get; init; }

        #endregion Simulation timing

        #region Scenario (callback)

        /// <summary>
        /// Callback invoked at the <b>start of every tick</b>, before the characters' <c>Tick()</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two roles in one callback:
        /// <list type="number">
        ///   <item>
        ///     <b>Scenario</b> — injection of scheduled events (day 2 → SmallTalk,
        ///     day 16 → move to the castle…). See the example below.
        ///   </item>
        ///   <item>
        ///     <b>ReachOut routing</b> — at this moment each character's <c>LastOutbox</c>
        ///     is still from the <em>previous</em> tick, so you can see who wants to
        ///     reach out and pick the target yourself.
        ///   </item>
        /// </list>
        /// </para>
        /// <para>
        /// Signature: <c>void OnTick(WDateTime now, IReadOnlyList&lt;IHuman&gt; characters)</c>
        /// </para>
        /// <example>
        /// <code>
        /// OnTick = (now, chars) =>
        /// {
        ///     var player = chars[0];
        ///     var npc    = chars[1];
        ///
        ///     // Scheduled scenario
        ///     if (now.Day is 2 or 6 or 12)
        ///         npc.ReceiveEvent(new InteractionProposed(now, player.Id, npc.Id, RelationalActKind.SmallTalk, "Hi!"));
        ///
        ///     // ReachOut routing — who wants to reach out?
        ///     foreach (var c in chars)
        ///     {
        ///         var reachOut = c.LastOutbox.OfType&lt;ActionCommitted&gt;()
        ///             .FirstOrDefault(a => a.ActionName == "ReachOut");
        ///         if (reachOut == null) continue;
        ///
        ///         // Pick a target — e.g. the nearest one in the same location
        ///         var target = chars
        ///             .Where(x => x.Id != c.Id
        ///                 &amp;&amp; x.Snapshot.InteractionSurface.Location == c.Snapshot.InteractionSurface.Location)
        ///             .FirstOrDefault();
        ///
        ///         target?.ReceiveEvent(new InteractionProposed(now, c.Id, target.Id, RelationalActKind.SmallTalk, null));
        ///     }
        /// }
        /// </code>
        /// </example>
        /// <para>If <c>null</c>, the scene simulates only the engines' natural behaviour.</para>
        /// </remarks>
        public Action<WDateTime, IReadOnlyList<IHuman>>? OnTick { get; init; }

        #endregion Scenario (callback)

        #region Narrative output

        /// <summary>
        /// Optional formatter that converts domain events into readable text.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If <c>null</c>, narrative output is disabled — the scene simulates normally.
        /// </para>
        /// <para>
        /// <b>Typical use in GameSandbox:</b>
        /// <code>
        /// NarrativeFormatter = new DefaultNarrativeFormatter(),
        /// ResolveCharacter   = id => new NarrativeCharacterInfo(
        ///     name:    chars.First(c => c.Id == id).Person.Identity.FirstName.Value,
        ///     biology: chars.First(c => c.Id == id).Biology),
        /// OnNarrative = entry =>
        /// {
        ///     if (entry.Priority >= NarrativePriority.Medium)
        ///         Console.WriteLine($"[{entry.OccurredAt}] {entry.Text}");
        /// }
        /// </code>
        /// </para>
        /// </remarks>
        public INarrativeFormatter? NarrativeFormatter { get; init; }

        /// <summary>
        /// Resolver of character information for narrative formatting.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The scene passes you a <see cref="HumanId"/>; you return the name and sex.
        /// The resolver is a lambda — you do not have to pass the whole character list into the Narrative namespace.
        /// </para>
        /// <para>
        /// If <c>null</c> and <see cref="NarrativeFormatter"/> is set,
        /// the scene uses a default fallback resolver (<c>HumanId.Value.ToString()</c>,
        /// <c>SexBiology.Unknown</c>).
        /// </para>
        /// </remarks>
        public Func<HumanId, NarrativeCharacterInfo>? ResolveCharacter { get; init; }

        /// <summary>
        /// Callback invoked for every generated narrative entry.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Signature: <c>void OnNarrative(NarrativeEntry entry)</c>
        /// </para>
        /// <para>
        /// Filter by priority as needed:
        /// <code>
        /// OnNarrative = entry =>
        /// {
        ///     if (entry.Priority == NarrativePriority.High)
        ///         ShowNotification(entry.Text);
        ///
        ///     _diary.Add(entry);
        /// }
        /// </code>
        /// </para>
        /// </remarks>
        public Action<NarrativeEntry>? OnNarrative { get; init; }

        #endregion Narrative output

        #region Astronomical context

        /// <summary>
        /// Optional configuration of the astronomical logic (solar model).
        /// When set, before every character tick the scene computes
        /// <see cref="CelestialContext"/> and injects it via
        /// <see cref="IHuman.SetAmbientContext"/>.
        /// </summary>
        public AstroConfig? AstroConfig { get; init; }

        /// <summary>
        /// Optional planetary-system configuration (Phase 2).
        /// When set together with <see cref="AstroConfig"/>, the scene uses
        /// Keplerian mechanics to compute season, temperature and gravity.
        /// </summary>
        public UniverseConfig? UniverseConfig { get; init; }

        #endregion Astronomical context

        #region Sleep handling

        /// <summary>
        /// Per-character sleep-prompt handlers.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Key: the character's <see cref="HumanId"/>.<br/>
        /// Value: a <c>Func&lt;SleepPromptRequested, bool&gt;</c> callback —
        /// returns <c>true</c> = go to sleep, <c>false</c> = decline.
        /// </para>
        /// <para>
        /// <b>Default behaviour:</b> if a character is not in this dictionary,
        /// the scene automatically confirms sleep (NPC behaviour).
        /// </para>
        /// <para>
        /// Example — a player with a manual prompt:
        /// <code>
        /// SleepPromptHandlers = new Dictionary&lt;HumanId, Func&lt;SleepPromptRequested, bool&gt;&gt;
        /// {
        ///     [player.Id] = _ =>
        ///     {
        ///         Console.WriteLine("[SLEEP] Go to sleep? (Y/n)");
        ///         return Console.ReadKey(true).Key != ConsoleKey.N;
        ///     }
        /// }
        /// </code>
        /// </para>
        /// </remarks>
        public IReadOnlyDictionary<HumanId, Func<SleepPromptRequested, bool>>? SleepPromptHandlers { get; init; }

        #endregion Sleep handling

        /// <summary>
        /// Optional per-tick object snapshot cache.
        /// When set, <see cref="SimulationScene"/> calls <see cref="WorldObjectSnapshotCache.Refresh"/>
        /// at the start of each substep, loading objects for all active locations in a single
        /// batch instead of one query per character per tick.
        /// </summary>
        /// <remarks>
        /// Assign the same <see cref="WorldObjectSnapshotCache"/> instance that is registered
        /// as <see cref="IWorldObjectProvider"/> in DI. The behavior engine will then read
        /// from the cache automatically, without any further changes.
        /// </remarks>
        public WorldObjectSnapshotCache? ObjectSnapshotCache { get; init; }

        /// <summary>
        /// Optional write buffer for world object mutations.
        /// When set, <see cref="SimulationScene"/> calls <see cref="WorldObjectWriteBuffer.Flush"/>
        /// at the start of each substep before cache Refresh, batching all mutations
        /// from the previous substep into a single SQLite transaction.
        /// </summary>
        public WorldObjectWriteBuffer? WriteBuffer { get; init; }

        /// <summary>
        /// Optional object respawn scheduler.
        /// When set, <see cref="SimulationScene"/> calls <see cref="ObjectRespawnScheduler.Tick"/>
        /// once per substep after all characters have ticked, restoring consumed objects
        /// whose respawn timer has elapsed and emitting EventId 1501 log entries.
        /// </summary>
        public ObjectRespawnScheduler? RespawnScheduler { get; init; }
    }
}
