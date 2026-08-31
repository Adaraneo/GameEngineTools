using GameEngineTools.World.Data;

namespace TerrainEditor.Services;

/// <summary>
/// Deterministic geomorphology-inspired terrain generator (no external dependency) — produces a
/// starting heightmap the designer then hand-tunes with the brush tools, instead of starting from
/// a perfectly flat grid.
/// </summary>
/// <remarks>
/// <para>
/// Plain isotropic fractal noise (the previous version of this generator) doesn't look like real
/// terrain: real mountain ranges are LINEAR — they form along tectonic collision belts — not
/// randomly scattered blobs, and real terrain is weathered by water, not left as raw noise. This
/// generator models three separate, geomorphologically distinct layers instead of one noise field:
/// </para>
/// <list type="number">
/// <item>
/// <b>Landmass shape</b> (<see cref="Sample"/>'s landmass term) — a large-scale, low-frequency,
/// SIGNED layer at a FIXED real-world wavelength (<see cref="Parameters.LandmassWavelengthMeters"/>,
/// deliberately NOT tied to the current grid's size — see that property's remarks) that decides
/// where land ends and sea begins. Bounded so local mountain/detail noise can't flip land into sea
/// except right at the actual coastline (see <see cref="Parameters.LandmassAmplitudeFraction"/>).
/// </item>
/// <item>
/// <b>Mountain uplift</b> (<see cref="Sample"/>'s ridged term) — a multi-octave RIDGED fractal
/// (<c>1 - |noise|</c>, which produces sharp creases instead of smooth blobby hills), stretched
/// along one dominant direction per seed so ranges read as elongated belts rather than isotropic
/// hills — the same anisotropy real collision-belt mountains have. Always non-negative: uplift
/// only ever ADDS height on top of the landmass shape, it never carves.
/// </item>
/// <item>
/// <b>Hydraulic erosion</b> (<see cref="HydraulicErosion"/>, a separate post-process run by the
/// caller after <see cref="Generate"/>) — carves valleys and redistributes sediment into the
/// combined result, giving it a weathered look instead of raw noise.
/// </item>
/// </list>
/// </remarks>
public static class TerrainGenerator
{
    /// <summary>Earth's standard surface gravity (m/s²) — the reference point
    /// <see cref="Parameters.GravityMs2"/> is scaled against.</summary>
    public const double EarthSurfaceGravityMs2 = 9.80665;

    /// <summary>Earth's equatorial radius (meters) — fallback when no planet config is available.</summary>
    public const double EarthRadiusMeters = 6_378_100.0;

    public sealed record Parameters(
        int Seed = 1,
        /// <summary>Full peak-to-trough elevation budget (meters) this generator draws from —
        /// see <see cref="LandmassAmplitudeFraction"/> for how it splits between the landmass and
        /// mountain layers. Actual min/max are asymmetric (mountains only add height, landmass
        /// swings both ways) — see <see cref="Sample"/>'s remarks for the exact bound.</summary>
        double AmplitudeMeters = 200.0,
        /// <summary>
        /// Wavelength (meters) of the landmass-shape layer — a FIXED real-world scale, not
        /// relative to the current grid's size. Deliberately not derived from the map's extent:
        /// real coastline features don't get physically bigger just because you expanded the
        /// canvas you're painting on. Expanding the map and regenerating should reveal more
        /// coastline/terrain at the same scale, not stretch the existing shape to fill the new
        /// area.
        /// </summary>
        double LandmassWavelengthMeters = 900.0,
        /// <summary>
        /// Share (0..1) of <see cref="AmplitudeMeters"/> given to the landmass-shape layer's ±
        /// swing; the rest becomes the mountain layer's uplift budget. Higher values make the
        /// landmass shape dominate more strongly over mountain uplift near the coastline, so
        /// ridged noise can't push land into sea (or back) far from the landmass layer's own
        /// zero-crossing.
        /// </summary>
        double LandmassAmplitudeFraction = 0.65,
        /// <summary>
        /// Wavelength (meters) of the broadest mountain ridge — a FIXED real-world scale, same
        /// reasoning as <see cref="LandmassWavelengthMeters"/>: it must NOT scale with the grid's
        /// current size, or every regenerate after Expand Map would stretch existing ridges wider
        /// instead of adding more of them across the newly revealed area. 2000m (2km) reflects an
        /// actual mountain-RANGE scale — the old 150m default was calibrated back when local
        /// editing windows were a few hundred meters across; at the km-scale windows
        /// <c>GenerateSphere</c> now produces, 150m ridges just tile as a "farm" of near-identical
        /// small hills with no larger structure. <see cref="MountainOctaves"/> still layers finer
        /// detail on top via <see cref="MountainLacunarity"/>, same as before.
        /// </summary>
        double MountainWavelengthMeters = 2000.0,
        /// <summary>
        /// Direction (degrees, mod 180 — a ridge line and its 180°-opposite are the same line) the
        /// mountain belt runs along. <c>null</c> (the default) derives a direction deterministically
        /// from <see cref="Seed"/>, so the same seed always produces the same belt orientation.
        /// </summary>
        double? MountainBeltDirectionDeg = null,
        /// <summary>How many times longer ridges run along the belt direction than across it.
        /// <c>1.0</c> = isotropic (no directional bias); realistic collision-belt ranges are
        /// usually well past <c>2</c>.</summary>
        double MountainBeltStretch = 3.0,
        int MountainOctaves = 4,
        /// <summary>Amplitude multiplier applied per successive mountain octave.</summary>
        double MountainPersistence = 0.55,
        /// <summary>Frequency multiplier applied per successive mountain octave.</summary>
        double MountainLacunarity = 2.0,
        /// <summary>
        /// Surface gravity (m/s²) — scales the mountain layer's uplift budget, NOT the landmass
        /// layer. Real mountain height is limited by isostasy: rock can only support so much
        /// weight before it deforms, so lower gravity allows taller peaks (Mars's Olympus Mons,
        /// at roughly a third of Earth's gravity, is over twice Everest's height) and higher
        /// gravity compresses them. Coastline/landmass shape isn't a strength-of-materials
        /// question, so it's left alone. Default is Earth's standard gravity
        /// (<see cref="EarthSurfaceGravityMs2"/>) — 1.0× scale, no effect. The resulting scale
        /// factor is clamped to [0.1, 10] so a pathological planet config (near-zero mass, say)
        /// can't produce absurd or non-finite terrain.
        /// </summary>
        double GravityMs2 = EarthSurfaceGravityMs2,
        /// <summary>
        /// Wavelength (meters) of the CONTINENT-scale landmass shape — used only by the spherical
        /// generator (<see cref="SampleSphereLandmass"/>/<see cref="GenerateSphere"/>), never by
        /// <see cref="Sample"/>/<see cref="Generate"/>. This is deliberately a SEPARATE knob from
        /// <see cref="LandmassWavelengthMeters"/>: that one is calibrated for local flat editing
        /// (hundreds of meters, matching a hand-authored map's own extent) and would alias into
        /// pure noise at planetary scale — a few hundred meters against a ~40,000km circumference
        /// is well over 100,000 wavelengths per degree of longitude, which is exactly why the
        /// planet overview looked like static instead of continents. <c>null</c> (the default)
        /// derives it from the planet's own radius (roughly a sixth of its circumference — a
        /// handful of continent-sized landmasses) — safe to auto-derive here, unlike a local
        /// grid's resizable extent, because a planet's radius is a genuinely fixed physical
        /// quantity that doesn't change as you pan around it.
        /// </summary>
        double? ContinentWavelengthMeters = null,
        /// <summary>Octaves of the continent-shape fBm — single-octave noise produces smooth,
        /// featureless "amoeba" blobs with no bays, peninsulas, or secondary islands; real
        /// coastlines are fractal at multiple scales simultaneously. More octaves add finer
        /// coastline detail at progressively shorter wavelengths (each one
        /// <see cref="ContinentLacunarity"/>× shorter, weighted <see cref="ContinentPersistence"/>×
        /// smaller — same fBm construction <see cref="SampleRidgedFractal"/> already uses for
        /// mountains, just without the ridge-sharpening step).</summary>
        int ContinentOctaves = 5,
        double ContinentPersistence = 0.5,
        double ContinentLacunarity = 2.2);

    /// <summary>Overwrites every cell in <paramref name="grid"/> with generated terrain (landmass
    /// shape + mountain uplift only — call <see cref="HydraulicErosion.Erode"/> afterward for the
    /// weathered/eroded look).</summary>
    public static void Generate(TerrainHeightmap grid, Parameters parameters)
    {
        if (parameters.MountainBeltDirectionDeg is null)
            parameters = parameters with { MountainBeltDirectionDeg = DeriveBeltDirectionDeg(parameters.Seed) };

        for (var gy = 0; gy < grid.Height; gy++)
        {
            for (var gx = 0; gx < grid.Width; gx++)
            {
                var worldX = grid.OriginX + gx * grid.CellSizeMeters;
                var worldY = grid.OriginY + gy * grid.CellSizeMeters;
                grid.Values[gy * grid.Width + gx] = (float)Sample(worldX, worldY, parameters);
            }
        }
    }

    /// <summary>Deterministically derives a belt direction in [0, 180) from the seed — a simple
    /// hash, not cryptographic, just well-mixed enough that neighboring seeds don't share a direction.</summary>
    private static double DeriveBeltDirectionDeg(int seed)
    {
        var unit = LatticeValue(seed, -seed, 8675309); // in [-1, 1]
        return (unit + 1.0) / 2.0 * 180.0; // -> [0, 180)
    }

    /// <summary>
    /// Overwrites every cell in <paramref name="grid"/> with terrain centered on a point on a
    /// planet — the landmass/coastline layer is sampled from <see cref="SampleSphereLandmass"/>
    /// (globally seamless, no poles/antimeridian artifacts). The mountain layer
    /// stays the existing flat, directional ridged fractal (see <see cref="Sample"/>) — a single
    /// window is a few km across at most, utterly negligible next to a planet's radius, so
    /// treating it as flat here introduces no meaningful distortion. What this does NOT do:
    /// guarantee two separately-generated nearby windows tile seamlessly on their mountain layer
    /// or their (window-resolution-dependent) hydraulic erosion — see the type-level remarks.
    /// </summary>
    public static void GenerateSphere(TerrainHeightmap grid, double centerLatDeg, double centerLonDeg,
        Parameters p, double planetRadiusMeters)
    {
        if (p.MountainBeltDirectionDeg is null)
            p = p with { MountainBeltDirectionDeg = DeriveBeltDirectionDeg(p.Seed) };

        var centerLatRad = centerLatDeg * Math.PI / 180.0;
        var centerWorldX = grid.OriginX + grid.Width / 2.0 * grid.CellSizeMeters;
        var centerWorldY = grid.OriginY + grid.Height / 2.0 * grid.CellSizeMeters;
        var landmassShare = Math.Clamp(p.LandmassAmplitudeFraction, 0.0, 1.0);
        var mountainShare = 1.0 - landmassShare;
        var gravityScale = Math.Clamp(EarthSurfaceGravityMs2 / Math.Max(p.GravityMs2, 1e-6), 0.1, 10.0);

        for (var gy = 0; gy < grid.Height; gy++)
        {
            for (var gx = 0; gx < grid.Width; gx++)
            {
                var worldX = grid.OriginX + gx * grid.CellSizeMeters;
                var worldY = grid.OriginY + gy * grid.CellSizeMeters;
                var offsetX = worldX - centerWorldX;
                var offsetY = worldY - centerWorldY;

                // Equirectangular local approximation — a window a few km across is negligible
                // against a planetary radius, so treating meters-offset as a flat tangent plane
                // introduces no visible distortion (this breaks down badly at global scale, which
                // is exactly why the overview map samples SampleSphereLandmass directly per
                // (lat,lon) cell instead of going through this projection).
                var lat = centerLatDeg + offsetY / planetRadiusMeters * (180.0 / Math.PI);
                var lon = centerLonDeg + offsetX / (planetRadiusMeters * Math.Cos(centerLatRad)) * (180.0 / Math.PI);

                var landmassElevation = SampleSphereLandmass(lat, lon, p, planetRadiusMeters);
                var mountainNoise = SampleRidgedFractal(offsetX, offsetY, p); // in [0, 1]
                var mountainMaxAmplitude = p.AmplitudeMeters * mountainShare * gravityScale;
                var suppression = MountainSuppression(landmassElevation, mountainMaxAmplitude);
                var mountainElevation = mountainNoise * mountainMaxAmplitude * suppression;

                grid.Values[gy * grid.Width + gx] = (float)(landmassElevation + mountainElevation);
            }
        }
    }

    /// <summary>
    /// Samples ONLY the landmass/coastline layer, at true spherical (latitude, longitude)
    /// coordinates on a planet of the given radius — globally seamless (no seam at the
    /// antimeridian, no singularity at the poles) because it's 3D noise evaluated at the point's
    /// actual position on the unit sphere, not a 2D projection. Used by
    /// <see cref="GenerateSphere"/>. Bounded exactly like
    /// <see cref="Sample"/>'s landmass term: <c>±AmplitudeMeters × LandmassAmplitudeFraction / 2</c>.
    /// </summary>
    public static double SampleSphereLandmass(double latDeg, double lonDeg, Parameters p, double planetRadiusMeters)
    {
        var latRad = latDeg * Math.PI / 180.0;
        var lonRad = lonDeg * Math.PI / 180.0;
        var cosLat = Math.Cos(latRad);
        var x = cosLat * Math.Cos(lonRad);
        var y = cosLat * Math.Sin(lonRad);
        var z = Math.Sin(latRad);

        // Scales the unit-sphere position so that one noise-lattice unit corresponds to exactly
        // ContinentWavelengthMeters of arc length — NOT LandmassWavelengthMeters, which is a
        // local-editing-scale constant that would alias into static at planetary scale (see
        // ContinentWavelengthMeters' remarks).
        var continentWavelengthMeters = p.ContinentWavelengthMeters
            ?? 2.0 * Math.PI * Math.Max(planetRadiusMeters, 1.0) / 6.0; // ~a sixth of the circumference
        var baseFrequency = Math.Max(planetRadiusMeters, 1.0) / continentWavelengthMeters;

        // Domain warping (same technique and same reasoning as SampleRidgedFractal's — see its
        // remarks) — without it, coastlines read as smooth "amoeba/cauliflower" blobs even with
        // several fBm octaves, because every octave is still centered on the same unwarped lattice.
        // Warping the unit-sphere position itself (not lat/lon) keeps this seamless: (x, y, z) is
        // already a continuous function of (lat, lon) with no antimeridian/pole discontinuity, and
        // any smooth function of a continuous input is itself continuous, so the warp offset can't
        // reintroduce a seam. Evaluated at a quarter of the base frequency (slower than the
        // continent shape itself) so it bends coastlines without overwhelming their overall shape.
        var warpFrequency = baseFrequency / 4.0;
        const double warpStrength = 0.4; // fraction of a base lattice unit
        var warpX = ValueNoise3D(x * warpFrequency, y * warpFrequency, z * warpFrequency, p.Seed + 555002) * warpStrength;
        var warpY = ValueNoise3D(x * warpFrequency, y * warpFrequency, z * warpFrequency, p.Seed + 777002) * warpStrength;
        var warpZ = ValueNoise3D(x * warpFrequency, y * warpFrequency, z * warpFrequency, p.Seed + 999002) * warpStrength;
        var wx = x + warpX;
        var wy = y + warpY;
        var wz = z + warpZ;

        // Multi-octave fBm, same construction as SampleRidgedFractal minus the ridge-sharpening
        // step — a single octave gives smooth, featureless "amoeba" continents with no coastline
        // detail; summing progressively finer, weaker octaves adds bays/peninsulas/small islands
        // at multiple scales, the way real coastlines actually look.
        var amplitude = 1.0;
        var frequency = baseFrequency;
        var sum = 0.0;
        var maxAmplitude = 0.0;
        for (var o = 0; o < p.ContinentOctaves; o++)
        {
            var n = ValueNoise3D(wx * frequency, wy * frequency, wz * frequency, p.Seed - 97531 + o * 1013);
            sum += n * amplitude;
            maxAmplitude += amplitude;
            amplitude *= p.ContinentPersistence;
            frequency *= p.ContinentLacunarity;
        }
        var landmassNoise = maxAmplitude > 0 ? sum / maxAmplitude : 0.0; // still in [-1, 1]

        // Raw symmetric noise splits land/sea roughly 50/50 on average — real planets don't (Earth
        // is ~29% land / 71% ocean). Shifting the whole distribution down before scaling biases
        // the split toward that without touching its shape (still full fBm coastline detail, just
        // recentered) — see ContinentSeaBias's remarks for how the constant was derived. Clamped
        // to [-1, 1] so this can't push the result past the bound documented below.
        landmassNoise = Math.Clamp(landmassNoise + ContinentSeaBias, -1.0, 1.0);

        var landmassShare = Math.Clamp(p.LandmassAmplitudeFraction, 0.0, 1.0);
        return landmassNoise * (p.AmplitudeMeters * landmassShare / 2.0);
    }

    /// <summary>
    /// Constant added to the continent-shape fBm sum (in its own [-1, 1] units, before scaling to
    /// meters) so the global land/sea split trends toward Earth's actual ~29% land / 71% ocean
    /// instead of the ~50/50 split raw symmetric noise naturally lands on — without it, whether a
    /// given seed happens to look "oceanic" or "one giant landmass" is pure coin-flip luck (this is
    /// exactly what produced the all-green "Earth" that prompted this bias in the first place).
    /// </summary>
    /// <remarks>
    /// Calibrated empirically, not derived analytically: pooled <see cref="SampleSphereLandmass"/>
    /// samples across 25 seeds (at the default <see cref="Parameters.ContinentOctaves"/>/
    /// <see cref="Parameters.ContinentPersistence"/>/<see cref="Parameters.ContinentLacunarity"/>)
    /// and took the 71st percentile of the raw noise — the bias that would put 71% of samples below
    /// sea level came out to ≈0.119, negated here. A single fixed constant can't hit exactly 71%
    /// ocean on any ONE seed (individual seeds still vary — that's real coastline variety, not a
    /// bug), but it shifts the AVERAGE across seeds to the right place instead of leaving it
    /// centered on a coin flip. Changing the continent octave/persistence/lacunarity defaults
    /// meaningfully would shift the noise's own distribution and could call for recalibrating this.
    /// </remarks>
    private const double ContinentSeaBias = -0.12;

    /// <summary>Samples the combined landmass + mountain field at an arbitrary world-space point —
    /// used by <see cref="Generate"/>, exposed separately so it stays independently testable.</summary>
    /// <remarks>
    /// The landmass term is signed and bounded to
    /// <c>±AmplitudeMeters × LandmassAmplitudeFraction / 2</c> (gravity does not affect it); the
    /// mountain term is UNSIGNED (uplift only) and bounded to
    /// <c>[0, AmplitudeMeters × (1 − LandmassAmplitudeFraction) × gravityScale]</c> where
    /// <c>gravityScale = clamp(EarthSurfaceGravityMs2 / GravityMs2, 0.1, 10)</c>. So the combined
    /// result's exact bound is
    /// <c>[-AmplitudeMeters×frac/2, AmplitudeMeters×frac/2 + AmplitudeMeters×(1−frac)×gravityScale]</c>
    /// — e.g. with the default 0.65 split, AmplitudeMeters=200, and Earth gravity (scale 1.0):
    /// roughly -65m to +135m (that upper bound is still technically correct but rarely reached in
    /// practice — see <see cref="MountainSuppression"/>: mountain uplift is dampened the deeper
    /// underwater the landmass baseline already is, so open ocean stays open ocean instead of
    /// sprouting random peaks/islets wherever the ridged noise happens to spike). This asymmetry
    /// (more headroom for mountains than ocean depth) mirrors real elevation distributions, where
    /// peaks reach far higher above sea level than typical seafloor variation reaches below it.
    /// </remarks>
    public static double Sample(double worldX, double worldY, Parameters p)
    {
        var landmassFrequency = 1.0 / p.LandmassWavelengthMeters;
        // Distinct seed offset so the landmass layer isn't correlated with the mountain layer.
        var landmassNoise = ValueNoise2D(worldX * landmassFrequency, worldY * landmassFrequency, p.Seed - 97531);

        var mountainNoise = SampleRidgedFractal(worldX, worldY, p); // in [0, 1]

        var landmassShare = Math.Clamp(p.LandmassAmplitudeFraction, 0.0, 1.0);
        var mountainShare = 1.0 - landmassShare;
        var gravityScale = Math.Clamp(EarthSurfaceGravityMs2 / Math.Max(p.GravityMs2, 1e-6), 0.1, 10.0);

        var landmassElevation = landmassNoise * (p.AmplitudeMeters * landmassShare / 2.0);
        var mountainMaxAmplitude = p.AmplitudeMeters * mountainShare * gravityScale;
        var suppression = MountainSuppression(landmassElevation, mountainMaxAmplitude);
        var mountainElevation = mountainNoise * mountainMaxAmplitude * suppression;
        return landmassElevation + mountainElevation;
    }

    /// <summary>
    /// Fraction of the mountain layer's own max amplitude used as the "suppression band" below sea
    /// level — see <see cref="MountainSuppression"/>. Deliberately small: empirically, the ridged
    /// fractal's TYPICAL value (median, measured across many samples) sits around 0.6-0.65 of its
    /// own max, not near 0 — averaging several octaves concentrates values toward the middle of the
    /// range instead of spreading them out, so "typical" uplift is already most of the max uplift.
    /// A band tied to a large fraction of that max (0.2 was tried first) still let the MEDIAN
    /// suppressed contribution swamp anything shallower than ~25-30m — an unambiguously-underwater
    /// point at -8m still came out 100% dry land after erosion. Tapering over a narrow band instead
    /// means most of the seafloor sits fully suppressed (flat, landmass-only), with uplift only
    /// easing back in within a thin coastal fringe close to the actual shoreline.
    /// </summary>
    private const double MountainSuppressionBandFraction = 0.08;

    /// <summary>
    /// How much of the mountain layer's uplift actually reaches the surface at a point whose
    /// landmass baseline is already at <paramref name="landmassElevation"/> — <c>1.0</c> (full
    /// effect) once the baseline is at or above sea level, tapering linearly to <c>0.0</c> once the
    /// baseline is <see cref="MountainSuppressionBandFraction"/> of <paramref name="mountainMaxAmplitude"/>
    /// or more below sea level.
    /// </summary>
    /// <remarks>
    /// Without this, mountain uplift applies at full strength EVERYWHERE regardless of how deep
    /// the seafloor already is there — a point 13m underwater (clearly "sea" on any coastline map)
    /// could still get +25m of uplift and pop up as dry land, because the ridged fractal's uplift
    /// doesn't know or care where the coastline is. Real seamounts only breach the surface where
    /// the seafloor is shallow enough for their own height to close the gap; this mirrors that by
    /// scaling uplift down the deeper the point already is, fully gone past a modest fraction of the
    /// mountain layer's own maximum possible height — an underwater point can only ever be lifted to
    /// the surface by a peak that's actually tall enough to reach it, and only near the coast.
    /// </remarks>
    private static double MountainSuppression(double landmassElevation, double mountainMaxAmplitude)
    {
        var band = mountainMaxAmplitude * MountainSuppressionBandFraction;
        return band > 0 ? Math.Clamp(1.0 + landmassElevation / band, 0.0, 1.0) : 0.0;
    }

    /// <summary>
    /// Multi-octave RIDGED fractal (<c>1 - |value noise|</c> per octave, sharpening creases instead
    /// of the smooth blobby look of plain fBm), with sampling coordinates rotated into the belt
    /// direction, compressed along it (elongated ridges instead of isotropic hills), and DOMAIN
    /// WARPED before evaluation (see the warp block below — bent, branching ridges instead of a
    /// too-regular tiled "waffle iron" of near-identical hills). Result is a weighted average of
    /// per-octave values already in [0, 1], so it stays in [0, 1].
    /// </summary>
    private static double SampleRidgedFractal(double worldX, double worldY, Parameters p)
    {
        var angleRad = (p.MountainBeltDirectionDeg ?? 0.0) * Math.PI / 180.0;
        var cosA = Math.Cos(angleRad);
        var sinA = Math.Sin(angleRad);
        var alongBelt = worldX * cosA + worldY * sinA;
        var acrossBelt = -worldX * sinA + worldY * cosA;
        var stretch = Math.Max(p.MountainBeltStretch, 1e-6);
        var stretchedX = alongBelt / stretch; // compressed frequency along the belt -> elongated ridges
        var stretchedY = acrossBelt;

        // Domain warping — the technique virtually every serious noise-based terrain tool uses
        // (libnoise, World Machine, Gaea, ...): distort the sample coordinates with a slower,
        // independent noise field before evaluating the ridged fractal, instead of evaluating it
        // directly. Raw multi-octave noise on its own reads as too regular — every octave centered
        // on the same lattice, just finer — which is exactly the "farm of near-identical hills"
        // look. Warping in ALREADY-stretched/rotated space (not raw worldX/Y) means the warp field
        // keeps the same belt-direction anisotropy as the ridges themselves, instead of undoing it.
        var warpFrequency = 1.0 / (p.MountainWavelengthMeters * 4.0); // slower than the ridges
        var warpStrength = p.MountainWavelengthMeters * 1.2; // meters, same (stretched) local space
        var warpX = ValueNoise2D(stretchedX * warpFrequency, stretchedY * warpFrequency, p.Seed + 555001) * warpStrength;
        var warpY = ValueNoise2D(stretchedX * warpFrequency, stretchedY * warpFrequency, p.Seed + 777001) * warpStrength;
        stretchedX += warpX;
        stretchedY += warpY;

        var frequency = 1.0 / p.MountainWavelengthMeters;
        var amplitude = 1.0;
        var sum = 0.0;
        var maxAmplitude = 0.0;

        for (var o = 0; o < p.MountainOctaves; o++)
        {
            var n = ValueNoise2D(stretchedX * frequency, stretchedY * frequency, p.Seed + o * 1013); // [-1, 1]
            var ridged = 1.0 - Math.Abs(n); // [0, 1], sharp crease where n crosses 0
            sum += ridged * amplitude;
            maxAmplitude += amplitude;
            amplitude *= p.MountainPersistence;
            frequency *= p.MountainLacunarity;
        }

        return maxAmplitude > 0 ? sum / maxAmplitude : 0.0;
    }

    /// <summary>Smooth 2D value noise in [-1, 1]: deterministic pseudo-random values at integer
    /// lattice points, smoothstep-interpolated between them.</summary>
    private static double ValueNoise2D(double x, double y, int seed)
    {
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var x1 = x0 + 1;
        var y1 = y0 + 1;

        var tx = Smoothstep(x - x0);
        var ty = Smoothstep(y - y0);

        var v00 = LatticeValue(x0, y0, seed);
        var v10 = LatticeValue(x1, y0, seed);
        var v01 = LatticeValue(x0, y1, seed);
        var v11 = LatticeValue(x1, y1, seed);

        var top = v00 + (v10 - v00) * tx;
        var bottom = v01 + (v11 - v01) * tx;
        return top + (bottom - top) * ty;
    }

    private static double Smoothstep(double t) => t * t * (3.0 - 2.0 * t);

    /// <summary>Deterministic pseudo-random value in [-1, 1] for an integer lattice point —
    /// a simple integer hash (Wang/Squirrel-style mix), not cryptographic, just well-mixed.</summary>
    private static double LatticeValue(int x, int y, int seed)
    {
        unchecked
        {
            var h = x * 374761393 + y * 668265263 + seed * 1442695041;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            var frac = (h & 0xFFFFFF) / (double)0xFFFFFF;
            return frac * 2.0 - 1.0;
        }
    }

    /// <summary>Smooth 3D value noise in [-1, 1] — same construction as <see cref="ValueNoise2D"/>,
    /// trilinearly interpolated instead of bilinearly. Used for sphere sampling
    /// (<see cref="SampleSphereLandmass"/>), where a flat 2D field would produce visible
    /// poles/antimeridian seams.</summary>
    private static double ValueNoise3D(double x, double y, double z, int seed)
    {
        var x0 = (int)Math.Floor(x); var x1 = x0 + 1;
        var y0 = (int)Math.Floor(y); var y1 = y0 + 1;
        var z0 = (int)Math.Floor(z); var z1 = z0 + 1;

        var tx = Smoothstep(x - x0);
        var ty = Smoothstep(y - y0);
        var tz = Smoothstep(z - z0);

        var v000 = LatticeValue3D(x0, y0, z0, seed);
        var v100 = LatticeValue3D(x1, y0, z0, seed);
        var v010 = LatticeValue3D(x0, y1, z0, seed);
        var v110 = LatticeValue3D(x1, y1, z0, seed);
        var v001 = LatticeValue3D(x0, y0, z1, seed);
        var v101 = LatticeValue3D(x1, y0, z1, seed);
        var v011 = LatticeValue3D(x0, y1, z1, seed);
        var v111 = LatticeValue3D(x1, y1, z1, seed);

        var top0 = v000 + (v100 - v000) * tx;
        var top1 = v010 + (v110 - v010) * tx;
        var bottom0 = v001 + (v101 - v001) * tx;
        var bottom1 = v011 + (v111 - v011) * tx;

        var top = top0 + (top1 - top0) * ty;
        var bottom = bottom0 + (bottom1 - bottom0) * ty;

        return top + (bottom - top) * tz;
    }

    /// <summary>3D counterpart of <see cref="LatticeValue"/> — same hash-and-mix style, with a
    /// third coordinate folded in via a distinct large odd multiplier.</summary>
    private static double LatticeValue3D(int x, int y, int z, int seed)
    {
        unchecked
        {
            var h = x * 374761393 + y * 668265263 + z * 2126618783 + seed * 1442695041;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            var frac = (h & 0xFFFFFF) / (double)0xFFFFFF;
            return frac * 2.0 - 1.0;
        }
    }
}
