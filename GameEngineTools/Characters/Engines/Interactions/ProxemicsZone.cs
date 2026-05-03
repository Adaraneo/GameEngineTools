// ProxemicsZone.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Interactions
{
    /// <summary>
    /// Altman (1975) interpersonal distance zones.
    /// Violation of the intimate zone by an unwanted party → acute stress response.
    /// </summary>
    public enum ProxemicsZone
    {
        /// <summary>0–0.45 m — reserved for intimate partners or close family.</summary>
        Intimate,

        /// <summary>0.45–1.2 m — friends and acquaintances.</summary>
        Personal,

        /// <summary>1.2–3.6 m — social acquaintances.</summary>
        Social,

        /// <summary>&gt; 3.6 m — public distance.</summary>
        Public
    }

    /// <summary>Static helpers for proxemics zone classification and stress calculation.</summary>
    public static class ProxemicsHelper
    {
        /// <summary>Classify a distance in metres into the corresponding Altman zone.</summary>
        public static ProxemicsZone GetZone(double distanceMeters)
        {
            if (distanceMeters < 0.45) return ProxemicsZone.Intimate;
            if (distanceMeters < 1.20) return ProxemicsZone.Personal;
            if (distanceMeters < 3.60) return ProxemicsZone.Social;
            return ProxemicsZone.Public;
        }

        /// <summary>
        /// Returns true when the given zone constitutes a violation for the observer.
        /// An intimate zone is violated when there is no privacy and the nearest person
        /// is within 0.45 m — even brief exposure produces strong discomfort.
        /// A personal zone is mildly violating in a Public or Work context.
        /// </summary>
        public static bool IsZoneViolation(ProxemicsZone zone, bool hasPrivacy, SurfaceKind surface)
        {
            return zone switch
            {
                ProxemicsZone.Intimate => !hasPrivacy,
                ProxemicsZone.Personal => !hasPrivacy
                    && surface is SurfaceKind.Public or SurfaceKind.Work,
                _ => false
            };
        }
    }
}
