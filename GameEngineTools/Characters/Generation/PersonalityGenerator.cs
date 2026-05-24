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
    AttachmentProfile? Attachment = null,
    CommunicationStyle? Communication = null,
    Chronotype? Chronotype = null,
    Sociosexuality? Sociosexuality = null,
    /// <summary>
    /// When set, clamps only the <see cref="Traits.Sociosexuality.Behavior"/> facet after generation.
    /// Allows Attitude and Desire to vary freely while hard-capping experiential history.
    /// Takes effect only when <see cref="Sociosexuality"/> is <c>null</c> (per-facet cap mode).
    /// </summary>
    double? SociosexualityBehaviorMax = null
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

        // Teenager — Attitude and Desire generated freely up to Intermediate (0.50),
        // but Behavior is hard-capped at 0.25: a teenager simply has not had time
        // to accumulate a high-casualty history (SOI-R Behavior = past partner count).
        StadiumType.Teenager => new PersonalityHints(
            Sociosexuality: Characters.Traits.Sociosexuality.Intermediate,
            SociosexualityBehaviorMax: 0.25),

        // Adult, MidAged, Old — no hard constraints
        _ => new PersonalityHints()
    };
}

public sealed record TraitDistribution(
    double Mean,
    double Dev,
    double Skew = 0.0,
    double Concentration = 1.0
)
{
    public double ClampedMean => Math.Clamp(Mean, 1e-6, 1.0 - 1e-6);
    public double ClampedDev => Math.Max(Dev, 1e-6);
    public double ClampedConcentration => Math.Max(Concentration, 1e-6);
}

/// <summary>
/// Parametry generátoru – střed, rozptyl a korelace BigFive + mapování na motivace.
/// Vše v [0..1].
/// </summary>
public sealed record PersonalitySpec(
    // BigFive baselines
    TraitDistribution O,
    TraitDistribution C,
    TraitDistribution E,
    TraitDistribution A,
    TraitDistribution N,

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
    public static PersonalitySpec Default
    {
        get
        {
            var corr = new double[5, 5]
            {
                { 1.00,  0.12,  0.12,  0.15, -0.12 },    // O
                { 0.12,  1.00,  0.10,  0.10, -0.35 },    // C
                { 0.12,  0.10,  1.00,  0.08, -0.20 },    // E
                { 0.15,  0.10,  0.08,  1.00, -0.20 },    // A
                {-0.12, -0.35, -0.20, -0.20,  1.00 }     // N
            };
            return new PersonalitySpec(
                O: new TraitDistribution(Mean: 0.50, Dev: 0.16, Skew: 0.00, Concentration: 1.0),
                C: new TraitDistribution(Mean: 0.50, Dev: 0.16, Skew: 0.00, Concentration: 1.1),
                E: new TraitDistribution(Mean: 0.50, Dev: 0.18, Skew: 0.00, Concentration: 0.95),
                A: new TraitDistribution(Mean: 0.50, Dev: 0.16, Skew: 0.05, Concentration: 1.05),
                N: new TraitDistribution(Mean: 0.50, Dev: 0.18, Skew: 0.05, Concentration: 0.95),
                Corr: corr,
                AttachmentWeights: (Secure: 0.58, Anxious: 0.18, Avoidant: 0.18, Disorganized: 0.06),
                CommunicationWeights: (Indirect: 0.25, Direct: 0.25, LowContext: 0.25, HighContext: 0.25),
                ChronotypeWeights: (Lark: 0.33, Neutral: 0.34, Owl: 0.33),
                SociosexualityWeights: (Restricted: 0.40, Intermediate: 0.35, Unrestricted: 0.25),
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
            O = new TraitDistribution(Mean: 0.72, Dev: 0.10, Skew: +0.08, Concentration: 1.15),
            C = new TraitDistribution(Mean: 0.15, Dev: 0.08, Skew: +0.20, Concentration: 1.35),
            E = new TraitDistribution(Mean: 0.52, Dev: 0.12, Skew: 0.00, Concentration: 1.05),
            A = new TraitDistribution(Mean: 0.68, Dev: 0.10, Skew: +0.10, Concentration: 1.10),
            N = new TraitDistribution(Mean: 0.72, Dev: 0.12, Skew: -0.10, Concentration: 1.00),
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
            O = new TraitDistribution(Mean: 0.66, Dev: 0.13, Skew: +0.06, Concentration: 1.05),
            C = new TraitDistribution(Mean: 0.34, Dev: 0.13, Skew: +0.12, Concentration: 1.10),
            E = new TraitDistribution(Mean: 0.55, Dev: 0.15, Skew: 0.00, Concentration: 1.00),
            A = new TraitDistribution(Mean: 0.60, Dev: 0.12, Skew: +0.05, Concentration: 1.05),
            N = new TraitDistribution(Mean: 0.56, Dev: 0.14, Skew: -0.04, Concentration: 0.98),
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
            O = new TraitDistribution(Mean: 0.62, Dev: 0.16, Skew: +0.04, Concentration: 0.98),
            C = new TraitDistribution(Mean: 0.42, Dev: 0.17, Skew: +0.06, Concentration: 0.95),
            E = new TraitDistribution(Mean: 0.53, Dev: 0.18, Skew: 0.00, Concentration: 0.95),
            A = new TraitDistribution(Mean: 0.45, Dev: 0.15, Skew: -0.06, Concentration: 0.98),
            N = new TraitDistribution(Mean: 0.58, Dev: 0.20, Skew: -0.08, Concentration: 0.92),
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
        O = new TraitDistribution(Mean: 0.50, Dev: 0.15, Skew: 0.00, Concentration: 1.02),
        C = new TraitDistribution(Mean: 0.58, Dev: 0.13, Skew: -0.03, Concentration: 1.08),
        E = new TraitDistribution(Mean: 0.49, Dev: 0.16, Skew: 0.00, Concentration: 1.00),
        A = new TraitDistribution(Mean: 0.58, Dev: 0.12, Skew: -0.03, Concentration: 1.08),
        N = new TraitDistribution(Mean: 0.42, Dev: 0.14, Skew: +0.05, Concentration: 1.05)
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
            O = new TraitDistribution(Mean: 0.44, Dev: 0.13, Skew: +0.05, Concentration: 1.10),
            C = new TraitDistribution(Mean: 0.62, Dev: 0.11, Skew: -0.04, Concentration: 1.12),
            E = new TraitDistribution(Mean: 0.44, Dev: 0.13, Skew: +0.03, Concentration: 1.08),
            A = new TraitDistribution(Mean: 0.56, Dev: 0.12, Skew: -0.02, Concentration: 1.08),
            N = new TraitDistribution(Mean: 0.38, Dev: 0.13, Skew: +0.06, Concentration: 1.08),
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
    public static MotivationMapping Default => new(
        BiasAff: 0.50, WAff: (wO: +0.10, wC: -0.05, wE: +0.25, wA: +0.20, wN: -0.05), // extraverze/agreeab.
        BiasAch: 0.50, WAch: (wO: 0.00, wC: +0.30, wE: +0.05, wA: +0.05, wN: -0.10), // conscientiousness
        BiasPow: 0.45, WPow: (wO: -0.05, wC: +0.10, wE: +0.25, wA: -0.15, wN: +0.05), // dominance vs. agreeab.
        BiasAlt: 0.50, WAlt: (wO: +0.05, wC: 0.00, wE: -0.05, wA: +0.35, wN: -0.05), // prosocialita
        BiasCmp: 0.52, WCmp: (wO: +0.08, wC: +0.34, wE: 0.04, wA: 0.02, wN: -0.12),
        BiasAut: 0.50, WAut: (wO: +0.10, wC: +0.05, wE: -0.10, wA: -0.10, wN: +0.05), // autonomie vs. afiliace
        BiasCur: 0.50, WCur: (wO: +0.35, wC: 0.00, wE: +0.05, wA: 0.00, wN: -0.05), // otevřenost
        BiasRes: 0.50, WRes: (wO: -0.05, wC: +0.05, wE: -0.20, wA: +0.05, wN: +0.15),
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
            (AttachmentProfile.Secure, spec.AttachmentWeights.Secure),
            (AttachmentProfile.Preoccupied, spec.AttachmentWeights.Anxious),
            (AttachmentProfile.Dismissing, spec.AttachmentWeights.Avoidant),
            (AttachmentProfile.Fearful, spec.AttachmentWeights.Disorganized), rng);

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

        // Apply per-facet Behavior cap when a fixed Sociosexuality preset was NOT used.
        // This allows Attitude and Desire to vary freely while hard-capping
        // the experiential-history facet for life stages where high past behavior
        // is biologically implausible (e.g. teenagers).
        if (hints.SociosexualityBehaviorMax is double behaviorMax)
        {
            socio = socio with { Behavior = Math.Min(socio.Behavior, behaviorMax) };
        }

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
        return new Personality(bigFive, attach, comm, motivation, socio, chrono, SexualResponsiveness.Default);
    }

    private static (double O, double C, double E, double A, double N) GenerateBigFive(IRandomSource rng, PersonalitySpec spec)
    {
        var z = new double[5];
        for (int i = 0; i < 5; i++)
        {
            z[i] = NextStandardNormal(rng);
        }

        var L = CholeskyRobust(spec.Corr);
        var y = new double[5];
        for (int i = 0; i < 5; i++)
        {
            double s = 0;
            for (int j = 0; j <= i; j++)
            {
                s += L[i, j] * z[j];
            }

            y[i] = s;
        }

        return (
            O: MapToBoundedTrait(spec.O, y[0]),
            C: MapToBoundedTrait(spec.C, y[1]),
            E: MapToBoundedTrait(spec.E, y[2]),
            A: MapToBoundedTrait(spec.A, y[3]),
            N: MapToBoundedTrait(spec.N, y[4])
        );
    }

    private static double NextStandardNormal(IRandomSource rng)
    {
        while (true)
        {
            var u = 2.0 * rng.NextUnit() - 1.0;
            var v = 2.0 * rng.NextUnit() - 1.0;
            var s = u * u + v * v;

            if (s <= 0 || s >= 1)
            {
                continue;
            }

            var mul = Math.Sqrt(-2.0 * Math.Log(s) / s);
            return u * mul;
        }
    }

    private static double[,] CholeskyRobust(double[,] corr, double jitter = 1e-10)
    {
        int n = corr.GetLength(0);

        if (corr.GetLength(1) != n)
        {
            throw new ArgumentException("Correlation matrix must be square.");
        }

        // kontrola symetrie + diagonály
        for (int i = 0; i < n; i++)
        {
            if (Math.Abs(corr[i, i] - 1.0) > 1e-8)
            {
                throw new ArgumentException($"Diagonal element [{i},{i}] must be 1.0.");
            }

            for (int j = i + 1; j < n; j++)
            {
                if (Math.Abs(corr[i, j] - corr[j, i]) > 1e-8)
                {
                    throw new ArgumentException("Correlation matrix must be symmetric.");
                }
            }
        }

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
                    if (sum < jitter)
                    {
                        sum = jitter;
                    }

                    L[i, j] = Math.Sqrt(sum);
                }
                else
                {
                    L[i, j] = sum / L[j, j];
                }
            }
        }

        return L;
    }

    private static double NormalCdf(double x)
    {
        // Abramowitz-Stegun-like erf approximation
        double t = 1.0 / (1.0 + 0.2316419 * Math.Abs(x));
        double d = 0.3989422804014327 * Math.Exp(-x * x / 2.0);

        double prob = d * t *
            (0.319381530
            + t * (-0.356563782
            + t * (1.781477937
            + t * (-1.821255978
            + t * 1.330274429))));

        return x > 0.0 ? 1.0 - prob : prob;
    }

    private static double InverseNormalCdf(double p)
    {
        if (p <= 0.0 || p >= 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(p), "p must be in (0,1).");
        }

        // Peter J. Acklam approximation
        double[] a =
        {
            -3.969683028665376e+01,
             2.209460984245205e+02,
            -2.759285104469687e+02,
             1.383577518672690e+02,
            -3.066479806614716e+01,
             2.506628277459239e+00
        };

        double[] b =
        {
            -5.447609879822406e+01,
             1.615858368580409e+02,
            -1.556989798598866e+02,
             6.680131188771972e+01,
            -1.328068155288572e+01
        };

        double[] c =
        {
            -7.784894002430293e-03,
            -3.223964580411365e-01,
            -2.400758277161838e+00,
            -2.549732539343734e+00,
             4.374664141464968e+00,
             2.938163982698783e+00
        };

        double[] d =
        {
             7.784695709041462e-03,
             3.224671290700398e-01,
             2.445134137142996e+00,
             3.754408661907416e+00
        };

        const double plow = 0.02425;
        const double phigh = 1.0 - plow;

        if (p < plow)
        {
            double q = Math.Sqrt(-2.0 * Math.Log(p));
            return (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) /
                   ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1.0);
        }

        if (p > phigh)
        {
            double q = Math.Sqrt(-2.0 * Math.Log(1.0 - p));
            return -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) /
                     ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1.0);
        }

        double r = p - 0.5;
        double s = r * r;
        return (((((a[0] * s + a[1]) * s + a[2]) * s + a[3]) * s + a[4]) * s + a[5]) * r /
               (((((b[0] * s + b[1]) * s + b[2]) * s + b[3]) * s + b[4]) * s + 1.0);
    }

    private static double StandardNormalPdf(double x)
        => 0.3989422804014327 * Math.Exp(-0.5 * x * x);

    private static double MapToBoundedTrait(TraitDistribution p, double latent)
    {
        double mean = p.ClampedMean;
        double dev = p.ClampedDev;

        // latent mean
        double mu = InverseNormalCdf(mean);

        // delta-method approx: sd_out ≈ phi(mu) * sigma_latent
        // => sigma_latent ≈ sd_out / phi(mu)
        double phi = Math.Max(StandardNormalPdf(mu), 1e-4);
        double sigma = dev / phi;

        sigma /= p.ClampedConcentration;

        // ochrana proti přehnanému roztahování
        sigma = Math.Clamp(sigma, 0.05, 3.0);

        var x = mu + sigma * latent;

        // lehká asymetrie v latentním prostoru
        if (Math.Abs(p.Skew) > 1e-9)
        {
            x += p.Skew * 0.35 * (latent * latent - 1.0);
        }

        return Clamp01(NormalCdf(x));
    }

    #region Old helper methods

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

    #endregion Old helper methods

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
