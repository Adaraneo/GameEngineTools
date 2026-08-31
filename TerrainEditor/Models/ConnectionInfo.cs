namespace TerrainEditor.Models;

/// <summary>Flat view of a <c>Connections</c> row (adjacency graph edge between two locations).</summary>
public sealed record ConnectionInfo(string FromId, string ToId, double DistanceMeters);
