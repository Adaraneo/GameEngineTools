// ConversationCoordinator.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Dialogue.Conversation
{
    using System;
    using System.Collections.Generic;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// The live state of an ongoing exchange between two participants — who spoke last, with what,
    /// whose turn it is, and how many turns have passed. Lightweight (model B): it tracks the surface
    /// of turn-taking without owning any simulation state.
    /// </summary>
    /// <param name="LastAct">The most recent act in the exchange.</param>
    /// <param name="LastSpeaker">Who produced <paramref name="LastAct"/> (⇒ it is the other party's turn).</param>
    /// <param name="LastActivity">When the last act occurred (for idle expiry).</param>
    /// <param name="TurnCount">Number of turns taken so far.</param>
    public sealed record ConversationContext(
        SpeechAct LastAct,
        HumanId LastSpeaker,
        WDateTime LastActivity,
        int TurnCount);

    /// <summary>
    /// Tracks active conversations between co-present character pairs so the speaker-side planner can
    /// produce a <i>response</i> (an adjacency pair) rather than a fresh topic, giving conversations
    /// turn-taking and coherence (model B). Stateful and single-threaded (one per scene orchestrator).
    /// </summary>
    public sealed class ConversationCoordinator
    {
        private readonly Dictionary<(Guid, Guid), ConversationContext> _active = new();
        private readonly WTimeSpan? _idleTimeout;
        private long? _idleTicks;

        /// <summary>Creates a coordinator; a conversation is dropped after <paramref name="idleTimeout"/> of silence.</summary>
        /// <remarks>
        /// The default timeout is resolved lazily (on first use, not in the constructor) so the
        /// coordinator can be constructed before <see cref="World.Core.Time.WWorld"/> is configured.
        /// </remarks>
        public ConversationCoordinator(WTimeSpan? idleTimeout = null) => _idleTimeout = idleTimeout;

        // Two game-hours of silence ends a conversation; resolved lazily (needs WWorld.Spec).
        private long IdleTicks => _idleTicks ??= (_idleTimeout ?? WTimeSpan.FromHours(2)).Ticks;

        /// <summary>Records an act between <paramref name="speaker"/> and <paramref name="addressee"/>.</summary>
        public void Observe(SpeechAct act, HumanId speaker, HumanId addressee, WDateTime now)
        {
            ArgumentNullException.ThrowIfNull(act);
            var key = Key(speaker, addressee);
            var turns = _active.TryGetValue(key, out var existing) ? existing.TurnCount + 1 : 1;
            _active[key] = new ConversationContext(act, speaker, now, turns);
        }

        /// <summary>
        /// True when <paramref name="responder"/> owes a conversational response to <paramref name="other"/>:
        /// an active, non-expired exchange exists, the other party spoke last, and that act invites a
        /// second-pair-part. Returns the act to respond to in <paramref name="pending"/>.
        /// </summary>
        public bool TryGetPendingResponse(HumanId responder, HumanId other, WDateTime now, out SpeechAct pending)
        {
            pending = null!;
            var key = Key(responder, other);
            if (!_active.TryGetValue(key, out var convo))
            {
                return false;
            }

            if (now.WorldTicks - convo.LastActivity.WorldTicks > IdleTicks)
            {
                _active.Remove(key);   // conversation went stale
                return false;
            }

            if (convo.LastSpeaker == responder)
            {
                return false;          // the responder already holds the floor — nothing owed
            }

            if (AdjacencyPairResolver.ResponseTo(convo.LastAct) is null)
            {
                return false;          // the last act does not oblige a response
            }

            pending = convo.LastAct;
            return true;
        }

        /// <summary>Number of currently-tracked conversations (diagnostics/tests).</summary>
        public int ActiveCount => _active.Count;

        private static (Guid, Guid) Key(HumanId a, HumanId b)
            => a.Value.CompareTo(b.Value) <= 0 ? (a.Value, b.Value) : (b.Value, a.Value);
    }
}
