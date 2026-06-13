// SocialComparisonEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Social
{
    // ──────────────────────────────────────────────────────────────────────────
    // TODO (Finding 4 — Social Comparison): NEW subsystem, not yet scheduled.
    //
    // Verdict: Confirmed but heavily moderated. CONTRAST is the default response;
    //   upward comparison is preferred even under threat. Assimilation occurs only under
    //   identification/priming.
    //
    // Sketch: each character has a reference group. Upward/downward comparison feeds
    //   self-esteem (SelfConcept) and motivation. Default response = contrast; assimilation
    //   only under identification/priming. Slots into the tick pipeline as its own engine and
    //   reads RelationshipState / SelfConcept; emits self-esteem/motivation deltas the
    //   Psychology and SelfConcept engines consume.
    //
    // Effect-size anchors (use as relative weights, calibrate in config):
    //   contrast ≈ −0.65 to −0.83 (ability/affect); upward-comparison self-eval g ≈ −0.24.
    //
    // Key papers: Gerber, Wheeler & Suls (2018) Psychological Bulletin 144(2);
    //   McComb et al. (2023) Media Psychology; Buunk & Gibbons (2007) OBHDP 102(1);
    //   Vogel et al. (2014); Luo & Yu (2019).
    //
    // Do NOT implement logic until explicitly tasked — this file is a scaffold/citation marker.
    // ──────────────────────────────────────────────────────────────────────────
}
