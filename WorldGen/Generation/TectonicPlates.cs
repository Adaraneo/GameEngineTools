namespace WorldGen.Generation;

/// <summary>
/// Independent port of TerraGen's own <c>TectonicPlates</c> (no project reference; see
/// WorldGen.csproj) — same plate scatter/classification, reused here so WorldGen can weight
/// settlement placement/danger by proximity to a plate boundary, using the SAME seed/count the
/// terrain it's reading was generated with (see <see cref="WorldGen.PlanetSettings"/>).
/// </summary>
public static class TectonicPlates
{
    public readonly record struct Plate(
        int Id, double X, double Y, double Z, double VelX, double VelY, double VelZ, bool IsContinental);

    public enum BoundaryType { None, Convergent, Divergent, Transform }

    /// <param name="BoundaryInfluence">0 deep inside a plate's own territory, growing toward 1 the
    /// closer a point sits to the Voronoi border with its nearest neighboring plate.</param>
    public readonly record struct BoundarySample(
        int PlateId, bool IsContinental, BoundaryType Boundary, double BoundaryInfluence);

    /// <summary>Builds <paramref name="count"/> plates for <paramref name="seed"/> — pure
    /// function of those two inputs; callers should build this once per run and reuse it.</summary>
    public static Plate[] Generate(int seed, int count)
    {
        count = Math.Max(1, count);
        var plates = new Plate[count];
        const double goldenAngle = Math.PI * (3.0 - 2.2360679774997896 /* sqrt(5) */);

        for (var i = 0; i < count; i++)
        {
            var t = (i + 0.5) / count;
            var phi = Math.Acos(Math.Clamp(1.0 - 2.0 * t, -1.0, 1.0));
            var theta = i * goldenAngle;

            phi = Math.Clamp(phi + LatticeValue(i, 111, seed) * 0.12, 0.001, Math.PI - 0.001);
            theta += LatticeValue(i, 222, seed) * 0.25;

            var x = Math.Sin(phi) * Math.Cos(theta);
            var y = Math.Sin(phi) * Math.Sin(theta);
            var z = Math.Cos(phi);

            var (tx, ty, tz, bx, by, bz) = TangentBasis(x, y, z);
            var velAngle = (LatticeValue(i, 333, seed) + 1.0) * Math.PI;
            var speed = 0.3 + (LatticeValue(i, 444, seed) + 1.0) / 2.0 * 0.7;
            var velX = (tx * Math.Cos(velAngle) + bx * Math.Sin(velAngle)) * speed;
            var velY = (ty * Math.Cos(velAngle) + by * Math.Sin(velAngle)) * speed;
            var velZ = (tz * Math.Cos(velAngle) + bz * Math.Sin(velAngle)) * speed;

            var isContinental = (LatticeValue(i, 555, seed) + 1.0) / 2.0 < 0.35;

            plates[i] = new Plate(i, x, y, z, velX, velY, velZ, isContinental);
        }

        return plates;
    }

    /// <summary>Classifies the point at unit-sphere position (x,y,z) against its two nearest
    /// plates. <paramref name="plates"/> must come from <see cref="Generate"/> with the SAME
    /// seed/count TerraGen used to generate the terrain being sampled.</summary>
    public static BoundarySample Sample(Plate[] plates, double x, double y, double z)
    {
        var nearestIdx = 0;
        var nearestDist = double.MaxValue;
        var secondIdx = 0;
        var secondDist = double.MaxValue;

        for (var i = 0; i < plates.Length; i++)
        {
            var p = plates[i];
            var dx = x - p.X;
            var dy = y - p.Y;
            var dz = z - p.Z;
            var d = dx * dx + dy * dy + dz * dz;
            if (d < nearestDist)
            {
                secondDist = nearestDist; secondIdx = nearestIdx;
                nearestDist = d; nearestIdx = i;
            }
            else if (d < secondDist)
            {
                secondDist = d; secondIdx = i;
            }
        }

        var nearest = plates[nearestIdx];
        if (plates.Length < 2)
            return new BoundarySample(nearest.Id, nearest.IsContinental, BoundaryType.None, 0.0);

        var second = plates[secondIdx];
        var distNearest = Math.Sqrt(nearestDist);
        var distSecond = Math.Sqrt(secondDist);
        var denom = distNearest + distSecond;
        var influence = denom > 1e-9 ? Math.Clamp(1.0 - (distSecond - distNearest) / denom * 2.0, 0.0, 1.0) : 0.0;

        var connX = second.X - nearest.X;
        var connY = second.Y - nearest.Y;
        var connZ = second.Z - nearest.Z;
        var connLen = Math.Sqrt(connX * connX + connY * connY + connZ * connZ);

        var boundary = BoundaryType.None;
        if (connLen > 1e-9)
        {
            connX /= connLen; connY /= connLen; connZ /= connLen;
            var relVelX = second.VelX - nearest.VelX;
            var relVelY = second.VelY - nearest.VelY;
            var relVelZ = second.VelZ - nearest.VelZ;
            var approach = -(relVelX * connX + relVelY * connY + relVelZ * connZ);
            const double transformThreshold = 0.15;
            boundary = approach > transformThreshold ? BoundaryType.Convergent
                : approach < -transformThreshold ? BoundaryType.Divergent
                : BoundaryType.Transform;
        }

        return new BoundarySample(nearest.Id, nearest.IsContinental, boundary, influence);
    }

    private static (double tx, double ty, double tz, double bx, double by, double bz) TangentBasis(double x, double y, double z)
    {
        double upX = 0, upY = 0, upZ = 1;
        if (Math.Abs(z) > 0.9) { upX = 1; upY = 0; upZ = 0; }

        var tx = upY * z - upZ * y;
        var ty = upZ * x - upX * z;
        var tz = upX * y - upY * x;
        var tLen = Math.Sqrt(tx * tx + ty * ty + tz * tz);
        tx /= tLen; ty /= tLen; tz /= tLen;

        var bx = y * tz - z * ty;
        var by = z * tx - x * tz;
        var bz = x * ty - y * tx;

        return (tx, ty, tz, bx, by, bz);
    }

    private static double LatticeValue(int index, int salt, int seed)
    {
        unchecked
        {
            var h = index * 374761393 + salt * 668265263 + seed * 1442695041;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            var frac = (h & 0xFFFFFF) / (double)0xFFFFFF;
            return frac * 2.0 - 1.0;
        }
    }
}
