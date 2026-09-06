using System.Numerics;
using GameEngineTools.World.Data;
using MathNet.Numerics.IntegralTransforms;

namespace TerraGen.Generation;

/// <summary>Smith &amp; Barstad (2004) linear orographic precipitation model. Source: Smith, R.B. &amp; Barstad, I. (2004), "A linear theory of orographic precipitation", J. Atmos. Sci. 61:1377-1391.</summary>
/// <remarks>Returns a RELATIVE precipitation-weighting field normalized around 1.0, not calibrated absolute mm/yr — Task 4.1.3 only needs a weight on drainage area, so the model's own uplift-sensitivity amplitude (which only scales the field uniformly) is normalized away rather than separately calibrated. ⚠ Design simplification, but the windward/leeward SHAPE the linear theory predicts is preserved exactly.</remarks>
public static class OrographicPrecipitation
{
    public sealed record Parameters(
        /// <summary>⚠ Design simplification: a representative synoptic wind speed, not derived per-planet.</summary>
        double WindSpeedMs = 10.0,
        /// <summary>Compass bearing the wind blows FROM (270 = from the west).</summary>
        double WindDirectionFromDeg = 270.0,
        /// <summary>Moist Brunt-Väisälä frequency. Source: Smith &amp; Barstad (2004), typical range ~0.005-0.01 /s.</summary>
        double MoistStabilityFrequencyHz = 0.005,
        /// <summary>Condensation time constant τc. Source: Roe, G.H. (2005), Annu. Rev. Earth Planet. Sci. 33:645-671 (100-2000s range); Smith et al. (2005) narrows to 800-1500s.</summary>
        double CondensationTimeSeconds = 1000.0,
        /// <summary>Fallout time constant τf, same source range as <see cref="CondensationTimeSeconds"/>.</summary>
        double FalloutTimeSeconds = 1000.0,
        /// <summary>Water vapor scale height Hw. ⚠ Design simplification: typical literature value, not planet-specific.</summary>
        double WaterVaporScaleHeightMeters = 2500.0);

    /// <summary>Computes the normalized precipitation-weight field for <paramref name="grid"/> — same length/index convention as <c>grid.Values</c>. Always positive (clamped to [0.05, 3.0] after normalization) so it's safe to use as a drainage-area multiplier without ever collapsing flow to zero.</summary>
    public static double[] ComputePrecipitationField(TerrainHeightmap grid, Parameters p)
    {
        var width = grid.Width;
        var height = grid.Height;
        var count = width * height;

        var h = new Complex[count];
        for (var i = 0; i < count; i++) h[i] = new Complex(grid.Values[i], 0.0);

        Forward2D(h, width, height);

        // Wind vector: meteorological "from" bearing -> the direction air actually FLOWS toward.
        // Sign calibrated empirically against MathNet's FFT exponent-sign convention (FourierOptions.Matlab)
        // so the windward flank comes out wetter than the leeward — see OrographicPrecipitationTests.
        var windFromRad = p.WindDirectionFromDeg * Math.PI / 180.0;
        var u = p.WindSpeedMs * Math.Sin(windFromRad);
        var v = p.WindSpeedMs * Math.Cos(windFromRad);

        var cellSize = grid.CellSizeMeters;
        var pHat = new Complex[count];

        for (var iy = 0; iy < height; iy++)
        {
            var l = AngularWavenumber(iy, height, cellSize);
            for (var ix = 0; ix < width; ix++)
            {
                var k = AngularWavenumber(ix, width, cellSize);
                var idx = iy * width + ix;

                var sigma = u * k + v * l; // intrinsic frequency: wind projected onto this wavenumber
                if (Math.Abs(sigma) < 1e-9) { pHat[idx] = Complex.Zero; continue; } // no perturbation along the no-flow direction

                var kh2 = k * k + l * l;
                var stabilityTerm = p.MoistStabilityFrequencyHz * p.MoistStabilityFrequencyHz / (sigma * sigma) - 1.0;
                // Vertical wavenumber m: real (propagating wave) when the atmosphere is stable
                // relative to this wave's intrinsic frequency, imaginary (evanescent, decaying with
                // height) otherwise — Smith & Barstad (2004)'s own regime split.
                Complex m = stabilityTerm > 0.0
                    ? new Complex(Math.Sqrt(kh2 * stabilityTerm) * Math.Sign(sigma), 0.0)
                    : new Complex(0.0, Math.Sqrt(kh2 * -stabilityTerm));

                var numerator = new Complex(0.0, sigma) * h[idx];
                var denominator = (Complex.One - Complex.ImaginaryOne * m * p.WaterVaporScaleHeightMeters)
                                 * (Complex.One + Complex.ImaginaryOne * sigma * p.CondensationTimeSeconds)
                                 * (Complex.One + Complex.ImaginaryOne * sigma * p.FalloutTimeSeconds);
                pHat[idx] = numerator / denominator;
            }
        }

        Inverse2D(pHat, width, height);

        var perturbation = new double[count];
        var maxAbs = 0.0;
        for (var i = 0; i < count; i++)
        {
            perturbation[i] = pHat[i].Real;
            maxAbs = Math.Max(maxAbs, Math.Abs(perturbation[i]));
        }

        var scale = maxAbs > 1e-12 ? 1.0 / maxAbs : 0.0;
        var field = new double[count];
        for (var i = 0; i < count; i++)
            field[i] = Math.Clamp(1.0 + perturbation[i] * scale, 0.05, 3.0);

        return field;
    }

    /// <summary>Signed angular wavenumber (rad/m) for FFT bin <paramref name="index"/> of a length-<paramref name="length"/> axis at <paramref name="cellSize"/> spacing — standard fftfreq convention (0, +1, +2, ..., -2, -1 cycles wrapping at the Nyquist bin).</summary>
    private static double AngularWavenumber(int index, int length, double cellSize)
    {
        var cyclesPerSample = index <= length / 2 ? index : index - length;
        return 2.0 * Math.PI * cyclesPerSample / (length * cellSize);
    }

    /// <summary>2D FFT via separable 1D row-then-column passes (MathNet.Numerics has no built-in 2D transform).</summary>
    private static void Forward2D(Complex[] field, int width, int height) => TransformRowsThenColumns(field, width, height, FourierTransformConvention.Forward);

    private static void Inverse2D(Complex[] field, int width, int height) => TransformRowsThenColumns(field, width, height, FourierTransformConvention.Inverse);

    private enum FourierTransformConvention { Forward, Inverse }

    private static void TransformRowsThenColumns(Complex[] field, int width, int height, FourierTransformConvention direction)
    {
        var row = new Complex[width];
        for (var y = 0; y < height; y++)
        {
            Array.Copy(field, y * width, row, 0, width);
            RunFft(row, direction);
            Array.Copy(row, 0, field, y * width, width);
        }

        var col = new Complex[height];
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++) col[y] = field[y * width + x];
            RunFft(col, direction);
            for (var y = 0; y < height; y++) field[y * width + x] = col[y];
        }
    }

    private static void RunFft(Complex[] samples, FourierTransformConvention direction)
    {
        if (direction == FourierTransformConvention.Forward) Fourier.Forward(samples, FourierOptions.Matlab);
        else Fourier.Inverse(samples, FourierOptions.Matlab);
    }
}
