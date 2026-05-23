// SemanticMemory.Config.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.SemanticMemory
{
    /// <summary>
    /// Konfigurace <see cref="DefaultSemanticMemoryEngine"/>.
    /// </summary>
    public sealed record SemanticMemoryConfig(
        /// <summary>Rychlost učení nových přesvědčení (0–1 per evidence).</summary>
        double LearningRate = 0.18,
        /// <summary>Jak rychle protichůdná přesvědčení slabnou (per event).</summary>
        double ContradictionRate = 0.08,
        /// <summary>Přirozený decay strength za den bez kontaktu.</summary>
        double DecayPerDay = 0.01,
        /// <summary>Nárůst stability za každou evidenci.</summary>
        double StabilityGainPerEvidence = 0.08,
        /// <summary>Počet posledních epizod pro pattern window.</summary>
        int PatternWindowSize = 6,
        /// <summary>Minimální počet výskytů pro plnou váhu patternu (jinak 0.45×).</summary>
        int MinimumPatternSupport = 2,
        /// <summary>Penalizace stability při contradikci.</summary>
        double ContradictionStabilityHit = 0.05,
        // ── Attachment style modulation (Bartholomew-Horowitz 2D model) ──────────────
        /// <summary>Anxious attachment: hyperaktivace → 1.30× rychlejší učení.</summary>
        double AttachmentLearningBoostAnxious = 1.30,
        /// <summary>Avoidant attachment: deaktivace → 0.75× pomalejší učení.</summary>
        double AttachmentLearningDiscountAvoidant = 0.75,
        /// <summary>Avoidant attachment: potlačení EmotionallySafe belief (0.45× váha).</summary>
        double AttachmentSafeDiscountAvoidant = 0.45,
        /// <summary>Disorganized attachment: nestabilní → 1.15× učení.</summary>
        double AttachmentLearningBoostDisorganized = 1.15,
        /// <summary>Anxious attachment: vyšší contradikční sensitivita → 1.20×.</summary>
        double AttachmentContradictionBoostAnxious = 1.20,
        /// <summary>Disorganized attachment: nejvyšší contradikční sensitivita → 1.40×.</summary>
        double AttachmentContradictionBoostDisorganized = 1.40,
        // ── Navarro 8× gap rule (Navarro et al. 2017) ────────────────────────────────
        /// <summary>
        /// Pokud uplynulo více než N × průměrný meziinterakční interval,
        /// decay se násobí <see cref="NavarroDecayAccelerator"/>.
        /// </summary>
        int NavarroCriticalMultiple = 8,
        /// <summary>Multiplikátor decay při překročení Navarro prahu (default 3×).</summary>
        double NavarroDecayAccelerator = 3.0)
    {
        /// <summary>Bezparametrický konstruktor vyžadovaný Options patternem.</summary>
        public SemanticMemoryConfig() : this(
            0.18, 0.08, 0.01, 0.08, 6, 2, 0.05,
            1.30, 0.75, 0.45, 1.15, 1.20, 1.40, 8, 3.0)
        { }
    }
}
