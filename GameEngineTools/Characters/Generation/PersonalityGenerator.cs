// PersonalityGenerator.cs
// Copyright (c) 50PSoftware

using GameEngineTools.Characters.Core;
using GameEngineTools.Characters.Hosting.Defaults;
using GameEngineTools.Characters.Traits;

namespace GameEngineTools.Characters.Generation;

/// <summary>
/// Ne-lokalizovaný, parametrický generátor Personality (BigFive + ostatní složky).
/// - Deterministický se seedem, jinak náhodný.
/// - Umí jemné korelace mezi BigFive a odvozuje MotivationWeights z rysů.
/// - Nepoužívá žádné reálné distribuce; čísla jsou kulturně agnostická.
/// </summary>
public interface IPersonalityGenerator
{
    Personality Generate(int? seed = null, PersonalityHints? hints = null, PersonalitySpec? spec = null);
}

/// <summary>
/// Volitelné fixy/omezení.
/// </summary>
public sealed record PersonalityHints(
    double? Openness = null,
    double? Conscientiousness = null,
    double? Extraversion = null,
    double? Agreeableness = null,
    double? Neuroticism = null,
    AttachmentStyle? Attachment = null,
    CommunicationStyle? Communication = null,
    Chronotype? Chronotype = null,
    Sociosexuality? Sociosexuality = null
)
{
    /// <summary>
    /// Returns hard personality constraints appropriate for the given life stage.
    /// These fix values that cannot vary — e.g. a child cannot be sexually unrestricted.
    /// Soft statistical adjustments (BigFive distributions) are handled by
    /// <see cref="PersonalitySpec.ForStadium"/>.
    /// </summary>
    /// <param name="stadium">Character's life stage.</param>
    /// <returns>
    /// A <see cref="PersonalityHints"/> instance with non-null fields only where
    /// the stadium imposes a hard constraint. Remaining fields stay <c>null</c>
    /// so the generator retains its normal randomness for those traits.
    /// </returns>
    public static PersonalityHints ForStadium(StadiumType stadium) => stadium switch
    {
        // Baby — no sexual dimension at all, direct communication only
        StadiumType.Baby => new PersonalityHints(
            Sociosexuality: Characters.Traits.Sociosexuality.Restricted,
            Communication: CommunicationStyle.Direct),  // babies are very direct :)

        // Child — no sexual dimension, attachment style still forming (Secure bias)
        StadiumType.Child => new PersonalityHints(
            Sociosexuality: Characters.Traits.Sociosexuality.Restricted),

        // Teenager — restricted to Intermediate at most (no Unrestricted)
        StadiumType.Teenager => new PersonalityHints(
            Sociosexuality: Characters.Traits.Sociosexuality.Intermediate),

        // Adult, MidAged, Old — no hard constraints
        _ => new PersonalityHints()
    };
}

/// <summary>
/// Parametry generátoru – střed, rozptyl a korelace BigFive + mapování na motivace.
/// Vše v [0..1].
/// </summary>
public sealed record PersonalitySpec(
    // BigFive baselines
    (double Mean, double Dev) O,
    (double Mean, double Dev) C,
    (double Mean, double Dev) E,
    (double Mean, double Dev) A,
    (double Mean, double Dev) N,

    // Korelace mezi rysy (sym. 5x5, diag = 1). Hodnoty -1..1. Slabé defaulty.
    double[,] Corr,

    // Volby mimo BigFive
    (double Secure, double Anxious, double Avoidant, double Disorganized) AttachmentWeights,
    (double Indirect, double Direct, double LowContext, double HighContext) CommunicationWeights,
    (double Lark, double Neutral, double Owl) ChronotypeWeights,
    (double Restricted, double Intermediate, double Unrestricted) SociosexualityWeights,

    // Mapování BigFive -> MotivationWeights (lineární mix)
    MotivationMapping MotivationMap
)
{
    public static readonly PersonalitySpec Default
    {
        get
        {
            var corr = new double[5, 5]
            {
                { 1.00,  0.10,  0.10,  0.15, -0.10 },
                { 0.10,  1.00,  0.10,  0.10, -0.15 },
                { 0.10,  0.10,  1.00,  0.05, -0.10 },
                { 0.15,  0.10,  0.05,  1.00, -0.10 },
                {-0.10, -0.15, -0.10, -0.10,  1.00 }
            };
            return new PersonalitySpec(
                O: (0.50, 0.16),
                C: (0.50, 0.16),
                E: (0.50, 0.18),
                A: (0.50, 0.16),
                N: (0.50, 0.18),
                Corr: corr,
                AttachmentWeights: (Secure: 0.55, Anxious: 0.15, Avoidant: 0.15, Disorganized: 0.15),
                CommunicationWeights: (Indirect: 0.25, Direct: 0.25, LowContext: 0.25, HighContext: 0.25),
                ChronotypeWeights: (Lark: 0.33, Neutral: 0.34, Owl: 0.33),
                SociosexualityWeights: (Restricted: 0.33, Intermediate: 0.34, Unrestricted: 0.33),
                MotivationMap: MotivationMapping.Default
            );
        }
    }

    /// <summary>
    /// Returns a <see cref="PersonalitySpec"/> with BigFive distributions and motivation
    /// mappings calibrated for the given life stage.
    /// </summary>
    /// <remarks>
    /// Key adjustments per stage:
    /// <list type="bullet">
    ///   <item><b>Baby</b> — Sexuality bias zeroed, high Neuroticism, low Conscientiousness.</item>
    ///   <item><b>Child</b> — Sexuality bias zeroed, high Openness, developing Conscientiousness.</item>
    ///   <item><b>Teenager</b> — High Neuroticism variance (puberty volatility), elevated Sexuality bias.</item>
    ///   <item><b>MidAged</b> — Lower Neuroticism, higher Conscientiousness (maturity).</item>
    ///   <item><b>Old</b> — Lower Openness and Sexuality, higher Conscientiousness.</item>
    ///   <item><b>Adult</b> — <see cref="Default"/> — no adjustments.</item>
    /// </list>
    /// </remarks>
    public static PersonalitySpec ForStadium(StadiumType stadium) => stadium switch
    {
        StadiumType.Baby => BabySpec(),
        StadiumType.Child => ChildSpec(),
        StadiumType.Teenager => TeenagerSpec(),
        StadiumType.MidAged => MidAgedSpec(),
        StadiumType.Old => OldSpec(),
        _ => Default  // Adult
    };

    // ── Private stadium spec factories ──────────────────────────────────────────

    /// <summary>
    /// Baby spec: no sexuality, high emotional reactivity, very low conscientiousness.
    /// </summary>
    private static PersonalitySpec BabySpec()
    {
        var m = MotivationMapping.Default;
        return Default with
        {
            N = (Mean: 0.70, Dev: 0.15),   // high emotional reactivity
            C = (Mean: 0.15, Dev: 0.10),   // almost no self-regulation yet
            O = (Mean: 0.75, Dev: 0.12),   // everything is new and interesting
            A = (Mean: 0.70, Dev: 0.12),   // dependent, trusting
            MotivationMap = m with
            {
                // Zero out sexual motivation — not applicable
                BiasSex = 0.0,
                WSex = (0, 0, 0, 0, 0),
                // Boost rest and affiliation — core baby needs
                BiasRes = 0.80,
                BiasAff = 0.75
            }
        };
    }

    /// <summary>
    /// Child spec: curiosity-driven, no sexuality, Conscientiousness still developing.
    /// </summary>
    private static PersonalitySpec ChildSpec()
    {
        var m = MotivationMapping.Default;
        return Default with
        {
            O = (Mean: 0.65, Dev: 0.14),   // naturally curious
            C = (Mean: 0.35, Dev: 0.15),   // developing self-control
            N = (Mean: 0.55, Dev: 0.16),   // still emotionally reactive
            A = (Mean: 0.60, Dev: 0.14),
            MotivationMap = m with
            {
                BiasSex = 0.0,
                WSex = (0, 0, 0, 0, 0),
                // Play and curiosity are dominant
                BiasCur = 0.70,
                BiasAff = 0.65
            }
        };
    }

    /// <summary>
    /// Teenager spec: puberty volatility via high Neuroticism variance,
    /// elevated but not dominant sexuality, forming identity (higher Openness).
    /// </summary>
    private static PersonalitySpec TeenagerSpec()
    {
        var m = MotivationMapping.Default;
        return Default with
        {
            N = (Mean: 0.58, Dev: 0.22),   // high variance — puberty volatility
            O = (Mean: 0.62, Dev: 0.16),   // identity exploration
            C = (Mean: 0.42, Dev: 0.18),   // still developing
            A = (Mean: 0.44, Dev: 0.16),
            MotivationMap = m with
            {
                // Sexuality starts developing but not dominant yet
                BiasSex = 0.30,
                // Strong affiliation need — peer belonging
                BiasAff = 0.65
            }
        };
    }

    /// <summary>
    /// MidAged spec: emotional maturity — lower Neuroticism, higher Conscientiousness.
    /// </summary>
    private static PersonalitySpec MidAgedSpec() => Default with
    {
        N = (Mean: 0.42, Dev: 0.15),   // more emotionally stable
        C = (Mean: 0.58, Dev: 0.14),   // more disciplined
        A = (Mean: 0.58, Dev: 0.13)    // slightly more agreeable
    };

    /// <summary>
    /// Old spec: lower Openness (set in ways), higher Conscientiousness,
    /// reduced sexuality. Still full variance on most traits.
    /// </summary>
    private static PersonalitySpec OldSpec()
    {
        var m = MotivationMapping.Default;
        return Default with
        {
            O = (Mean: 0.44, Dev: 0.14),   // less open to novelty
            C = (Mean: 0.62, Dev: 0.12),   // disciplined, routine-oriented
            N = (Mean: 0.38, Dev: 0.14),   // generally more stable
            E = (Mean: 0.44, Dev: 0.14),
            MotivationMap = m with
            {
                BiasSex = 0.20,            // reduced but not zero
                WSex = (0.02, -0.05, 0.10, 0, 0.02),
                // Rest becomes more important
                BiasRes = 0.65
            }
        };
    }
}

/// <summary>
/// Jak se BigFive promítají do MotivationWeights.
/// Každý výstup = bias + wO*O + wC*C + wE*E + wA*A + wN*N, následně normalizace do [0..1].
/// </summary>
public sealed record MotivationMapping(
    // pořadí: Affiliation, Achievement, Power, Altruism, Competence, Autonomy, Curiosity, Rest, Sexuality
    double BiasAff, (double wO, double wC, double wE, double wA, double wN) WAff,
    double BiasAch, (double wO, double wC, double wE, double wA, double wN) WAch,
    double BiasPow, (double wO, double wC, double wE, double wA, double wN) WPow,
    double BiasAlt, (double wO, double wC, double wE, double wA, double wN) WAlt,
    double BiasCmp, (double wO, double wC, double wE, double wA, double wN) WCmp,
    double BiasAut, (double wO, double wC, double wE, double wA, double wN) WAut,
    double BiasCur, (double wO, double wC, double wE, double wA, double wN) WCur,
    double BiasRes, (double wO, double wC, double wE, double wA, double wN) WRes,
    double BiasSex, (double wO, double wC, double wE, double wA, double wN) WSex
)
{
    public static readonly MotivationMapping Default => new(
        BiasAff: 0.50, WAff: (wO: +0.10, wC: -0.05, wE: +0.25, wA: +0.20, wN: -0.05), // extraverze/agreeab.
        BiasAch: 0.50, WAch: (wO: 0.00, wC: +0.30, wE: +0.05, wA: +0.05, wN: -0.10), // conscientiousness
        BiasPow: 0.45, WPow: (wO: -0.05, wC: +0.10, wE: +0.25, wA: -0.15, wN: +0.05), // dominance vs. agreeab.
        BiasAlt: 0.50, WAlt: (wO: +0.05, wC: 0.00, wE: -0.05, wA: +0.35, wN: -0.05), // prosocialita
        BiasCmp: 0.52, WCmp: (wO: +0.08, wC: +0.34, wE: 0.04, wA: 0.02, wN: -0.12),
        BiasAut: 0.50, WAut: (wO: +0.10, wC: +0.05, wE: -0.10, wA: -0.10, wN: +0.05), // autonomie vs. afiliace
        BiasCur: 0.50, WCur: (wO: +0.35, wC: 0.00, wE: +0.05, wA: 0.00, wN: -0.05), // otevřenost
        BiasRes: 0.50, WRes: (wO: -0.05, wC: +0.05, wE: -0.10, wA: +0.05, wN: +0.30), // neurotic.=potřeba odpočinku
        BiasSex: 0.50, WSex: (wO: +0.05, wC: -0.10, wE: +0.20, wA: 0.00, wN: +0.05)
    );
}

public sealed class PersonalityGenerator : IPersonalityGenerator
{
    private readonly IRandomSourceFactory _rngFactory;

    public PersonalityGenerator(IRandomSourceFactory rngFactory) => _rngFactory = rngFactory;

    public Personality Generate(int? seed = null, PersonalityHints? hints = null, PersonalitySpec? spec = null)
    {
        spec ??= PersonalitySpec.Default;
        hints ??= new PersonalityHints();
        var rng = _rngFactory.Create(seed ?? Environment.TickCount);

        // 1) vygeneruj korelované BigFive
        var (O, C, E, A, N) = GenerateBigFive(rng, spec);

        // 2) aplikuj hinty (fixy); clamp do [0..1]
        if (hints.Openness is double o)
        {
            O = Clamp01(o);
        }

        if (hints.Conscientiousness is double c)
        {
            C = Clamp01(c);
        }

        if (hints.Extraversion is double e)
        {
            E = Clamp01(e);
        }

        if (hints.Agreeableness is double a)
        {
            A = Clamp01(a);
        }

        if (hints.Neuroticism is double n)
        {
            N = Clamp01(n);
        }

        // 3) ostatní volby – losování z vah nebo hint fix
        var attach = hints.Attachment ?? PickWeighted(
            (AttachmentStyle.Secure, spec.AttachmentWeights.Secure),
            (AttachmentStyle.Anxious, spec.AttachmentWeights.Anxious),
            (AttachmentStyle.Avoidant, spec.AttachmentWeights.Avoidant),
            (AttachmentStyle.Disorganized, spec.AttachmentWeights.Disorganized), rng);

        var comm = hints.Communication ?? PickWeighted(
            (CommunicationStyle.Indirect, spec.CommunicationWeights.Indirect),
            (CommunicationStyle.Direct, spec.CommunicationWeights.Direct),
            (CommunicationStyle.LowContext, spec.CommunicationWeights.LowContext),
            (CommunicationStyle.HighContext, spec.CommunicationWeights.HighContext), rng);

        var chrono = hints.Chronotype ?? PickWeighted(
            (Chronotype.Lark, spec.ChronotypeWeights.Lark),
            (Chronotype.Neutral, spec.ChronotypeWeights.Neutral),
            (Chronotype.Owl, spec.ChronotypeWeights.Owl), rng);

        var socio = hints.Sociosexuality ?? PickWeighted(
            (Sociosexuality.Restricted, spec.SociosexualityWeights.Restricted),
            (Sociosexuality.Intermediate, spec.SociosexualityWeights.Intermediate),
            (Sociosexuality.Unrestricted, spec.SociosexualityWeights.Unrestricted), rng);

        // 4) motivace z BigFive (lineární mix + clamp do [0..1])
        var m = spec.MotivationMap;
        double Mix(double bias, (double wO, double wC, double wE, double wA, double wN) w)
            => Clamp01(bias + w.wO * O + w.wC * C + w.wE * E + w.wA * A + w.wN * N);

        var motivation = new MotivationWeights(
            Affiliation: Mix(m.BiasAff, m.WAff),
            Achievement: Mix(m.BiasAch, m.WAch),
            Power: Mix(m.BiasPow, m.WPow),
            Altruism: Mix(m.BiasAlt, m.WAlt),
            Competence: Mix(m.BiasCmp, m.WCmp),
            Autonomy: Mix(m.BiasAut, m.WAut),
            Curiosity: Mix(m.BiasCur, m.WCur),
            Rest: Mix(m.BiasRes, m.WRes),
            Sexuality: Mix(m.BiasSex, m.WSex)
        );

        // 5) poskládej Personality
        var bigFive = new BigFive(O, C, E, A, N);
        return new Personality(bigFive, attach, comm, motivation, socio, chrono);
    }

    private static (double O, double C, double E, double A, double N) GenerateBigFive(IRandomSource rng, PersonalitySpec spec)
    {
        // použijeme jednoduchý "Gaussian copula" trik bez heavy matiky:
        // 1) vytvoř 5 nezávislých N(0,1) z rovnoměrného zdroje (sum-of-uniforms approx)
        static double G(IRandomSource r)
            => (r.NextUnit() + r.NextUnit() + r.NextUnit() + r.NextUnit() + r.NextUnit() - 2.5) / 0.612372; // ~N(0,1)

        var z = new double[5];
        for (int i = 0; i < 5; i++)
        {
            z[i] = G(rng);
        }

        // 2) zavedeme korelace lineárním mixem (Cholesky approx – tady rychlý Gram-Schmidt)
        // Pozn.: pro malé korelace stačí; pokud budeš chtít přesný Cholesky, můžeme doplnit.
        var L = CholeskyLike(spec.Corr);
        var y = new double[5];
        for (int i = 0; i < 5; i++)
        {
            double s = 0; for (int j = 0; j <= i; j++)
            {
                s += L[i, j] * z[j];
            }

            y[i] = s; // ~N(0,1) s korelacemi
        }

        // 3) škáluj na Mean/Dev a mapuj do [0..1] přes sigmoid
        double Sig(double x) => 1.0 / (1.0 + Math.Exp(-x));
        double Map((double Mean, double Dev) p, double v)
            => Clamp01(p.Mean + p.Dev * (Sig(v) - 0.5) * 2.0); // převod N->(0..1) s kontrolou dev

        var O = Map(spec.O, y[0]);
        var C = Map(spec.C, y[1]);
        var E = Map(spec.E, y[2]);
        var A = Map(spec.A, y[3]);
        var N = Map(spec.N, y[4]);
        return (O, C, E, A, N);
    }

    private static double[,] CholeskyLike(double[,] corr)
    {
        int n = corr.GetLength(0);
        var L = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j <= i; j++)
            {
                double sum = corr[i, j];
                for (int k = 0; k < j; k++)
                {
                    sum -= L[i, k] * L[j, k];
                }

                if (i == j)
                {
                    L[i, j] = Math.Sqrt(Math.Max(sum, 1e-6));
                }
                else
                {
                    L[i, j] = sum / L[j, j];
                }
            }
        }
        return L;
    }

    private static T PickWeighted<T>(
        (T item, double w) a,
        (T item, double w) b,
        (T item, double w) c,
        IRandomSource rng)
    {
        var r = rng.NextUnit();
        var s = a.w;
        if (r <= s)
        {
            return a.item;
        }

        s += b.w;
        if (r <= s)
        {
            return b.item;
        }

        return c.item;
    }

    private static T PickWeighted<T>(
        (T item, double w) a,
        (T item, double w) b,
        (T item, double w) c,
        (T item, double w) d,
        IRandomSource rng)
    {
        var r = rng.NextUnit();
        var s = a.w;
        if (r <= s)
        {
            return a.item;
        }

        s += b.w; if (r <= s)
        {
            return b.item;
        }

        s += c.w; if (r <= s)
        {
            return c.item;
        }

        return d.item;
    }

    private static double Clamp01(double x) => x < 0 ? 0 : (x > 1 ? 1 : x);
}
