// MemoryWhatParser.cs
// Copyright (c) 50PSoftware
//
// Pomocný parser pro What schema v EpisodicMemory.
//
// Schema formát: {Kategorie}:{Typ}:{Výsledek}|{klíč}={hodnota}|{klíč}={hodnota}
// Příklad:       Interaction:SmallTalk:Accepted|from=a3f2c1d0|to=b7e9a2f1
//
// Pravidla schematu:
//   · Hlavička (před prvním |) je vždy přítomna — Kategorie:Typ nebo Kategorie:Typ:Výsledek
//   · Parametry (za |) jsou volitelné — klíč=hodnota páry oddělené |
//   · HumanId se zkracuje na prvních 8 znaků N-formátu (bez pomlček) — čitelné, unikátní pro logy
//   · What je deterministický klíč pro reinforcement v Encode() — NESMÍ obsahovat timestamp

namespace GameEngineTools.Characters.Engines.Memory
{
    using System;

    /// <summary>
    /// Statická pomocná třída pro sestavování a parsování <c>What</c> řetězce
    /// v <see cref="EpisodicMemory"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Proč samostatná třída, ne metody přímo v enginu?</b><br/>
    /// SRP — parser je zodpovědný za formát, engine za logiku kódování.
    /// Navíc je parser testovatelný izolovaně bez závislosti na celém enginu.
    /// </para>
    /// <para>
    /// <b>Dual purpose <c>What</c> řetězce:</b>
    /// <list type="number">
    ///   <item>
    ///     <b>Reinforcement klíč</b> — <see cref="DefaultMemoryEngine.Encode"/> hledá existující
    ///     epizodu se stejným <c>What</c>. Pokud ji najde, posílí ji místo vytvoření nové.
    ///     Proto musí být <c>What</c> deterministický pro stejný typ události se stejnými aktéry.
    ///   </item>
    ///   <item>
    ///     <b>Narativní zdroj</b> — <c>DefaultNarrativeFormatter</c> parsuje parametry
    ///     a překládá je na čitelnou větu. HumanId se překládá přes resolver na jméno.
    ///   </item>
    /// </list>
    /// </para>
    /// </remarks>
    internal static class MemoryWhatParser
    {
        // ══════════════════════════════════════════════════════════════════════════
        // Sestavení What řetězce
        // ══════════════════════════════════════════════════════════════════════════

        #region Build — sestavení What

        /// <summary>
        /// Sestaví <c>What</c> pro interakci (SpeechAct mezi dvěma postavami).
        /// </summary>
        /// <remarks>
        /// Výsledný formát: <c>Interaction:{Act}:{Accepted|Rejected}|from={id}|to={id}</c>
        /// </remarks>
        /// <param name="act">Typ řečového aktu (SmallTalk, Humor…).</param>
        /// <param name="accepted">Zda byla interakce přijata.</param>
        /// <param name="from">ID iniciátora.</param>
        /// <param name="to">ID příjemce.</param>
        public static string Interaction(string act, bool accepted, Guid from, Guid to)
            => $"Interaction:{act}:{(accepted ? "Accepted" : "Rejected")}" +
               $"|from={Full(from)}|to={Full(to)}";

        /// <summary>
        /// Sestaví <c>What</c> pro provedenou akci (vlastní akce postavy).
        /// </summary>
        /// <remarks>
        /// Výsledný formát: <c>Action:{ActionName}</c><br/>
        /// Žádné parametry — postava si pamatuje vlastní akci, ne cizí aktéry.
        /// </remarks>
        /// <param name="actionName">Název akce z <see cref="ActionNames"/>.</param>
        public static string Action(string actionName)
            => $"Action:{actionName}";

        /// <summary>
        /// Sestaví <c>What</c> pro ukončení spánku.
        /// </summary>
        /// <remarks>
        /// Výsledný formát: <c>Sleep:Ended:{High|Medium|Low|Poor}|hours={h:F1}</c><br/>
        /// Kvalita spánku je diskretizována — ne přesné číslo (to by bránilo reinforcement).
        /// </remarks>
        /// <param name="quality">Kvalita spánku 0–100.</param>
        /// <param name="totalHours">Délka spánku v hodinách.</param>
        public static string SleepEnded(double quality, double totalHours)
        {
            // Diskretizace kvality — přesné číslo by bránilo reinforcement
            // (každou noc trochu jiná kvalita → nikdy by se stejný What neopakoval)
            var qualityBucket = quality switch
            {
                >= 80 => "High",
                >= 55 => "Medium",
                >= 30 => "Low",
                _     => "Poor"
            };
            return $"Sleep:Ended:{qualityBucket}|hours={totalHours:F1}";
        }

        /// <summary>
        /// Sestaví <c>What</c> pro noční můru.
        /// </summary>
        /// <remarks>
        /// Výsledný formát: <c>Sleep:Nightmare|stress={High|Medium|Low}</c>
        /// </remarks>
        /// <param name="stressAtSleep">Hodnota stresu v okamžiku usnutí (0–100).</param>
        public static string Nightmare(double stressAtSleep)
        {
            var stressBucket = stressAtSleep switch
            {
                >= 70 => "High",
                >= 40 => "Medium",
                _     => "Low"
            };
            return $"Sleep:Nightmare|stress={stressBucket}";
        }

        /// <summary>
        /// Sestaví <c>What</c> pro první dojem z nové postavy.
        /// </summary>
        /// <remarks>
        /// Výsledný formát: <c>Relation:FirstImpression:{Positive|Neutral|Negative}|of={id}</c>
        /// </remarks>
        /// <param name="like">Hodnota Like z <see cref="FirstImpressionFormed"/> (0–100).</param>
        /// <param name="of">ID postavy, na kterou dojem vznikl.</param>
        public static string FirstImpression(double like, Guid of)
        {
            var sentiment = like switch
            {
                >= 70 => "Positive",
                >= 45 => "Neutral",
                _     => "Negative"
            };
            return $"Relation:FirstImpression:{sentiment}|of={Full(of)}";
        }

        /// <summary>
        /// Sestaví <c>What</c> pro mikrokladnou interakci.
        /// </summary>
        /// <remarks>
        /// Výsledný formát: <c>Relation:MicroPositive|from={id}|what={what}</c>
        /// </remarks>
        public static string MicroPositive(Guid from, string what)
            => $"Relation:MicroPositive|from={Full(from)}|what={what}";

        /// <summary>
        /// Sestaví <c>What</c> pro mikrozápornou interakci.
        /// </summary>
        /// <remarks>
        /// Výsledný formát: <c>Relation:MicroNegative|from={id}|what={what}</c>
        /// </remarks>
        public static string MicroNegative(Guid from, string what)
            => $"Relation:MicroNegative|from={Full(from)}|what={what}";

        /// <summary>
        /// Sestaví <c>What</c> pro pokus o smíření.
        /// </summary>
        /// <remarks>
        /// Výsledný formát: <c>Relation:Repair:{Accepted|Rejected}|with={id}</c>
        /// </remarks>
        public static string RepairAttempt(bool accepted, Guid with)
            => $"Relation:Repair:{(accepted ? "Accepted" : "Rejected")}|with={Full(with)}";

        #endregion

        // ══════════════════════════════════════════════════════════════════════════
        // Parsování What řetězce
        // ══════════════════════════════════════════════════════════════════════════

        #region Parse — čtení What

        /// <summary>
        /// Vrátí hlavičku <c>What</c> řetězce — část před prvním <c>|</c>.
        /// </summary>
        /// <param name="what">Kompletní <c>What</c> řetězec.</param>
        /// <returns>Hlavička, např. <c>"Interaction:SmallTalk:Accepted"</c>.</returns>
        public static string GetHeader(string what)
        {
            var idx = what.IndexOf('|');
            return idx < 0 ? what : what[..idx];
        }

        /// <summary>
        /// Vrátí hodnotu pojmenovaného parametru z <c>What</c> řetězce.
        /// </summary>
        /// <param name="what">Kompletní <c>What</c> řetězec.</param>
        /// <param name="key">Název parametru (např. <c>"from"</c>, <c>"to"</c>).</param>
        /// <returns>Hodnota parametru, nebo <c>null</c> pokud parametr neexistuje.</returns>
        /// <example>
        /// <code>
        /// var what = "Interaction:SmallTalk:Accepted|from=a3f2c1d0|to=b7e9a2f1";
        /// var from = MemoryWhatParser.GetParam(what, "from"); // → "a3f2c1d0"
        /// var to   = MemoryWhatParser.GetParam(what, "to");   // → "b7e9a2f1"
        /// </code>
        /// </example>
        public static string? GetParam(string what, string key)
        {
            // Přeskočíme hlavičku (část před prvním |) a iterujeme přes parametry
            var parts = what.AsSpan();
            var firstPipe = parts.IndexOf('|');
            if (firstPipe < 0) return null;

            var remaining = parts[(firstPipe + 1)..];

            while (remaining.Length > 0)
            {
                // Najdi další | nebo konec řetězce
                var next = remaining.IndexOf('|');
                var segment = next < 0 ? remaining : remaining[..next];

                // Rozděl na klíč=hodnota
                var eq = segment.IndexOf('=');
                if (eq > 0)
                {
                    var segKey = segment[..eq];
                    if (segKey.SequenceEqual(key.AsSpan()))
                        return segment[(eq + 1)..].ToString();
                }

                if (next < 0) break;
                remaining = remaining[(next + 1)..];
            }

            return null;
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════════════
        // Privátní pomocné metody
        // ══════════════════════════════════════════════════════════════════════════

        #region Privátní — Full ID

        /// <summary>
        /// Vrátí Guid ve stringu.
        /// </summary>
        private static string Full(Guid id)
            => id.ToString("N");

        #endregion
    }
}
