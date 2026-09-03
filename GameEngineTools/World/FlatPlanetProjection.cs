// FlatPlanetProjection.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World
{
    using System;

    /// <summary>
    /// The flat equirectangular offset ↔ lat/lon conversion TerraGen's tile batches are generated
    /// against (see <c>TerraGen.Generation.TileGenerator</c>'s remarks) — lives here, in the core
    /// library both TerraGen and TerrainEditor already reference, so both sides read the exact same
    /// formula instead of each keeping its own copy that could quietly drift apart. TerrainEditor
    /// uses this to recover a loaded/stitched tile's true (lat,lon) from its stored meters-space
    /// <c>OriginX/OriginY</c> — see <see cref="Data.TerrainGeoReference"/> for the reference point
    /// persisted alongside a batch's tiles.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT the same formula as <c>WorldGen.Generation.PlanetGeometry</c>'s spherical
    /// "direct geodesic problem" — that one is exact at any distance but isn't what tile edges were
    /// actually generated against, so using it here would silently report the wrong lat/lon for
    /// anything more than a couple hundred kilometers from the reference point. This flat projection
    /// is the one that must match, however distorted it gets far from the reference latitude.
    /// </remarks>
    public static class FlatPlanetProjection
    {
        /// <summary>Converts a meters offset from (<paramref name="refLatDeg"/>, <paramref name="refLonDeg"/>)
        /// back into true (lat,lon) — inverse of <see cref="LatLonToOffset"/>.</summary>
        public static (double LatDeg, double LonDeg) OffsetToLatLon(
            double offsetXMeters, double offsetYMeters, double refLatDeg, double refLonDeg, double planetRadiusMeters)
        {
            var refLatRad = refLatDeg * Math.PI / 180.0;
            var lat = refLatDeg + offsetYMeters / planetRadiusMeters * (180.0 / Math.PI);
            var lon = refLonDeg + offsetXMeters / (planetRadiusMeters * Math.Cos(refLatRad)) * (180.0 / Math.PI);
            return (lat, lon);
        }

        /// <summary>Converts true (lat,lon) into meters offset from (<paramref name="refLatDeg"/>,
        /// <paramref name="refLonDeg"/>) — inverse of <see cref="OffsetToLatLon"/>.</summary>
        public static (double OffsetXMeters, double OffsetYMeters) LatLonToOffset(
            double latDeg, double lonDeg, double refLatDeg, double refLonDeg, double planetRadiusMeters)
        {
            var refLatRad = refLatDeg * Math.PI / 180.0;
            var offsetY = (latDeg - refLatDeg) * (Math.PI / 180.0) * planetRadiusMeters;
            var offsetX = (lonDeg - refLonDeg) * (Math.PI / 180.0) * planetRadiusMeters * Math.Cos(refLatRad);
            return (offsetX, offsetY);
        }
    }
}
