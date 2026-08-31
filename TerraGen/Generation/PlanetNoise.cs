namespace TerraGen.Generation;

/// <summary>
/// Deterministic elevation noise for a planet — an independent implementation (no shared code
/// with TerrainEditor's own generator) built specifically for tile-based batch generation. Two
/// layers, same physically-motivated split TerrainEditor's tool arrived at through iteration:
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>
/// <b>Landmass/coastline</b> (<see cref="SampleLandmass"/>) — multi-octave fBm evaluated on the
/// true 3D unit sphere (never a 2D lat/lon projection), so it is globally seamless: no seam at
/// the antimeridian, no singularity at the poles, valid everywhere on the planet regardless of
/// which run generated which tile.
/// </item>
/// <item>
/// <b>Mountain uplift</b> (<see cref="SampleMountainRidge"/>) — a ridged multifractal evaluated
/// on a FLAT local tangent plane, in meters offset from a single SHARED reference point passed in
/// by the caller (see <see cref="TileRun"/>) — NOT each tile's own center. That single shared
/// origin, reused for every tile in one run, is what makes the mountain layer agree at tile
/// boundaries: two tiles sampling the same physical point through the same flat-plane formula
/// necessarily get the same answer. This only holds WITHIN one run's bounded region — a flat
/// tangent plane isn't valid at planet-wide scale (a proper global fix would need a low-distortion
/// projection like cube-sphere; explicitly out of scope here, see the project's plan).
/// </item>
/// </list>
/// </remarks>
public static class PlanetNoise
{
    public const double EarthSurfaceGravityMs2 = 9.80665;
    public const double EarthRadiusMeters = 6_378_100.0;

    public sealed record Parameters(
        int Seed,
        double AmplitudeMeters = 200.0,
        double LandmassAmplitudeFraction = 0.65,
        double? ContinentWavelengthMeters = null,
        int ContinentOctaves = 5,
        double ContinentPersistence = 0.5,
        double ContinentLacunarity = 2.2,
        double MountainWavelengthMeters = 2000.0,
        double? MountainBeltDirectionDeg = null,
        double MountainBeltStretch = 3.0,
        int MountainOctaves = 4,
        double MountainPersistence = 0.55,
        double MountainLacunarity = 2.0,
        double GravityMs2 = EarthSurfaceGravityMs2)
    {
        /// <summary>Resolves <see cref="MountainBeltDirectionDeg"/>, deriving a deterministic
        /// direction from <see cref="Seed"/> when unset — same value for every tile in a run since
        /// it only depends on the seed, not position.</summary>
        public double ResolvedMountainBeltDirectionDeg => MountainBeltDirectionDeg ?? DeriveBeltDirectionDeg(Seed);
    }

    /// <summary>
    /// Fraction of the mountain layer's own max amplitude used as the "suppression band" below sea
    /// level (see <see cref="MountainSuppression"/>) — calibrated empirically (not derived
    /// analytically) against this exact noise construction: the ridged fractal's TYPICAL value
    /// sits well above 0 (averaging several octaves concentrates values toward the middle of the
    /// range), so a band tied to a large fraction of the max amplitude leaves the MEDIAN
    /// suppressed contribution still able to swamp anything shallower than tens of meters. A
    /// narrow band instead means most of the seafloor sits fully suppressed, with uplift only
    /// easing back in within a thin coastal fringe.
    /// </summary>
    private const double MountainSuppressionBandFraction = 0.08;

    /// <summary>
    /// Constant added to the continent-shape fBm sum (in its own [-1, 1] units, before scaling to
    /// meters) so the global land/sea split trends toward Earth's actual ~29% land / 71% ocean
    /// instead of the ~50/50 split raw symmetric noise lands on by default. Calibrated empirically
    /// against this construction (pooled samples across many seeds, 71st percentile of the raw
    /// noise) — a fixed constant can't hit exactly 71% ocean on any ONE seed, but shifts the
    /// average across seeds to the right place instead of leaving it a coin flip.
    /// </summary>
    private const double ContinentSeaBias = -0.12;

    /// <summary>
    /// Samples ONLY the landmass/coastline layer at true spherical (latitude, longitude)
    /// coordinates — globally seamless because it's evaluated on the point's actual position on
    /// the unit sphere (3D noise), not a 2D projection. Bounded to
    /// <c>±AmplitudeMeters × LandmassAmplitudeFraction / 2</c>.
    /// </summary>
    public static double SampleLandmass(double latDeg, double lonDeg, Parameters p, double planetRadiusMeters)
    {
        var latRad = latDeg * Math.PI / 180.0;
        var lonRad = lonDeg * Math.PI / 180.0;
        var cosLat = Math.Cos(latRad);
        var x = cosLat * Math.Cos(lonRad);
        var y = cosLat * Math.Sin(lonRad);
        var z = Math.Sin(latRad);

        var continentWavelengthMeters = p.ContinentWavelengthMeters
            ?? 2.0 * Math.PI * Math.Max(planetRadiusMeters, 1.0) / 6.0; // ~a sixth of the circumference
        var baseFrequency = Math.Max(planetRadiusMeters, 1.0) / continentWavelengthMeters;

        // Domain warping — distort the unit-sphere position with a slower, independent noise
        // field before evaluating fBm, so coastlines bend/branch instead of reading as smooth
        // "amoeba" blobs. Warping (x,y,z) itself (not lat/lon) keeps this seamless: (x,y,z) is
        // already a continuous function of (lat,lon) with no antimeridian/pole discontinuity, and
        // a smooth function of a continuous input stays continuous.
        var warpFrequency = baseFrequency / 4.0;
        const double warpStrength = 0.4;
        var warpX = ValueNoise3D(x * warpFrequency, y * warpFrequency, z * warpFrequency, p.Seed + 555002) * warpStrength;
        var warpY = ValueNoise3D(x * warpFrequency, y * warpFrequency, z * warpFrequency, p.Seed + 777002) * warpStrength;
        var warpZ = ValueNoise3D(x * warpFrequency, y * warpFrequency, z * warpFrequency, p.Seed + 999002) * warpStrength;
        var wx = x + warpX;
        var wy = y + warpY;
        var wz = z + warpZ;

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
        var landmassNoise = maxAmplitude > 0 ? sum / maxAmplitude : 0.0; // in [-1, 1]

        landmassNoise = Math.Clamp(landmassNoise + ContinentSeaBias, -1.0, 1.0);

        var landmassShare = Math.Clamp(p.LandmassAmplitudeFraction, 0.0, 1.0);
        return landmassNoise * (p.AmplitudeMeters * landmassShare / 2.0);
    }

    /// <summary>
    /// Samples the mountain/ridge layer at a point given as METERS OFFSET from a shared, caller-
    /// chosen reference point (NOT from any per-tile center) — see the type-level remarks for why
    /// that's the key to tile-boundary agreement. Result is in [0, 1]; the caller scales it by the
    /// gravity-adjusted amplitude budget and by <see cref="MountainSuppression"/>.
    /// </summary>
    public static double SampleMountainRidge(double offsetXMeters, double offsetYMeters, Parameters p)
    {
        var angleRad = p.ResolvedMountainBeltDirectionDeg * Math.PI / 180.0;
        var cosA = Math.Cos(angleRad);
        var sinA = Math.Sin(angleRad);
        var alongBelt = offsetXMeters * cosA + offsetYMeters * sinA;
        var acrossBelt = -offsetXMeters * sinA + offsetYMeters * cosA;
        var stretch = Math.Max(p.MountainBeltStretch, 1e-6);
        var stretchedX = alongBelt / stretch;
        var stretchedY = acrossBelt;

        // Domain warping, same reasoning as SampleLandmass's — evaluated in the already-
        // stretched/rotated space so the warp keeps the same belt-direction anisotropy instead of
        // undoing it.
        var warpFrequency = 1.0 / (p.MountainWavelengthMeters * 4.0);
        var warpStrength = p.MountainWavelengthMeters * 1.2;
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
            var n = ValueNoise2D(stretchedX * frequency, stretchedY * frequency, p.Seed + o * 1013);
            var ridged = 1.0 - Math.Abs(n);
            sum += ridged * amplitude;
            maxAmplitude += amplitude;
            amplitude *= p.MountainPersistence;
            frequency *= p.MountainLacunarity;
        }
        return maxAmplitude > 0 ? sum / maxAmplitude : 0.0;
    }

    /// <summary>
    /// How much of the mountain layer's uplift actually reaches the surface at a point whose
    /// landmass baseline is already at <paramref name="landmassElevation"/> — <c>1.0</c> once at
    /// or above sea level, tapering to <c>0.0</c> a modest fraction of the mountain layer's own
    /// max amplitude below sea level. Without this, deep ocean could still sprout dry-land peaks
    /// wherever the ridged noise happened to spike, regardless of depth.
    /// </summary>
    public static double MountainSuppression(double landmassElevation, double mountainMaxAmplitude)
    {
        var band = mountainMaxAmplitude * MountainSuppressionBandFraction;
        return band > 0 ? Math.Clamp(1.0 + landmassElevation / band, 0.0, 1.0) : 0.0;
    }

    /// <summary>
    /// Converts a point given as meters offset from a shared reference (lat,lon) into true
    /// (lat,lon) — a plain equirectangular (Plate Carrée) projection anchored at the reference
    /// point, using ITS latitude as the fixed longitude-scaling factor everywhere (not each
    /// point's own latitude, the way a genuinely local tangent-plane approximation would). That's
    /// deliberate: <c>TileGenerator</c> always calls this with the SAME reference point across
    /// every run (see its remarks), so every tile — from any run, at any time — computes the
    /// mountain layer with the exact same formula and is seamless against ANY existing neighbor,
    /// not just ones from its own run. The cost is the projection's own well-known distortion far
    /// from the reference latitude (shapes stretch east-west approaching the poles) — a real,
    /// visible-if-you-go-far-enough tradeoff, but a smooth and CONSISTENT one: it never breaks
    /// tile-to-tile agreement, it only ever changes how stretched a mountain range looks.
    /// </summary>
    public static (double latDeg, double lonDeg) OffsetToLatLon(
        double offsetXMeters, double offsetYMeters, double refLatDeg, double refLonDeg, double planetRadiusMeters)
    {
        var refLatRad = refLatDeg * Math.PI / 180.0;
        var lat = refLatDeg + offsetYMeters / planetRadiusMeters * (180.0 / Math.PI);
        var lon = refLonDeg + offsetXMeters / (planetRadiusMeters * Math.Cos(refLatRad)) * (180.0 / Math.PI);
        return (lat, lon);
    }

    /// <summary>Inverse of <see cref="OffsetToLatLon"/> — converts true (lat,lon) into meters
    /// offset from the reference point, using the same fixed-at-the-reference-latitude
    /// equirectangular formula.</summary>
    public static (double offsetXMeters, double offsetYMeters) LatLonToOffset(
        double latDeg, double lonDeg, double refLatDeg, double refLonDeg, double planetRadiusMeters)
    {
        var refLatRad = refLatDeg * Math.PI / 180.0;
        var offsetY = (latDeg - refLatDeg) * (Math.PI / 180.0) * planetRadiusMeters;
        var offsetX = (lonDeg - refLonDeg) * (Math.PI / 180.0) * planetRadiusMeters * Math.Cos(refLatRad);
        return (offsetX, offsetY);
    }

    /// <summary>
    /// Samples the combined landmass + mountain elevation (meters) at a point given as meters
    /// offset from a shared reference (lat,lon) — the single entry point <c>TileGenerator</c>
    /// calls per grid cell. Landmass comes from the point's true (lat,lon) (globally seamless);
    /// mountain uplift comes from the flat offset itself — seamless against ANY other tile
    /// computed from the SAME reference point, which is why <c>TileGenerator</c> always uses one
    /// fixed planet-wide reference rather than a different one per run (see its remarks).
    /// </summary>
    public static double SampleCombined(double offsetXMeters, double offsetYMeters,
        double refLatDeg, double refLonDeg, Parameters p, double planetRadiusMeters)
    {
        var (lat, lon) = OffsetToLatLon(offsetXMeters, offsetYMeters, refLatDeg, refLonDeg, planetRadiusMeters);
        var landmassElevation = SampleLandmass(lat, lon, p, planetRadiusMeters);

        var mountainNoise = SampleMountainRidge(offsetXMeters, offsetYMeters, p);
        var landmassShare = Math.Clamp(p.LandmassAmplitudeFraction, 0.0, 1.0);
        var mountainShare = 1.0 - landmassShare;
        var gravityScale = Math.Clamp(EarthSurfaceGravityMs2 / Math.Max(p.GravityMs2, 1e-6), 0.1, 10.0);
        var mountainMaxAmplitude = p.AmplitudeMeters * mountainShare * gravityScale;
        var suppression = MountainSuppression(landmassElevation, mountainMaxAmplitude);
        var mountainElevation = mountainNoise * mountainMaxAmplitude * suppression;

        return landmassElevation + mountainElevation;
    }

    private static double DeriveBeltDirectionDeg(int seed)
    {
        var unit = LatticeValue(seed, -seed, 8675309); // in [-1, 1]
        return (unit + 1.0) / 2.0 * 180.0; // -> [0, 180)
    }

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

    private static double Smoothstep(double t) => t * t * (3.0 - 2.0 * t);

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
