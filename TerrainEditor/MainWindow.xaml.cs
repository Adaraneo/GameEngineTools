using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using GameEngineTools.World.Data;
using TerrainEditor.Models;
using TerrainEditor.Rendering;
using TerrainEditor.Services;
using TerrainEditor.ViewModels;

namespace TerrainEditor;

/// <summary>
/// Owns the interactive heightmap paint surface and location-marker overlay. A WriteableBitmap
/// + overlay Canvas aren't practically MVVM-bindable, so this state lives here rather than in
/// <see cref="ShellViewModel"/>, which it talks to only via events/commands.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ShellViewModel _vm;
    private readonly ContourGenerator _contourGen;

    private TerrainHeightmap? _grid;
    private WriteableBitmap? _bitmap;
    private readonly List<LocationMarkerViewModel> _markers = [];
    private readonly Dictionary<string, Ellipse> _markerShapes = [];
    private readonly Dictionary<string, TextBlock> _markerLabels = [];

    private List<ConnectionInfo> _connections = [];
    /// <summary>Generated terrain-aware road paths, keyed by an order-independent "a|b" pair id
    /// so both directions of a bidirectional Connection share one path.</summary>
    private readonly Dictionary<string, RoadGenerator.RoadPath> _roadPaths = [];

    private LocationMarkerViewModel? _draggingMarker;
    /// <summary>Location ids already persisted in the open database — used by <see cref="SaveToDatabase"/>
    /// to tell an existing location (UPDATE) apart from one added in this session (INSERT).</summary>
    private readonly HashSet<string> _existingLocationIds = [];
    /// <summary>Directed "from|to" connection keys already persisted in the open database — same
    /// insert-vs-update role as <see cref="_existingLocationIds"/>, but per direction since
    /// Connections rows are directed.</summary>
    private readonly HashSet<string> _existingConnectionKeys = [];
    /// <summary>First location clicked while "Connect Locations" is armed — <c>null</c> when no
    /// connection is half-made. Reset whenever <see cref="ShellViewModel.IsConnectingLocations"/>
    /// goes back to false, from any cause (completed, or the user un-toggled mid-selection).</summary>
    private LocationMarkerViewModel? _connectFromMarker;

    public MainWindow(ShellViewModel vm, ContourGenerator contourGen)
    {
        InitializeComponent();
        _vm = vm;
        _contourGen = contourGen;
        DataContext = _vm;

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.IsConnectingLocations) && !_vm.IsConnectingLocations)
                _connectFromMarker = null;
        };

        _vm.DatabaseOpened += (_, _) => LoadFromDatabase();
        _vm.SaveRequested += (_, _) => SaveToDatabase();
        // ExportSeedSql() reads straight from the database file, not from the in-memory
        // _markers/_grid/_connections the editor is actually showing — so unsaved edits (a moved
        // location, a freshly generated terrain, ...) would silently be left out of the export.
        // Saving first keeps the exported script honestly in sync with what's on screen.
        _vm.ExportRequested += (_, path) =>
        {
            SaveToDatabase();
            File.WriteAllText(path, _vm.WorldDb.ExportSeedSql());
        };
        _vm.GenerateRoadsRequested += (_, _) => GenerateRoads();
        _vm.GenerateLakesRequested += (_, _) => GenerateLakes();
        _vm.AssignRegionsRequested += (_, _) => AssignRegions();
        _vm.GoToLatLonRequested += (_, _) => GoToLatLon(_vm.TargetLatitude, _vm.TargetLongitude);
        _vm.OpenTileBrowserRequested += (_, _) => OpenTileBrowser();
    }

    /// <summary>Opens a non-modal list of every heightmap saved in the open terrain.db (e.g. tiles
    /// from a TerraGen batch run) — picking one loads it straight into the editing surface,
    /// replacing whatever <see cref="_grid"/> currently holds. Unlike <see cref="GoToLatLon"/>,
    /// this doesn't know (and doesn't claim) a real-world lat/long for the loaded tile — TerraGen
    /// tiles live in their own run-local flat frame, not one TerrainEditor has any way to map back
    /// to a planet position — so the current-center display is cleared instead of shown wrong.</summary>
    private void OpenTileBrowser()
    {
        var tiles = _vm.WorldDb.ListHeightmaps();
        var window = new TileBrowserWindow(_vm.WorldDb.LoadHeightmap, tiles) { Owner = this };
        window.TileChosen += id => LoadTile(id);
        window.Show();
    }

    /// <summary>Loads a specific saved heightmap (by id) straight into the editing surface — used
    /// by <see cref="OpenTileBrowser"/>. Saving afterward (<c>Save</c>) writes back to this same
    /// id, not the single "default" window, so loading a tile for a look and hitting Save doesn't
    /// silently clobber the main window's own terrain.</summary>
    private void LoadTile(string id)
    {
        var tile = _vm.WorldDb.LoadHeightmap(id);
        if (tile is null)
        {
            _vm.StatusText = $"Dlaždice „{id}“ se nepodařilo načíst (mezitím zmizela?).";
            return;
        }

        _grid = tile;
        _roadPaths.Clear();
        _vm.CurrentCenterLatitude = null;
        _vm.CurrentCenterLongitude = null;
        _vm.StatusText = $"Dlaždice „{id}“ načtena ({tile.Width}×{tile.Height}, buňka {tile.CellSizeMeters:0.0} m).";
        RenderGrid();
        RenderOverlay();
        Dispatcher.BeginInvoke(new Action(FitZoomToWindow), DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Builds a fresh window centered on a point on the planet (see
    /// <see cref="TerrainGenerator.GenerateSphere"/>) and generates it in one step — sized from
    /// <see cref="ShellViewModel.TargetMapSizeKm"/>, so this covers the old two-click "Expand
    /// Map, then remember to hit Generate Terrain again" dance in a single action ("give me a
    /// map of this size, here" — this is now the only terrain-generation entry point; there's no
    /// separate flat/non-planetary "Generate Terrain" anymore). The landmass/coastline layer
    /// matches whatever the Planet Overview shows at that spot. Same erosion + location-protection
    /// pipeline as before, same destructive "overwrites the current window" semantics —
    /// existing locations are NOT deleted or moved, only re-protected from ending up underwater at
    /// whatever X/Y they already have (which may no longer mean much once you've jumped somewhere
    /// else entirely — see the plan's explicitly-scoped-out multi-region/lat-long-per-location work).
    /// </summary>
    private void GoToLatLon(double latDeg, double lonDeg)
    {
        if (_grid is null) return;

        _grid = CreateGridForLatLon(_vm.TargetMapSizeKm);
        _roadPaths.Clear();

        var seed = ComputePlanetSeed();
        var gravityMs2 = ComputeSurfaceGravityMs2();
        var planetRadiusMeters = ComputePlanetRadiusMeters();
        var parameters = new TerrainGenerator.Parameters(
            Seed: seed,
            AmplitudeMeters: _vm.TerrainAmplitude,
            GravityMs2: gravityMs2);
        TerrainGenerator.GenerateSphere(_grid, latDeg, lonDeg, parameters, planetRadiusMeters);

        _vm.CurrentCenterLatitude = latDeg;
        _vm.CurrentCenterLongitude = lonDeg;

        FinishTerrainGeneration(seed, gravityMs2, locationDescription: $" na {latDeg:0.00}°, {lonDeg:0.00}°");
    }

    /// <summary>Builds a square starting grid of the given size (km per side) — used by
    /// <see cref="GoToLatLon"/> so the window's own size is a first-class part of "navigate here",
    /// not a separate Expand Map step. Higher cell-count target than <see cref="CreateDefaultGrid"/>
    /// (400 vs. 150) so the window renders at a reasonable pixel size instead of a tiny postage
    /// stamp at 100% zoom — this is the ONLY terrain-generation entry point now, so it needs to
    /// look reasonable without the designer having to zoom in first.</summary>
    private static TerrainHeightmap CreateGridForLatLon(double sizeKm)
    {
        const int TargetCellsAcrossLongAxis = 400;
        const double MinCellSize = 1.0;

        var spanMeters = Math.Max(sizeKm, 0.05) * 1000.0;
        var cellSize = Math.Max(spanMeters / TargetCellsAcrossLongAxis, MinCellSize);
        var cells = Math.Max((int)Math.Ceiling(spanMeters / cellSize) + 1, 20);
        var origin = -(cells / 2.0) * cellSize;

        return new TerrainHeightmap(
            Id: WorldDatabaseService.DefaultHeightmapId,
            OriginX: origin,
            OriginY: origin,
            CellSizeMeters: cellSize,
            Width: cells,
            Height: cells,
            Values: new float[cells * cells]);
    }

    /// <summary>Shared tail of both terrain-generation entry points: hydraulic erosion (gravity-scaled),
    /// clearing stale rivers/roads, protecting known locations from ending up underwater, and the
    /// status-bar summary.</summary>
    private void FinishTerrainGeneration(int seed, double gravityMs2, string locationDescription)
    {
        if (_grid is null) return;

        // Droplet count scales with the grid's own cell count so erosion coverage stays
        // proportional regardless of map size; capped so a huge grid can't turn generation into
        // a multi-minute wait. Erosion's own Gravity constant scales the same direction as the
        // planet's actual gravity (stronger gravity -> more erosive power), the OPPOSITE sense
        // from the mountain-height scale (stronger gravity -> shorter peaks) — both are real
        // effects, just pulling terrain shape in different directions.
        var cellCount = _grid.Width * _grid.Height;
        var dropletCount = Math.Min(150_000, (int)(_vm.ErosionStrength / 100.0 * cellCount * 2));
        const double erosionGravityAtEarthG = 4.0; // HydraulicErosion's own default/baseline
        var erosionGravity = erosionGravityAtEarthG * (gravityMs2 / TerrainGenerator.EarthSurfaceGravityMs2);
        HydraulicErosion.Erode(_grid, new HydraulicErosion.Parameters(
            Seed: seed, DropletCount: dropletCount, Gravity: erosionGravity));

        _grid = _grid with { RiverMask = null };
        _roadPaths.Clear();

        // Protection radius tied to the map's own scale (mirrors the mountain layer's
        // auto-derived wavelength) — run AFTER erosion so it also covers anything erosion itself
        // carved back underwater near a location.
        var protectionRadiusCells = Math.Max(6.0, Math.Min(_grid.Width, _grid.Height) / 6.0);
        LocationProtector.KeepLocationsDry(_grid, _markers.Select(m => (m.X, m.Y)), radiusCells: protectionRadiusCells);

        var gravityNote = _vm.WorldDb.PlanetConfig is { } planet
            ? $", gravitace {planet.PlanetName} {gravityMs2:0.00} m/s²"
            : "";
        _vm.StatusText = $"Terén vygenerován{locationDescription} (seed planety {seed}, eroze {_vm.ErosionStrength:0}%{gravityNote}), lokace ponechány nad hladinou.";
        RenderGrid();
        RenderOverlay();

        // RenderGrid() just changed HeightmapImage's Width/Height, which invalidates layout —
        // WPF doesn't recompute MapScrollViewer.ViewportWidth/Height synchronously, so measuring
        // them right here would read STALE numbers from before this resize (the actual bug behind
        // both "doesn't fill the window" and "the scale doesn't match": zoom got computed against
        // the wrong viewport size). Posting at Loaded priority runs this only after WPF has
        // finished that pending layout pass — the standard technique for "act on final layout".
        Dispatcher.BeginInvoke(new Action(FitZoomToWindow), DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Sets <see cref="ShellViewModel.Zoom"/> so the freshly generated grid's bitmap (1 device
    /// pixel per cell at zoom 1, see <see cref="RenderGrid"/>) fills the scroll viewport instead of
    /// sitting in a corner at whatever zoom was left over from browsing a previous, differently
    /// sized map. Only called right after a fresh generate — manual zooming afterward is left alone.
    /// </summary>
    private void FitZoomToWindow()
    {
        if (_grid is null) return;

        MapScrollViewer.UpdateLayout();
        var viewportWidth = MapScrollViewer.ViewportWidth > 0 ? MapScrollViewer.ViewportWidth : MapScrollViewer.ActualWidth;
        var viewportHeight = MapScrollViewer.ViewportHeight > 0 ? MapScrollViewer.ViewportHeight : MapScrollViewer.ActualHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0 || _grid.Width <= 0 || _grid.Height <= 0) return;

        // Math.Max (not Min) deliberately: the grid is square but the viewport usually isn't, so
        // "fit the smaller axis" (Min) leaves empty letterboxing on the wider axis — visually
        // "doesn't fill the window" even though nothing is cut off. Max instead fills the ENTIRE
        // viewport on both axes, letting the ScrollViewer handle the axis that now overflows
        // (which it already supports for manual pan/zoom) — the same "cover" fit CSS/photo apps use.
        _vm.Zoom = Math.Max(viewportWidth / _grid.Width, viewportHeight / _grid.Height);
    }

    /// <summary>
    /// Derives the terrain-generation seed from the open world's planet identity (<c>World:Universe</c>
    /// in <c>appsettings.World.json</c>) instead of a manually-typed number — the same planet
    /// (same name/mass/radius) should always generate the same terrain without the designer having
    /// to remember or copy a seed value around. A stable FNV-1a hash of the planet's identifying
    /// fields (NOT <c>string.GetHashCode()</c>, which .NET salts per-process and would make the
    /// "same planet" generate different terrain on every run). Falls back to a fixed constant when
    /// no planet config is available, matching the old manual default.</summary>
    private int ComputePlanetSeed()
    {
        var planet = _vm.WorldDb.PlanetConfig;
        if (planet is null) return 1;

        var key = $"{planet.PlanetName}|{planet.PlanetMassKg:R}|{planet.PlanetEquatorialRadiusKm:R}";
        var hash = 2166136261u; // FNV-1a offset basis
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(key))
        {
            hash ^= b;
            hash *= 16777619u; // FNV-1a prime
        }
        return unchecked((int)hash);
    }

    /// <summary>Derives surface gravity (m/s²) from the open world's planet settings
    /// (<c>World:Universe</c> in <c>appsettings.World.json</c>) — <c>g = G·M / r²</c>. Falls back
    /// to Earth's standard gravity when no planet config is available (no settings file, or one
    /// without a <c>World:Universe</c> section).</summary>
    private double ComputeSurfaceGravityMs2()
    {
        var planet = _vm.WorldDb.PlanetConfig;
        if (planet is null) return TerrainGenerator.EarthSurfaceGravityMs2;

        var radiusM = planet.PlanetEquatorialRadiusKm * 1000.0;
        if (radiusM <= 0) return TerrainGenerator.EarthSurfaceGravityMs2;

        return GameEngineTools.Universe.PhysicalConstants.G * planet.PlanetMassKg / (radiusM * radiusM);
    }

    /// <summary>Planet radius (meters) for spherical sampling — same settings source as
    /// <see cref="ComputeSurfaceGravityMs2"/>, falls back to Earth's radius.</summary>
    private double ComputePlanetRadiusMeters()
    {
        var planet = _vm.WorldDb.PlanetConfig;
        if (planet is null) return TerrainGenerator.EarthRadiusMeters;

        var radiusM = planet.PlanetEquatorialRadiusKm * 1000.0;
        return radiusM > 0 ? radiusM : TerrainGenerator.EarthRadiusMeters;
    }

    private void GenerateLakes()
    {
        if (_grid is null) return;
        if (_grid.RiverMask is null)
            _grid = _grid with { RiverMask = new byte[_grid.Width * _grid.Height] };

        // Locations are protected — a lake can form right next to one, it just won't swallow it.
        var count = LakeGenerator.Generate(_grid, new LakeGenerator.Parameters(),
            protectedLocations: _markers.Select(m => (m.X, m.Y)).ToList());

        _vm.StatusText = $"Vygenerováno {count} jezer.";
        RenderGrid();
        RenderOverlay();
    }

    private void AssignRegions()
    {
        if (_grid is null) return;
        if (_markers.Count == 0)
        {
            _vm.StatusText = "Žádné lokace k přiřazení regionu.";
            return;
        }

        var parameters = new RegionClassifier.Parameters();
        foreach (var marker in _markers)
            marker.Region = RegionClassifier.Classify(_grid, marker.X, marker.Y, parameters);

        _vm.StatusText = $"Region navržen pro {_markers.Count} lokací (uloží se přes Save).";
        RenderOverlay(); // marker tooltips show the new Region
        RefreshLocationList();
    }

    /// <summary>Rebuilds the "Lokace" side-panel list from <see cref="_markers"/> — a plain
    /// snapshot re-set, same manual-refresh convention as <see cref="RenderOverlay"/>, called
    /// wherever a marker is added or its name/region changes.</summary>
    private void RefreshLocationList()
    {
        LocationListBox.ItemsSource = _markers.OrderBy(m => m.DisplayName).ToList();
    }

    /// <summary>Double-click a row in the location list: selects that marker (same
    /// <see cref="ShellViewModel.SelectedMarker"/> the Region editor and canvas selection use) and
    /// scrolls/centers the map on it — a lighter-weight "find and jump to" than double-clicking
    /// the marker itself on the canvas, which opens the full edit dialog.</summary>
    private void LocationListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LocationListBox.SelectedItem is not LocationMarkerViewModel marker) return;

        _vm.SelectedMarker = marker;

        var (cx, cy) = WorldToCanvas(marker.X, marker.Y);
        var targetH = cx * _vm.Zoom - MapScrollViewer.ViewportWidth / 2.0;
        var targetV = cy * _vm.Zoom - MapScrollViewer.ViewportHeight / 2.0;
        MapScrollViewer.ScrollToHorizontalOffset(targetH);
        MapScrollViewer.ScrollToVerticalOffset(targetV);
    }

    private static string PairKey(string a, string b)
        => string.CompareOrdinal(a, b) <= 0 ? $"{a}|{b}" : $"{b}|{a}";

    #region Loading / saving

    private void LoadFromDatabase()
    {
        // Terrain-only mode (OpenTerrainOnly) has no world.db at all — Locations/Connections
        // aren't available, so skip them entirely and just load the heightmap.
        if (_vm.WorldDb.IsTerrainOnly)
        {
            _markers.Clear();
            _existingLocationIds.Clear();
            _vm.SelectedMarker = null;

            _connections = [];
            _existingConnectionKeys.Clear();
            _connectFromMarker = null;
            _roadPaths.Clear();

            _grid = _vm.WorldDb.LoadHeightmap() ?? CreateDefaultGrid([]);

            RenderGrid();
            RenderOverlay();
            RefreshLocationList();
            return;
        }

        var locations = _vm.WorldDb.GetLocations();
        _markers.Clear();
        _markers.AddRange(locations.Select(l => new LocationMarkerViewModel(l)));
        _existingLocationIds.Clear();
        _existingLocationIds.UnionWith(locations.Select(l => l.Id));
        _vm.SelectedMarker = null;

        _connections = _vm.WorldDb.GetConnections().ToList();
        _existingConnectionKeys.Clear();
        _existingConnectionKeys.UnionWith(_connections.Select(c => $"{c.FromId}|{c.ToId}"));
        _connectFromMarker = null;
        _roadPaths.Clear();

        _grid = _vm.WorldDb.LoadHeightmap() ?? CreateDefaultGrid(locations);

        RenderGrid();
        RenderOverlay();
        RefreshLocationList();
    }

    private void SaveToDatabase()
    {
        if (_grid is null) return;

        // Terrain-only mode has no world.db to write locations/connections into — just save the
        // heightmap itself (InsertLocation/InsertConnection would throw, see RequireWorldOpen).
        if (_vm.WorldDb.IsTerrainOnly)
        {
            _vm.WorldDb.SaveHeightmap(_grid);
            return;
        }

        foreach (var marker in _markers)
        {
            var altitude = _grid.SampleAt(marker.X, marker.Y);
            marker.AltitudeMeters = altitude;

            if (_existingLocationIds.Contains(marker.Id))
            {
                _vm.WorldDb.UpdateLocationPosition(marker.Id, marker.X, marker.Y, altitude);
                _vm.WorldDb.UpdateLocationRegion(marker.Id, marker.Region);
                _vm.WorldDb.UpdateLocationDetails(marker.ToInfo()); // picks up any edit-dialog changes
            }
            else
            {
                _vm.WorldDb.InsertLocation(marker.ToInfo());
                _existingLocationIds.Add(marker.Id);
            }
        }

        // _connections is the single current source of truth for distances — GenerateRoads()
        // updates it in place when it changes something, so a plain write-back here always
        // persists whatever's current, not just road-generated ones.
        // Same insert-vs-update split as locations above: a connection made via "Connect
        // Locations" this session has no row in the database yet.
        foreach (var conn in _connections)
        {
            var key = $"{conn.FromId}|{conn.ToId}";
            if (_existingConnectionKeys.Contains(key))
            {
                _vm.WorldDb.UpdateConnectionDistance(conn.FromId, conn.ToId, conn.DistanceMeters);
            }
            else
            {
                _vm.WorldDb.InsertConnection(conn.FromId, conn.ToId, conn.DistanceMeters);
                _existingConnectionKeys.Add(key);
            }
        }

        _vm.WorldDb.SaveHeightmap(_grid);
    }

    /// <summary>Runs terrain-aware pathfinding (see <see cref="RoadGenerator"/>) for every loaded
    /// Connection and redraws the road overlay. A bidirectional pair (A→B and B→A) shares one path.</summary>
    private void GenerateRoads()
    {
        if (_grid is null) return;
        if (_connections.Count == 0)
        {
            _vm.StatusText = "Žádná Connections data k vygenerování cest.";
            return;
        }

        var byId = _markers.ToDictionary(m => m.Id);
        _roadPaths.Clear();
        var seen = new HashSet<string>();
        var generated = 0;

        // An indexed for-loop, not foreach: the loop body below mutates _connections[i] via the
        // indexer to write back the road's real length, and List<T>'s indexer setter bumps its
        // version counter — a foreach enumerator over the same list would throw
        // "Collection was modified" the moment that happens.
        for (var outerI = 0; outerI < _connections.Count; outerI++)
        {
            var conn = _connections[outerI];
            var key = PairKey(conn.FromId, conn.ToId);
            if (!seen.Add(key)) continue;
            if (!byId.TryGetValue(conn.FromId, out var from) || !byId.TryGetValue(conn.ToId, out var to)) continue;

            var path = RoadGenerator.FindPath(_grid, from.X, from.Y, to.X, to.Y);
            if (path is null) continue;

            _roadPaths[key] = path;
            generated++;

            // The terrain-aware path's real length usually differs from the straight-line
            // distance the connection was seeded with — keep _connections (both directions of
            // this pair) in sync so Save persists the more accurate figure.
            for (var i = 0; i < _connections.Count; i++)
            {
                if (PairKey(_connections[i].FromId, _connections[i].ToId) == key)
                    _connections[i] = _connections[i] with { DistanceMeters = path.LengthMeters };
            }
        }

        _vm.StatusText = $"Vygenerováno {generated} cest podle terénu.";
        RenderOverlay();
    }

    /// <summary>
    /// Builds a flat starting grid sized to fit all loaded locations (with padding), or a
    /// reasonable default if there are none yet — a blank canvas for the designer to paint.
    /// </summary>
    private static TerrainHeightmap CreateDefaultGrid(IReadOnlyList<LocationInfo> locations)
    {
        const int TargetCellsAcrossLongAxis = 150;
        const double MinCellSize = 5.0;
        const double PaddingFraction = 0.2;

        double minX, minY, maxX, maxY;
        if (locations.Count == 0)
        {
            minX = -500; minY = -500; maxX = 500; maxY = 500;
        }
        else
        {
            minX = locations.Min(l => l.X);
            minY = locations.Min(l => l.Y);
            maxX = locations.Max(l => l.X);
            maxY = locations.Max(l => l.Y);

            var spanX0 = Math.Max(maxX - minX, 1.0);
            var spanY0 = Math.Max(maxY - minY, 1.0);
            var padX = spanX0 * PaddingFraction;
            var padY = spanY0 * PaddingFraction;
            minX -= padX; maxX += padX;
            minY -= padY; maxY += padY;
        }

        var spanX = maxX - minX;
        var spanY = maxY - minY;
        var cellSize = Math.Max(Math.Max(spanX, spanY) / TargetCellsAcrossLongAxis, MinCellSize);

        var width = Math.Max((int)Math.Ceiling(spanX / cellSize) + 1, 20);
        var height = Math.Max((int)Math.Ceiling(spanY / cellSize) + 1, 20);

        return new TerrainHeightmap(
            Id: WorldDatabaseService.DefaultHeightmapId,
            OriginX: minX,
            OriginY: minY,
            CellSizeMeters: cellSize,
            Width: width,
            Height: height,
            Values: new float[width * height]); // flat sea level — designer paints from here
    }

    #endregion Loading / saving

    #region Coordinate conversion

    // The heightmap image is rendered 1 device-independent pixel per grid cell (Stretch="None"),
    // so canvas coordinates and grid cell coordinates are the same numbers.

    private (double X, double Y) WorldToCanvas(double worldX, double worldY)
        => ((worldX - _grid!.OriginX) / _grid.CellSizeMeters, (worldY - _grid.OriginY) / _grid.CellSizeMeters);

    private (double X, double Y) CanvasToWorld(double canvasX, double canvasY)
        => (_grid!.OriginX + canvasX * _grid.CellSizeMeters, _grid.OriginY + canvasY * _grid.CellSizeMeters);

    #endregion Coordinate conversion

    #region Heightmap rendering

    private void RenderGrid()
    {
        if (_grid is null) return;

        if (_bitmap is null || _bitmap.PixelWidth != _grid.Width || _bitmap.PixelHeight != _grid.Height)
        {
            _bitmap = new WriteableBitmap(_grid.Width, _grid.Height, 96, 96, PixelFormats.Bgr32, null);
            HeightmapImage.Source = _bitmap;
            HeightmapImage.Width = _grid.Width;
            HeightmapImage.Height = _grid.Height;
            OverlayCanvas.Width = _grid.Width;
            OverlayCanvas.Height = _grid.Height;
        }

        var min = _grid.Values.Min();
        var max = _grid.Values.Max();

        // Bgr32: 4 bytes/pixel (B, G, R, unused), elevation-tinted — blue below sea level (0m),
        // beach/green/brown/gray/snow above it, river cells overridden to freshwater teal.
        // See TerrainColorRamp for the exact bands.
        var stride = _grid.Width * 4;
        var pixels = new byte[stride * _grid.Height];
        for (var i = 0; i < _grid.Values.Length; i++)
        {
            var isRiver = _grid.RiverMask is { } mask && mask[i] != 0;
            var color = TerrainColorRamp.ForCell(_grid.Values[i], min, max, isRiver);
            var offset = i * 4;
            pixels[offset] = color.B;
            pixels[offset + 1] = color.G;
            pixels[offset + 2] = color.R;
            pixels[offset + 3] = 255;
        }

        _bitmap.WritePixels(new Int32Rect(0, 0, _grid.Width, _grid.Height), pixels, stride, 0);
    }

    /// <summary>Rebuilds contour lines + location markers. Contours are recomputed here only —
    /// on stroke end, not every mouse-move — because marching squares over the whole grid is
    /// too expensive to run per pixel-drag.</summary>
    private void RenderOverlay()
    {
        if (_grid is null) return;

        OverlayCanvas.Children.Clear();
        _markerShapes.Clear();
        _markerLabels.Clear();

        foreach (var seg in _contourGen.Generate(_grid))
        {
            var isCoastline = seg.Level == 0f;
            var (x1, y1) = WorldToCanvas(seg.X1, seg.Y1);
            var (x2, y2) = WorldToCanvas(seg.X2, seg.Y2);
            var line = new Line
            {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                Stroke = isCoastline ? new SolidColorBrush(TerrainColorRamp.CoastlineColor) : Brushes.SaddleBrown,
                StrokeThickness = isCoastline ? 1.4 : 0.6,
                Opacity = isCoastline ? 0.9 : 0.7,
                IsHitTestVisible = false,
            };
            OverlayCanvas.Children.Add(line);
        }

        RenderConnections();

        foreach (var marker in _markers)
            AddMarkerVisual(marker);
    }

    /// <summary>Draws each Connection: a generated terrain-aware road (dashed, if one has been
    /// generated) or a faint straight line as a placeholder (before "Generate Roads" is run).</summary>
    private void RenderConnections()
    {
        if (_connections.Count == 0) return;

        var byId = _markers.ToDictionary(m => m.Id);
        var drawn = new HashSet<string>();

        foreach (var conn in _connections)
        {
            var key = PairKey(conn.FromId, conn.ToId);
            if (!drawn.Add(key)) continue;
            if (!byId.TryGetValue(conn.FromId, out var from) || !byId.TryGetValue(conn.ToId, out var to)) continue;

            if (_roadPaths.TryGetValue(key, out var road))
            {
                var points = new PointCollection(road.WorldPoints.Select(p =>
                {
                    var (cx, cy) = WorldToCanvas(p.X, p.Y);
                    return new Point(cx, cy);
                }));
                var polyline = new Polyline
                {
                    Points = points,
                    Stroke = Brushes.DimGray,
                    StrokeThickness = 1.2,
                    StrokeDashArray = [3, 2],
                    Opacity = 0.85,
                    IsHitTestVisible = false,
                };
                OverlayCanvas.Children.Add(polyline);
            }
            else
            {
                var (x1, y1) = WorldToCanvas(from.X, from.Y);
                var (x2, y2) = WorldToCanvas(to.X, to.Y);
                var line = new Line
                {
                    X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                    Stroke = Brushes.Gray,
                    StrokeThickness = 0.8,
                    Opacity = 0.4,
                    IsHitTestVisible = false,
                };
                OverlayCanvas.Children.Add(line);
            }
        }
    }

    private void AddMarkerVisual(LocationMarkerViewModel marker)
    {
        var (cx, cy) = WorldToCanvas(marker.X, marker.Y);

        var dot = new Ellipse
        {
            Width = 12,
            Height = 12,
            Fill = Brushes.OrangeRed,
            Stroke = Brushes.White,
            StrokeThickness = 1.5,
            Cursor = Cursors.SizeAll,
            Tag = marker,
            ToolTip = $"{marker.DisplayName} ({marker.Region})",
        };
        dot.MouseLeftButtonDown += MarkerDot_MouseLeftButtonDown;
        dot.MouseMove += MarkerDot_MouseMove;
        dot.MouseLeftButtonUp += MarkerDot_MouseLeftButtonUp;

        Canvas.SetLeft(dot, cx - dot.Width / 2);
        Canvas.SetTop(dot, cy - dot.Height / 2);
        OverlayCanvas.Children.Add(dot);
        _markerShapes[marker.Id] = dot;

        var label = new TextBlock
        {
            Text = marker.DisplayName,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
            FontSize = 10,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(label, cx + 8);
        Canvas.SetTop(label, cy - 7);
        OverlayCanvas.Children.Add(label);
        _markerLabels[marker.Id] = label;
    }

    #endregion Heightmap rendering

    #region Zoom

    /// <summary>
    /// Ctrl+wheel zooms, centered on the cursor. Plain wheel is left alone so the
    /// ScrollViewer's normal vertical-scroll behavior still works.
    /// </summary>
    private void MapScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        e.Handled = true;

        var oldZoom = _vm.Zoom;
        var factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
        var newZoom = Math.Clamp(oldZoom * factor, ShellViewModel.MinZoom, ShellViewModel.MaxZoom);
        if (Math.Abs(newZoom - oldZoom) < 1e-9) return;

        // Keep the point under the cursor fixed on screen: capture it in unscaled content
        // space before changing Zoom, then re-derive the scroll offsets that put it back
        // under the cursor at the new scale.
        var mousePos = e.GetPosition(MapScrollViewer);
        var contentX = (MapScrollViewer.HorizontalOffset + mousePos.X) / oldZoom;
        var contentY = (MapScrollViewer.VerticalOffset + mousePos.Y) / oldZoom;

        _vm.Zoom = newZoom;

        // The LayoutTransform only takes effect after the next layout pass, so the offset
        // fix-up has to wait for it — otherwise ScrollableWidth/Height are still stale.
        Dispatcher.InvokeAsync(() =>
        {
            MapScrollViewer.ScrollToHorizontalOffset(contentX * newZoom - mousePos.X);
            MapScrollViewer.ScrollToVerticalOffset(contentY * newZoom - mousePos.Y);
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    #endregion Zoom

    #region Panning

    private Point? _panStartMousePos;
    private double _panStartHOffset;
    private double _panStartVOffset;

    /// <summary>Middle-mouse-button drag pans the map — doesn't collide with left-click, which
    /// is already spoken for (select/drag a marker, place a location, wire up a connection).
    /// Preview so it captures the press before any child (marker dot, heightmap image) sees it.</summary>
    private void MapScrollViewer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;

        _panStartMousePos = e.GetPosition(MapScrollViewer);
        _panStartHOffset = MapScrollViewer.HorizontalOffset;
        _panStartVOffset = MapScrollViewer.VerticalOffset;
        MapScrollViewer.CaptureMouse();
        Mouse.OverrideCursor = Cursors.ScrollAll;
        e.Handled = true;
    }

    private void MapScrollViewer_MouseMove(object sender, MouseEventArgs e)
    {
        if (_panStartMousePos is not { } start || e.MiddleButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(MapScrollViewer);
        MapScrollViewer.ScrollToHorizontalOffset(_panStartHOffset - (pos.X - start.X));
        MapScrollViewer.ScrollToVerticalOffset(_panStartVOffset - (pos.Y - start.Y));
    }

    private void MapScrollViewer_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || _panStartMousePos is null) return;

        _panStartMousePos = null;
        MapScrollViewer.ReleaseMouseCapture();
        Mouse.OverrideCursor = null;
        e.Handled = true;
    }

    #endregion Panning

    #region Map click (add location) / cursor position

    private void HeightmapImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_grid is null) return;
        if (!_vm.IsAddingLocation) return;

        var pos = e.GetPosition(HeightmapImage);
        var (worldX, worldY) = CanvasToWorld(pos.X, pos.Y);
        AddLocationAt(worldX, worldY);
    }

    /// <summary>Prompts for a display name and places a new location marker at the given
    /// world-space point. Not persisted until Save — see <see cref="_existingLocationIds"/>.</summary>
    private void AddLocationAt(double worldX, double worldY)
    {
        _vm.IsAddingLocation = false; // single-shot: placing one disarms the mode

        var dialog = new LocationDetailsDialog { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var id = MakeUniqueLocationId(dialog.EnteredName);
        var altitude = _grid?.SampleAt(worldX, worldY) ?? 0.0;
        var region = _grid is not null
            ? RegionClassifier.Classify(_grid, worldX, worldY, new RegionClassifier.Parameters())
            : string.Empty;

        var info = new LocationInfo(id, dialog.EnteredName, region, worldX, worldY, altitude,
            dialog.SelectedType, dialog.SelectedTerrain, dialog.BaseNoise, dialog.NoisePerPerson,
            dialog.Capacity, dialog.AllowsPrivacy);
        _markers.Add(new LocationMarkerViewModel(info));
        // Deliberately NOT added to _existingLocationIds — SaveToDatabase treats its absence
        // there as "insert this one", then adds it so a later Save updates instead of re-inserting.

        _vm.StatusText = $"Lokace „{dialog.EnteredName}“ přidána (uloží se přes Save).";
        RenderOverlay();
        RefreshLocationList();
    }

    /// <summary>Double-click a marker: opens the same dialog pre-filled with its current values,
    /// then applies whatever changed straight to the marker — persisted on the next Save.</summary>
    private void EditLocationAt(LocationMarkerViewModel marker)
    {
        var dialog = new LocationDetailsDialog(marker.ToInfo()) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        marker.DisplayName = dialog.EnteredName;
        marker.Type = dialog.SelectedType;
        marker.Terrain = dialog.SelectedTerrain;
        marker.BaseNoise = dialog.BaseNoise;
        marker.NoisePerPerson = dialog.NoisePerPerson;
        marker.Capacity = dialog.Capacity;
        marker.AllowsPrivacy = dialog.AllowsPrivacy;

        _vm.StatusText = $"Lokace „{marker.DisplayName}“ upravena (uloží se přes Save).";
        RenderOverlay(); // marker label/tooltip reflect the new name/region right away
        RefreshLocationList();
    }

    /// <summary>Slugifies the display name into an id and appends a numeric suffix on collision
    /// against every id currently loaded (existing or already added this session).</summary>
    private string MakeUniqueLocationId(string displayName)
    {
        var slug = new string(displayName.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
        if (slug.Length == 0) slug = "location";

        var candidate = slug;
        var suffix = 2;
        var takenIds = _markers.Select(m => m.Id).ToHashSet();
        while (takenIds.Contains(candidate))
            candidate = $"{slug}_{suffix++}";
        return candidate;
    }

    private void HeightmapImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (_grid is null) return;

        var pos = e.GetPosition(HeightmapImage);
        var (worldX, worldY) = CanvasToWorld(pos.X, pos.Y);
        var (gridWidth, gridHeight) = (_grid.Width, _grid.Height);
        var canvasXInBounds = pos.X >= 0 && pos.X < gridWidth;
        var canvasYInBounds = pos.Y >= 0 && pos.Y < gridHeight;
        _vm.CursorWorldPosition = canvasXInBounds && canvasYInBounds
            ? $"X={worldX:0.0}  Y={worldY:0.0}  Z={_grid.SampleAt(worldX, worldY):0.0}m"
            : string.Empty;
    }

    #endregion Map click (add location) / cursor position

    #region Marker dragging

    private void MarkerDot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var dot = (Ellipse)sender;
        var marker = (LocationMarkerViewModel)dot.Tag;
        _vm.SelectedMarker = marker; // clicking a marker also selects it for the Region editor

        if (e.ClickCount == 2)
        {
            // The first click of the double-click already ran this handler once (ClickCount 1)
            // and captured the mouse to start a drag — release that before the modal dialog opens,
            // or the Ellipse would keep owning mouse capture with no matching MouseLeftButtonUp
            // ever reaching it (the dialog swallows subsequent input).
            dot.ReleaseMouseCapture();
            _draggingMarker = null;
            EditLocationAt(marker);
            e.Handled = true;
            return;
        }

        if (_vm.IsConnectingLocations)
        {
            HandleConnectClick(marker);
            e.Handled = true;
            return; // don't also start a drag while wiring up a connection
        }

        _draggingMarker = marker;
        dot.CaptureMouse();
        e.Handled = true;
    }

    /// <summary>First click of a pair picks the "from" location; the second creates a
    /// bidirectional Connection at their straight-line distance (Generate Roads refines it into
    /// a terrain-aware path later). Stays armed afterward so several connections can be made in
    /// a row without re-toggling "Connect Locations".</summary>
    private void HandleConnectClick(LocationMarkerViewModel marker)
    {
        if (_connectFromMarker is null)
        {
            _connectFromMarker = marker;
            _vm.StatusText = $"Spojení: „{marker.DisplayName}“ vybrána jako start — klikni na cílovou lokaci.";
            return;
        }

        if (_connectFromMarker.Id == marker.Id)
        {
            _vm.StatusText = $"„{marker.DisplayName}“ je pořád start — klikni na JINOU lokaci jako cíl.";
            return;
        }

        var from = _connectFromMarker;
        var to = marker;
        var distance = Math.Sqrt(Math.Pow(to.X - from.X, 2) + Math.Pow(to.Y - from.Y, 2));

        UpsertConnection(from.Id, to.Id, distance);
        UpsertConnection(to.Id, from.Id, distance);
        _roadPaths.Remove(PairKey(from.Id, to.Id)); // any previously generated road no longer matches

        _connectFromMarker = null;
        _vm.StatusText = $"Spojení „{from.DisplayName}“ ↔ „{to.DisplayName}“ vytvořeno ({distance:0}m). " +
            "Ulož přes Save, nebo spusť Generate Roads pro terén-vědomou cestu.";
        RenderOverlay();
    }

    private void UpsertConnection(string fromId, string toId, double distanceMeters)
    {
        var idx = _connections.FindIndex(c => c.FromId == fromId && c.ToId == toId);
        if (idx >= 0)
            _connections[idx] = _connections[idx] with { DistanceMeters = distanceMeters };
        else
            _connections.Add(new ConnectionInfo(fromId, toId, distanceMeters));
    }

    private void MarkerDot_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingMarker is null || _grid is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var dot = (Ellipse)sender;
        var pos = e.GetPosition(OverlayCanvas);
        var (worldX, worldY) = CanvasToWorld(pos.X, pos.Y);
        _draggingMarker.X = worldX;
        _draggingMarker.Y = worldY;

        Canvas.SetLeft(dot, pos.X - dot.Width / 2);
        Canvas.SetTop(dot, pos.Y - dot.Height / 2);

        if (_markerLabels.TryGetValue(_draggingMarker.Id, out var label))
        {
            Canvas.SetLeft(label, pos.X + 8);
            Canvas.SetTop(label, pos.Y - 7);
        }

        _vm.CursorWorldPosition = $"X={worldX:0.0}  Y={worldY:0.0}  (přesouvám {_draggingMarker.DisplayName})";
    }

    private void MarkerDot_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ((Ellipse)sender).ReleaseMouseCapture();
        _draggingMarker = null;
        e.Handled = true;
    }

    #endregion Marker dragging
}
