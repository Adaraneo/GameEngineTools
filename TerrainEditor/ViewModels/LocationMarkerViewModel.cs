using GameEngineTools.World.Location;
using TerrainEditor.Models;

namespace TerrainEditor.ViewModels;

/// <summary>Draggable on-canvas representation of one <see cref="LocationInfo"/>.</summary>
public sealed class LocationMarkerViewModel : ViewModelBase
{
    public LocationMarkerViewModel(LocationInfo info)
    {
        Id = info.Id;
        _displayName = info.DisplayName;
        _region = info.Region;
        _x = info.X;
        _y = info.Y;
        _altitudeMeters = info.AltitudeMeters;
        _type = info.Type;
        _terrain = info.Terrain;
        _baseNoise = info.BaseNoise;
        _noisePerPerson = info.NoisePerPerson;
        _capacity = info.Capacity;
        _allowsPrivacy = info.AllowsPrivacy;
    }

    public string Id { get; }

    private string _displayName;
    /// <summary>Settable — the location-edit dialog (double-click a marker) can rename it.</summary>
    public string DisplayName { get => _displayName; set => SetProperty(ref _displayName, value); }

    private string _region;
    /// <summary>Loaded from the database; overwritable by <c>RegionClassifier</c> and persisted on save.</summary>
    public string Region { get => _region; set => SetProperty(ref _region, value); }

    private double _x;
    /// <summary>World-space X (meters). Updated live while dragging.</summary>
    public double X { get => _x; set => SetProperty(ref _x, value); }

    private double _y;
    /// <summary>World-space Y (meters). Updated live while dragging.</summary>
    public double Y { get => _y; set => SetProperty(ref _y, value); }

    private double _altitudeMeters;
    /// <summary>Elevation sampled from the heightmap at (X, Y); refreshed on save.</summary>
    public double AltitudeMeters { get => _altitudeMeters; set => SetProperty(ref _altitudeMeters, value); }

    private LocationType _type;
    public LocationType Type { get => _type; set => SetProperty(ref _type, value); }

    private TerrainType _terrain;
    public TerrainType Terrain { get => _terrain; set => SetProperty(ref _terrain, value); }

    private double _baseNoise;
    public double BaseNoise { get => _baseNoise; set => SetProperty(ref _baseNoise, value); }

    private double _noisePerPerson;
    public double NoisePerPerson { get => _noisePerPerson; set => SetProperty(ref _noisePerPerson, value); }

    private int _capacity;
    public int Capacity { get => _capacity; set => SetProperty(ref _capacity, value); }

    private bool _allowsPrivacy;
    public bool AllowsPrivacy { get => _allowsPrivacy; set => SetProperty(ref _allowsPrivacy, value); }

    /// <summary>Label for the location list panel — a plain computed snapshot (the list is
    /// manually rebuilt on change, same convention as the map overlay, so this doesn't need its
    /// own change notification).</summary>
    public string DisplayText => string.IsNullOrEmpty(Region) ? DisplayName : $"{DisplayName} ({Region})";

    /// <summary>Snapshots this marker's current fields back into a <see cref="LocationInfo"/> —
    /// used when saving/inserting/updating and when opening the edit dialog pre-filled.</summary>
    public LocationInfo ToInfo() => new(Id, DisplayName, Region, X, Y, AltitudeMeters,
        Type, Terrain, BaseNoise, NoisePerPerson, Capacity, AllowsPrivacy);
}
