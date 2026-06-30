// BereavementTypes.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Bereavement
{
    using System;
    using System.Collections.Generic;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// The empirically-supported grief trajectory a mourner follows after a loss. <b>Not stages</b> —
    /// these are distinct latent trajectory classes identified by growth-mixture modelling, with the
    /// prevalences in <see cref="BereavementConfig"/>.
    /// </summary>
    /// <remarks>
    /// Source: Lundorff, Bonanno, Johannsen &amp; O'Connor (2020) <i>J Psychiatr Res</i> 129:168–175;
    /// Bonanno (2002) <i>JPSP</i> 83:1150. There is deliberately no Kübler-Ross stage automaton
    /// (Maciejewski et al. 2007 + the JAMA critique letters reject sequential stages).
    /// </remarks>
    public enum GriefTrajectory
    {
        /// <summary>Low, stable distress — the modal response (~64 %).</summary>
        Resilient,

        /// <summary>Chronically elevated but non-escalating distress (~20 %).</summary>
        ModerateStable,

        /// <summary>High initial distress that recovers over months (~8 %).</summary>
        Recovery,

        /// <summary>Persistent, impairing grief — the prolonged-grief class (~7 %).</summary>
        Prolonged
    }

    /// <summary>
    /// The character's continuing bond with the deceased. Bonds are not uniformly adaptive
    /// (Field &amp; Filanosky 2010): an internalised bond is a secure base that eases grief over time,
    /// while an externalised bond (e.g. seeking the deceased's presence) sustains or worsens it.
    /// </summary>
    public enum ContinuingBond
    {
        /// <summary>No salient continuing bond.</summary>
        None,

        /// <summary>Adaptive: the deceased is internalised as a secure base — lowers long-term grief.</summary>
        Internalized,

        /// <summary>Maladaptive: the bond is externalised — maintains or raises grief.</summary>
        Externalized
    }

    /// <summary>
    /// One bereavement: a mourner's evolving relationship to a specific death.
    /// </summary>
    /// <param name="DeceasedId">The person who died.</param>
    /// <param name="KinRole">The mourner's kin relationship to the deceased at the time of death.</param>
    /// <param name="BondStrength">Strength of the lost bond at the moment of death, 0..100.</param>
    /// <param name="OnsetTime">When the loss was registered.</param>
    /// <param name="Trajectory">The assigned grief trajectory class.</param>
    /// <param name="GriefIntensity">Current grief intensity readout, 0..100.</param>
    /// <param name="LoRoWeight">
    /// Dual-Process Model oscillator position: 1 = full loss-orientation (active grieving),
    /// 0 = full restoration-orientation. Oscillates ("waves of grief") under a declining envelope —
    /// it is <b>not</b> a monotonic timer (Stroebe &amp; Schut 2010 <i>OMEGA</i> 61:273).
    /// </param>
    /// <param name="Bond">The continuing-bond type.</param>
    /// <param name="Buried">Whether the deceased has been physically buried (Stage 2 world burial).</param>
    public sealed record LossRecord(
        HumanId DeceasedId,
        KinRole KinRole,
        double BondStrength,
        WDateTime OnsetTime,
        GriefTrajectory Trajectory,
        double GriefIntensity,
        double LoRoWeight,
        ContinuingBond Bond,
        bool Buried);

    /// <summary>Persistent bereavement state: the set of losses a character is currently grieving.</summary>
    /// <param name="Losses">Active loss records; resolved losses are dropped.</param>
    public sealed record BereavementState(IReadOnlyList<LossRecord> Losses)
    {
        /// <summary>The empty state — no active grief.</summary>
        public static BereavementState Empty { get; } = new(Array.Empty<LossRecord>());
    }
}
