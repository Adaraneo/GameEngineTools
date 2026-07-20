// InteractionContent.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Dialogue.Contracts
{
    /// <summary>
    /// Thin carrier for the semantic payload of an interaction in the interaction pipeline: a
    /// <see cref="SpeechAct"/> plus any non-speech interaction metadata. Deliberately introduces no
    /// parallel semantic representation — the <see cref="SpeechAct"/> is the single source of meaning.
    /// </summary>
    /// <param name="SpeechAct">The structured act being communicated.</param>
    public sealed record InteractionContent(SpeechAct SpeechAct);
}
