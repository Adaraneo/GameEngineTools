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
        private readonly WTimeSpan? _replyWindow;
        private long? _idleTicks;
        private long? _replyTicks;

        /// <summary>Creates a coordinator; a conversation is dropped after <paramref name="idleTimeout"/> of silence.</summary>
        /// <remarks>
        /// Both timeouts are resolved lazily (on first use, not in the constructor) so the coordinator
        /// can be constructed before <see cref="World.Core.Time.WWorld"/> is configured.
        /// </remarks>
        /// <param name="idleTimeout">Silence after which the exchange is forgotten entirely.</param>
        /// <param name="replyWindow">
        /// How long an owed response stays owed. Shorter than <paramref name="idleTimeout"/>: an
        /// unanswered question stops demanding an answer long before the pair stop counting as having
        /// talked, so nobody answers an hour late.
        /// </param>
        public ConversationCoordinator(WTimeSpan? idleTimeout = null, WTimeSpan? replyWindow = null)
        {
            _idleTimeout = idleTimeout;
            _replyWindow = replyWindow;
        }

        // Two game-hours of silence ends a conversation; resolved lazily (needs WWorld.Spec).
        private long IdleTicks => _idleTicks ??= (_idleTimeout ?? WTimeSpan.FromHours(2)).Ticks;

        // A response is owed for a quarter of a game-hour — past that the moment to answer has passed.
        private long ReplyTicks => _replyTicks ??= (_replyWindow ?? WTimeSpan.FromMinutes(15)).Ticks;

        /// <summary>
        /// Hard ceiling on how many turns an exchange can be driven by obligation alone.
        /// </summary>
        /// <remarks>
        /// A safety net, not the mechanism that ends conversations — pairs close because a
        /// second-pair-part obliges nothing further (see <see cref="AdjacencyPairResolver"/>). It exists
        /// because obligation-driven replies turn any symmetric pair into an unbounded loop, which is a
        /// failure mode worth making structurally impossible rather than relying on every future pair
        /// being asymmetric. Generous enough not to truncate real exchanges (the longest today is three).
        /// </remarks>
        private const int MaxObligationTurns = 8;

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

        /// <summary>
        /// Finds a partner <paramref name="responder"/> currently owes an answer to — the same condition
        /// as <see cref="TryGetPendingResponse"/>, but searched by responder alone rather than asked
        /// about a partner already chosen.
        /// </summary>
        /// <remarks>
        /// This is what turns a question into an obligation. <see cref="TryGetPendingResponse"/> can only
        /// answer "is this the person I owe?" — it presumes the responder already decided to speak and to
        /// whom, so a reply happened only when the answerer independently felt like socialising and
        /// happened to pick the asker. Searching by responder lets the caller deliver the reply because it
        /// is owed. Bounded by <see cref="ReplyTicks"/>, so a lapsed obligation is simply not returned.
        /// </remarks>
        /// <param name="responder">The character that may owe a response.</param>
        /// <param name="now">Current simulation time.</param>
        /// <param name="other">The partner owed a response.</param>
        /// <param name="pending">The act to respond to.</param>
        /// <returns>True when a response is owed and still timely.</returns>
        public bool TryGetObligation(HumanId responder, WDateTime now, out HumanId other, out SpeechAct pending)
        {
            other = default;
            pending = null!;

            // Most recent first: when two people are both owed an answer, the freshest question wins.
            var bestActivity = long.MinValue;
            var found = false;

            foreach (var (key, convo) in _active)
            {
                if (convo.LastSpeaker == responder)
                {
                    continue;              // the responder holds the floor here — nothing owed
                }

                var (a, b) = key;
                HumanId partner;
                if (a == responder.Value)
                {
                    partner = new HumanId(b);
                }
                else if (b == responder.Value)
                {
                    partner = new HumanId(a);
                }
                else
                {
                    continue;              // not a participant
                }

                if (convo.LastSpeaker != partner)
                {
                    continue;              // the pending act is not the partner's
                }

                if (now.WorldTicks - convo.LastActivity.WorldTicks > ReplyTicks)
                {
                    continue;              // the moment to answer has passed
                }

                if (convo.TurnCount >= MaxObligationTurns)
                {
                    continue;              // safety net — an exchange cannot be driven forever
                }

                if (AdjacencyPairResolver.ResponseTo(convo.LastAct) is null)
                {
                    continue;              // the act does not oblige a response
                }

                if (convo.LastActivity.WorldTicks > bestActivity)
                {
                    bestActivity = convo.LastActivity.WorldTicks;
                    other = partner;
                    pending = convo.LastAct;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>Number of currently-tracked conversations (diagnostics/tests).</summary>
        public int ActiveCount => _active.Count;

        private static (Guid, Guid) Key(HumanId a, HumanId b)
            => a.Value.CompareTo(b.Value) <= 0 ? (a.Value, b.Value) : (b.Value, a.Value);
    }
}
