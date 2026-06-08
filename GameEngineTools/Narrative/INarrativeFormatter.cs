// INarrativeFormatter.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Narrative
{
    using System;
    using GameEngineTools.Characters.Core;

    /// <summary>
    /// Interface for formatting domain events into readable narrative text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why an interface, not a static class?</b><br/>
    /// Testability — in tests of <c>SimulationScene</c> you mock the formatter
    /// and verify it was called with the correct arguments.<br/>
    /// Extensibility — in the future you can add an <c>EnglishNarrativeFormatter</c>,
    /// <c>DebugNarrativeFormatter</c> or an AI-generated formatter — without changing SimulationScene.
    /// </para>
    /// <para>
    /// <b>Return value <c>null</c>:</b><br/>
    /// The formatter returns <c>null</c> for events that are not narratively interesting
    /// (e.g. <c>SleepPhaseChanged</c> — debug info, not story).
    /// The calling layer ignores null outputs.
    /// </para>
    /// <para>
    /// <b>Why <see cref="NarrativeCharacterInfo"/> instead of a plain <c>string</c>?</b><br/>
    /// See the record's documentation — because of grammatical gender in Czech.
    /// </para>
    /// </remarks>
    public interface INarrativeFormatter
    {
        /// <summary>
        /// Formats a domain event into a readable narrative entry.
        /// </summary>
        /// <param name="ev">The domain event to format.</param>
        /// <param name="resolveCharacter">
        /// Function that translates a <see cref="HumanId"/> into character information.
        /// The formatter calls it for every character mentioned in the event.
        /// </param>
        /// <returns>
        /// The narrative entry, or <c>null</c> if the event is not narratively interesting.
        /// </returns>
        NarrativeEntry? Format(IDomainEvent ev, Func<HumanId, NarrativeCharacterInfo> resolveCharacter);
    }
}
