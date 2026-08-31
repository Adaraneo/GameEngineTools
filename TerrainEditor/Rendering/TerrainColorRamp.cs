using System.Windows.Media;

namespace TerrainEditor.Rendering;

/// <summary>
/// Elevation → color mapping for the heightmap canvas: blue below sea level (0m), a
/// beach → green → brown → gray → snow ramp above it. The sea-level boundary is always
/// exactly 0m (matching <c>AltitudeMeters</c>'s real-world-meters semantics) — whether a
/// cell is "underwater" never depends on this particular grid's min/max. The land and
/// water gradients each scale to the grid's own actual range, so a small local terrain
/// (tens of meters) still shows visible variation instead of a single flat tint.
/// </summary>
/// <remarks>
/// The land ceiling is <c>Max(gridMax, AbsoluteCeilingFloorMeters)</c>, not just
/// <c>gridMax</c> — purely grid-relative scaling would make the snow/rock band always sit at the
/// same top ~20% no matter how tall the terrain actually is in meters, hiding real elevation
/// differences (e.g. <c>TerrainGenerator</c>'s gravity-driven mountain-height scaling: a
/// high-gravity planet's compressed peaks would get auto-stretched right back to looking "full
/// height," and a low-gravity planet's taller ones wouldn't look any taller either). Flooring the
/// ceiling at a fixed reference means genuinely short terrain reads as visibly less snowy, while
/// genuinely tall terrain (gridMax already above the floor) still gets full contrast from its own range.
/// </remarks>
public static class TerrainColorRamp
{
    /// <summary>Reference ceiling (meters) for land coloring — roughly what a single mountain
    /// layer produces at Earth gravity with this app's default Amplituda (200m; see
    /// <c>TerrainGenerator.Parameters</c>). Terrain shorter than this floors the ceiling here
    /// instead of stretching color contrast to its own smaller max.</summary>
    private const double AbsoluteCeilingFloorMeters = 150.0;
    private static readonly (double T, Color Color)[] LandStops =
    [
        (0.00, Color.FromRgb(0xE8, 0xDC, 0xAE)), // beach
        (0.08, Color.FromRgb(0x7F, 0xAE, 0x5E)), // lowland green
        (0.30, Color.FromRgb(0xA8, 0xB2, 0x5A)), // upland green / olive
        (0.55, Color.FromRgb(0xB9, 0x8A, 0x55)), // brown hills
        (0.78, Color.FromRgb(0x8F, 0x83, 0x78)), // gray-brown mountains
        (0.92, Color.FromRgb(0xC9, 0xC5, 0xC0)), // bare rock
        (1.00, Color.FromRgb(0xF5, 0xF5, 0xF5)), // snow
    ];

    private static readonly Color DeepWater = Color.FromRgb(0x0A, 0x1A, 0x4F);
    private static readonly Color ShoreWater = Color.FromRgb(0x4F, 0x9B, 0xC9);

    /// <summary>Freshwater tint for river cells — deliberately distinct from the ocean blues
    /// above, so a mountain river doesn't read as a bay.</summary>
    private static readonly Color RiverColor = Color.FromRgb(0x3F, 0xA9, 0x9B);

    /// <summary>Also used by the caller to color the sea-level (0m) contour as a coastline.</summary>
    public static readonly Color CoastlineColor = Color.FromRgb(0x2E, 0x6F, 0x9E);

    /// <summary>Elevation-tinted color for a cell, with river cells overriding to <see cref="RiverColor"/>
    /// regardless of their absolute height (a river carved through a mountain valley is still a river).</summary>
    public static Color ForCell(float heightMeters, float gridMin, float gridMax, bool isRiver)
        => isRiver ? RiverColor : ForHeight(heightMeters, gridMin, gridMax);

    public static Color ForHeight(float heightMeters, float gridMin, float gridMax)
    {
        if (heightMeters < 0)
        {
            var floor = Math.Min(gridMin, -10.0);
            var t = Math.Clamp((heightMeters - floor) / (0 - floor), 0.0, 1.0);
            return Lerp(DeepWater, ShoreWater, t);
        }
        else
        {
            var ceiling = Math.Max(gridMax, AbsoluteCeilingFloorMeters);
            var t = Math.Clamp(heightMeters / ceiling, 0.0, 1.0);
            return Sample(LandStops, t);
        }
    }

    private static Color Sample((double T, Color Color)[] stops, double t)
    {
        for (var i = 1; i < stops.Length; i++)
        {
            if (t <= stops[i].T)
            {
                var (t0, c0) = stops[i - 1];
                var (t1, c1) = stops[i];
                var localT = t1 > t0 ? (t - t0) / (t1 - t0) : 0.0;
                return Lerp(c0, c1, localT);
            }
        }
        return stops[^1].Color;
    }

    private static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }
}
