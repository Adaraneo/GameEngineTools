// SpeechActInterpretation.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Dialogue.Interpretation
{
    using GameEngineTools.Dialogue.Semantics;

    /// <summary>
    /// Ambient shared listener-side interpreter used by the engine paths (Psychology emotion,
    /// Memory subjective trace). Mirrors the <c>WWorld.Configure</c> pattern: configured once at
    /// startup, read everywhere, <see cref="Reset"/> restores defaults for test isolation.
    /// </summary>
    /// <remarks>
    /// The default instance has the connotation layer OFF and a neutral lexicon — byte-identical to
    /// the pre-connotation engine. A host that wants the connotation experiment (e.g. WorldObserver)
    /// calls <see cref="Configure"/> with <c>EnableConnotationLayer = true</c> and the curated lexicon.
    /// Both Psychology and Memory read <see cref="Current"/>, so they always interpret with the SAME
    /// configuration and derive identical <c>PerceivedMeaning</c>s.
    /// </remarks>
    public static class SpeechActInterpretation
    {
        private static ISpeechActInterpreter _current = new DefaultSpeechActInterpreter();

        /// <summary>The interpreter the engine paths use.</summary>
        public static ISpeechActInterpreter Current => _current;

        /// <summary>Replaces the ambient interpreter (call once at startup, before ticking).</summary>
        /// <param name="config">Irony/hostility/connotation calibration.</param>
        /// <param name="connotationLexicon">Lemma affect data; neutral no-op when omitted.</param>
        /// <param name="acquisition">
        /// Per-character vocabulary. Supplying it lets decoding depend on whether <i>this</i> listener
        /// knows the word; without it, decoding falls back to what the population knows on average.
        /// </param>
        public static void Configure(
            SpeechActInterpreterConfig config,
            IConnotationLexicon? connotationLexicon = null,
            Characters.Engines.Language.ILexicalAcquisitionStore? acquisition = null)
            => _current = new DefaultSpeechActInterpreter(config, connotationLexicon, acquisition);

        /// <summary>Restores the default (connotation off, neutral lexicon) — test isolation.</summary>
        public static void Reset() => _current = new DefaultSpeechActInterpreter();
    }
}
