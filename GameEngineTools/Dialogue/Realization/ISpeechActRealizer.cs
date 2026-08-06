// ISpeechActRealizer.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Dialogue.Realization
{
    /// <summary>
    /// Renders a <see cref="SpeechAct"/> into Czech for the player.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rendering is one-way and player-facing. Characters exchange <see cref="SpeechAct"/> structures;
    /// the text produced here is never read back into simulation state, so no engine may depend on it.
    /// </para>
    /// <para>
    /// One act yields two readings, and they are not interchangeable: an observer's third-person
    /// account of what happened, and the words the speaker actually said.
    /// </para>
    /// </remarks>
    public interface ISpeechActRealizer
    {
        /// <summary>
        /// The observer's account — third person, past tense: <i>"Petr se zeptal Jany."</i>
        /// </summary>
        /// <param name="act">The act to describe.</param>
        /// <param name="speaker">Who spoke.</param>
        /// <param name="addressee">Who was addressed.</param>
        /// <returns>A finished Czech sentence.</returns>
        string Narrate(SpeechAct act, Participant speaker, Participant addressee);

        /// <summary>
        /// The utterance itself — first person to second, with address and register:
        /// <i>"Jano, nezajdeš se mnou?"</i>
        /// </summary>
        /// <param name="act">The act being spoken.</param>
        /// <param name="speaker">Who is speaking.</param>
        /// <param name="addressee">Who is being spoken to.</param>
        /// <returns>A finished Czech utterance.</returns>
        string Utter(SpeechAct act, Participant speaker, Participant addressee);
    }

    /// <summary>
    /// A participant's surface identity. The act itself carries only ids, so the caller supplies the
    /// name and the grammatical gender that agreement needs.
    /// </summary>
    /// <param name="Name">Nominative form of the name.</param>
    /// <param name="IsFemale">Grammatical gender, for predicate agreement and declension.</param>
    public readonly record struct Participant(string Name, bool IsFemale);
}
