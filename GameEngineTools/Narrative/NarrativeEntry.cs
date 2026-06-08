// NarrativeEntry.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Narrative
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// A single narrative entry — the result of translating a domain event into a readable sentence.
    /// </summary>
    /// <param name="OccurredAt">Game time of the event.</param>
    /// <param name="Subject">
    /// The main character of the sentence — the "hero" of the entry.
    /// Used to filter a specific character's journal.
    /// </param>
    /// <param name="Text">Readable sentence describing the event (in Czech).</param>
    /// <param name="Priority">Importance of the entry to the player.</param>
    public sealed record NarrativeEntry(
        WDateTime OccurredAt,
        HumanId Subject,
        string Text,
        NarrativePriority Priority);
}
