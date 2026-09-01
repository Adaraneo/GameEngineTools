using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Threading.Tasks;
using GameEngineTools.World.Data;
using TerrainEditor.Diagnostics;
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
    /// <summary>When <see cref="_grid"/>.Id is <c>"combined"</c> (a multi-tile stitched view, see
    /// <see cref="EnsureViewportCoverage"/>), the exact original terrain.db tiles it was built
    /// from — so <see cref="SaveToDatabase"/> can split edits back into each source tile instead
    /// of writing one throwaway "combined" row. Empty whenever <see cref="_grid"/> is a single
    /// tile (its own id) or the hand-painted "default" canvas — nothing to split there.</summary>
    private IReadOnlyList<TerrainHeightmap> _combinedSources = [];
    private WriteableBitmap? _bitmap;
    /// <summary>Applied to <see cref="OverlayCanvas"/> so markers/contours/connections track a
    /// mid-drag grid-origin shift cheaply (see <see cref="SwapGridPreservingViewport"/>) without a
    /// full rebuild — reset to (0,0) whenever a full <see cref="RenderOverlay"/> runs.</summary>
    private readonly TranslateTransform _overlayShift = new();
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
        OverlayCanvas.RenderTransform = _overlayShift;

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.IsConnectingLocations) && !_vm.IsConnectingLocations)
                _connectFromMarker = null;

            if (e.PropertyName == nameof(ShellViewModel.Zoom))
            {
                // Re-render only if zoom actually crossed into a different LOD bracket (see
                // ComputeDownsampleFactor) — most individual Ctrl+wheel notches don't, so this
                // skips rebuilding the bitmap on every single one of them.
                if (ComputeDownsampleFactor(_vm.Zoom) != _lastRenderedDownsample)
                    RenderGrid();

                // Zooming OUT (buttons, Reset, or Ctrl+wheel) can reveal area beyond whatever was
                // last stitched by a pan — panning is the only other thing that re-checks coverage,
                // so without this a zoomed-out view can show a black "nothing here" gap at the
                // edges even though real tiles exist there, just not pulled in yet. Deferred to
                // Loaded priority so the ScaleTransform's layout pass (which the Zoom binding
                // drives) has already run — reading ViewportWidth/Height synchronously here would
                // still see the pre-zoom numbers.
                Dispatcher.BeginInvoke(new Action(() => EnsureViewportCoverage()), DispatcherPriority.Loaded);
            }
        };

        // A window resize/maximize also grows the visible viewport without any pan or zoom change
        // — same "might now reach beyond what's stitched" risk as zooming out.
        MapScrollViewer.SizeChanged += (_, _) =>
            Dispatcher.BeginInvoke(new Action(() => EnsureViewportCoverage()), DispatcherPriority.Loaded);

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
        _vm.OpenPerfLogRequested += (_, _) => OpenPerfLog();
    }

    private PerfLogWindow? _perfLogWindow;

    /// <summary>Opens the perf log window, or just focuses it if one is already open — a second
    /// independent window watching the same static <see cref="Diagnostics.PerfLog"/> would just be
    /// confusing (two windows, same data, out of sync scroll position).</summary>
    private void OpenPerfLog()
    {
        if (_perfLogWindow is not null)
        {
            _perfLogWindow.Activate();
            return;
        }

        _perfLogWindow = new PerfLogWindow { Owner = this };
        _perfLogWindow.Closed += (_, _) => _perfLogWindow = null;
        _perfLogWindow.Show();
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
        _combinedSources = [];
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
        _combinedSources = [];
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

            LoadInitialGrid([]);

            RenderGrid();
            RenderOverlay();
            RefreshLocationList();
            Dispatcher.BeginInvoke(new Action(() => { FitZoomToWindow(); EnsureViewportCoverage(); }), DispatcherPriority.Loaded);
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

        LoadInitialGrid(locations);

        RenderGrid();
        RenderOverlay();
        RefreshLocationList();
        Dispatcher.BeginInvoke(new Action(() => { FitZoomToWindow(); EnsureViewportCoverage(); }), DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Picks what to load as the initial <see cref="_grid"/>. If terrain.db actually has TerraGen-
    /// style tiles (anything other than the legacy single "default" canvas), starts from whichever
    /// tile is closest to the loaded locations (or the world origin, with none) — NOT the
    /// "default" id, which usually doesn't exist at all once a world is tile-based; loading it
    /// would silently fall back to a blank <see cref="CreateDefaultGrid"/> canvas that shares no
    /// coordinate frame with the real tiles, so <see cref="EnsureViewportCoverage"/> would never
    /// find anything beyond it — the bug behind "only one tile ever renders, panning never
    /// extends". Falls back to the legacy single "default" grid (or a blank canvas) only when
    /// there are no tiles at all.
    /// </summary>
    private void LoadInitialGrid(IReadOnlyList<LocationInfo> locations)
    {
        var summaries = _vm.WorldDb.ListHeightmaps();
        var tiles = summaries.Where(s => s.Id != WorldDatabaseService.DefaultHeightmapId).ToList();

        if (tiles.Count > 0)
        {
            var startX = locations.Count > 0 ? locations.Average(l => l.X) : 0.0;
            var startY = locations.Count > 0 ? locations.Average(l => l.Y) : 0.0;
            var nearest = tiles.OrderBy(s => DistanceSquaredToCenter(s, startX, startY)).First();
            var tile = _vm.WorldDb.LoadHeightmap(nearest.Id);
            if (tile is not null)
            {
                _grid = tile;
                _combinedSources = [tile];
                return;
            }
        }

        _grid = _vm.WorldDb.LoadHeightmap() ?? CreateDefaultGrid(locations);
        _combinedSources = [];
    }

    private static double DistanceSquaredToCenter(TerrainHeightmapSummary s, double x, double y)
    {
        var cx = s.OriginX + s.Width * s.CellSizeMeters / 2.0;
        var cy = s.OriginY + s.Height * s.CellSizeMeters / 2.0;
        var dx = cx - x;
        var dy = cy - y;
        return dx * dx + dy * dy;
    }

    private void SaveToDatabase()
    {
        if (_grid is null) return;

        // Terrain-only mode has no world.db to write locations/connections into — just save the
        // heightmap itself (InsertLocation/InsertConnection would throw, see RequireWorldOpen).
        if (_vm.WorldDb.IsTerrainOnly)
        {
            SaveGrid();
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

        SaveGrid();
    }

    /// <summary>
    /// Saves <see cref="_grid"/> — but if it's currently a multi-tile stitched view (see
    /// <see cref="EnsureViewportCoverage"/>), splits it back into <see cref="_combinedSources"/>'s
    /// original tiles and saves each of THOSE instead, so painting/erosion/lakes done while
    /// panning across a "combined" view lands back in the individual TerraGen tiles it spans
    /// rather than one throwaway "combined" row.
    /// </summary>
    private void SaveGrid()
    {
        if (_grid is null) return;

        if (_grid.Id == "combined" && _combinedSources.Count > 0)
            TileStitcher.SplitAndSave(_grid, _combinedSources, _vm.WorldDb.SaveHeightmap);
        else
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

    /// <summary>How many grid cells the bitmap skips per sampled pixel when zoomed out — 1 at
    /// 100%+ zoom (full resolution), growing as zoom shrinks, capped at <see cref="MaxDownsample"/>
    /// so an extreme zoom-out doesn't collapse the bitmap to a handful of pixels. Colouring is the
    /// hot loop in <see cref="RenderGrid"/>, and at low zoom most of that detail lands on less than
    /// a screen pixel anyway — WPF's own upscale (see <c>Stretch="Fill"</c> on HeightmapImage in
    /// XAML) fills back in the logical size, just blurrier, which is the expected LOD tradeoff.</summary>
    private static int ComputeDownsampleFactor(double zoom)
        => Math.Clamp((int)Math.Ceiling(1.0 / Math.Max(zoom, 0.01)), 1, MaxDownsample);

    private const int MaxDownsample = 8;

    /// <summary>The downsample factor <see cref="_bitmap"/> was actually built at — lets the
    /// Zoom-changed handler skip re-rendering when zoom moved but stayed within the same LOD
    /// bracket (e.g. Ctrl+wheel ticks that don't cross a <see cref="ComputeDownsampleFactor"/>
    /// boundary), instead of rebuilding the bitmap on every single zoom notch.</summary>
    private int _lastRenderedDownsample = 1;

    private unsafe void RenderGrid()
    {
        if (_grid is null) return;

        var downsample = ComputeDownsampleFactor(_vm.Zoom);
        _lastRenderedDownsample = downsample;
        var bitmapWidth = Math.Max(1, (_grid.Width + downsample - 1) / downsample);
        var bitmapHeight = Math.Max(1, (_grid.Height + downsample - 1) / downsample);
        using var perfScope = PerfLog.Scope("RenderGrid",
            $"grid {_grid.Width}x{_grid.Height} (id={_grid.Id}) -> bitmap {bitmapWidth}x{bitmapHeight} (downsample {downsample}x)");

        if (_bitmap is null || _bitmap.PixelWidth != bitmapWidth || _bitmap.PixelHeight != bitmapHeight)
        {
            _bitmap = new WriteableBitmap(bitmapWidth, bitmapHeight, 96, 96, PixelFormats.Bgr32, null);
            HeightmapImage.Source = _bitmap;
        }

        // The LOGICAL size (what the ScrollViewer scrolls over, what WorldToCanvas/CanvasToWorld
        // assume 1 unit = 1 grid cell against) always matches the grid's true cell count, whatever
        // the bitmap's own (possibly downsampled) pixel resolution is — Stretch="Fill" is what
        // makes WPF stretch a smaller bitmap back up to fill this same box.
        HeightmapImage.Width = _grid.Width;
        HeightmapImage.Height = _grid.Height;
        OverlayCanvas.Width = _grid.Width;
        OverlayCanvas.Height = _grid.Height;

        var min = _grid.Values.Min();
        var max = _grid.Values.Max();
        var grid = _grid; // local capture — Parallel.For bodies below run on other threads

        // Bgr32: 4 bytes/pixel (B, G, R, unused), elevation-tinted — blue below sea level (0m),
        // beach/green/brown/gray/snow above it, river cells overridden to freshwater teal.
        // See TerrainColorRamp for the exact bands.
        //
        // Writes straight into the bitmap's own native back buffer instead of building a managed
        // byte[] and handing it to WritePixels — WritePixels would just copy that array into this
        // same native memory anyway, so for a combined grid that can be well over a million pixels
        // this skips one whole extra full-size copy (and the array allocation that used to go with
        // it) on every pan/zoom-triggered re-render.
        _bitmap.Lock();
        var backBuffer = (byte*)_bitmap.BackBuffer;
        var stride = _bitmap.BackBufferStride;

        // Each pixel only reads its own cell and writes its own 4 bytes — no shared mutable state
        // between rows, so this is safe to parallelize across cores. TerrainColorRamp.ForCell is a
        // pure function over static readonly data. Worthwhile because this is the actual hot loop:
        // up to bitmapWidth*bitmapHeight TerrainColorRamp calls every time the grid is (re)rendered
        // (== grid.Width*grid.Height only when downsample is 1, i.e. at 100%+ zoom).
        Parallel.For(0, bitmapHeight, by =>
        {
            var rowPtr = backBuffer + by * stride;
            var gy = Math.Min(by * downsample, grid.Height - 1);
            var rowStart = gy * grid.Width;
            for (var bx = 0; bx < bitmapWidth; bx++)
            {
                var gx = Math.Min(bx * downsample, grid.Width - 1);
                var i = rowStart + gx;
                var isRiver = grid.RiverMask is { } mask && mask[i] != 0;
                var color = TerrainColorRamp.ForCell(grid.Values[i], min, max, isRiver);
                var pixelPtr = rowPtr + bx * 4;
                pixelPtr[0] = color.B;
                pixelPtr[1] = color.G;
                pixelPtr[2] = color.R;
                pixelPtr[3] = 255;
            }
        });

        _bitmap.AddDirtyRect(new Int32Rect(0, 0, bitmapWidth, bitmapHeight));
        _bitmap.Unlock();
    }

    /// <summary>Rebuilds contour lines + location markers. Contours are recomputed here only —
    /// on stroke end, not every mouse-move — because marching squares over the whole grid is
    /// too expensive to run per pixel-drag. During a live pan drag, this whole method is skipped
    /// entirely instead (see <see cref="SwapGridPreservingViewport"/>).</summary>
    private void RenderOverlay()
    {
        if (_grid is null) return;
        using var perfScope = PerfLog.Scope("RenderOverlay",
            $"grid {_grid.Width}x{_grid.Height}, {_markers.Count} markerů, {_connections.Count} spojení (marching squares)");

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
    /// <summary>Screen position <see cref="EnsureViewportCoverage"/> last ran from during the
    /// current drag — <c>null</c> means "not checked yet this drag". Throttles the check to once
    /// per <see cref="CoverageCheckThresholdPixels"/> of mouse movement instead of every single
    /// MouseMove event (which fires far more often than the view can actually need re-stitching).</summary>
    private Point? _lastCoverageCheckPos;
    private const double CoverageCheckThresholdPixels = 40.0;

    /// <summary>Middle-mouse-button drag pans the map — doesn't collide with left-click, which
    /// is already spoken for (select/drag a marker, place a location, wire up a connection).
    /// Preview so it captures the press before any child (marker dot, heightmap image) sees it.</summary>
    private void MapScrollViewer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;

        _panStartMousePos = e.GetPosition(MapScrollViewer);
        _panStartHOffset = MapScrollViewer.HorizontalOffset;
        _panStartVOffset = MapScrollViewer.VerticalOffset;
        _lastCoverageCheckPos = null;
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

        // Re-stitching (SQL round-trips, tile decode, a full RenderGrid) is comparatively
        // expensive — checking on every MouseMove during a fast drag would run it far more often
        // than the visible area actually changes enough to need it.
        if (_lastCoverageCheckPos is not { } lastCheck || (pos - lastCheck).Length >= CoverageCheckThresholdPixels)
        {
            _lastCoverageCheckPos = pos;
            EnsureViewportCoverage(isLiveDrag: true);
        }
    }

    private void MapScrollViewer_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || _panStartMousePos is null) return;

        _panStartMousePos = null;
        MapScrollViewer.ReleaseMouseCapture();
        Mouse.OverrideCursor = null;
        e.Handled = true;

        // Unconditional (not throttled) — guarantees the final drag position is covered even if
        // the last few pixels of movement didn't cross the throttle threshold above.
        EnsureViewportCoverage();
    }

    /// <summary>
    /// Middle-mouse panning across a continuous terrain.db surface: if the visible viewport has
    /// panned close to (or past) the currently-loaded <see cref="_grid"/>'s edge, re-stitches a
    /// wider combined view from whichever ADJACENT tiles are already saved in terrain.db (see
    /// <see cref="TileStitcher"/>) — never generates new terrain, only pulls in what TerraGen has
    /// already produced. A no-op when there's nothing beyond the current grid (a single hand-
    /// painted "default" canvas, or panning still safely inside the loaded area).
    /// </summary>
    private void EnsureViewportCoverage(bool isLiveDrag = false)
    {
        if (_grid is null || !_vm.WorldDb.IsOpen) return;

        // Forces ViewportWidth/Height/HorizontalOffset etc. to reflect any pending layout change
        // (e.g. a Zoom update from FitZoomToWindow moments earlier in the same dispatch) instead
        // of stale pre-layout numbers — a no-op, and cheap, when layout is already clean (the
        // common case during a live pan, which only changes scroll offsets, not layout).
        MapScrollViewer.UpdateLayout();

        var viewportCanvasLeft = MapScrollViewer.HorizontalOffset / _vm.Zoom;
        var viewportCanvasTop = MapScrollViewer.VerticalOffset / _vm.Zoom;
        var viewportCanvasRight = viewportCanvasLeft + MapScrollViewer.ViewportWidth / _vm.Zoom;
        var viewportCanvasBottom = viewportCanvasTop + MapScrollViewer.ViewportHeight / _vm.Zoom;
        if (viewportCanvasRight <= viewportCanvasLeft || viewportCanvasBottom <= viewportCanvasTop)
            return; // viewport not laid out yet

        var (minWorldX, minWorldY) = CanvasToWorld(viewportCanvasLeft, viewportCanvasTop);
        var (maxWorldX, maxWorldY) = CanvasToWorld(viewportCanvasRight, viewportCanvasBottom);

        // Small slack so a reload doesn't fire from a single pixel of scroll jitter right at the edge.
        var margin = 20.0 * _grid.CellSizeMeters;
        var gridMinX = _grid.OriginX;
        var gridMinY = _grid.OriginY;
        var gridMaxX = _grid.OriginX + _grid.Width * _grid.CellSizeMeters;
        var gridMaxY = _grid.OriginY + _grid.Height * _grid.CellSizeMeters;

        var needsExtend = minWorldX < gridMinX + margin || minWorldY < gridMinY + margin
            || maxWorldX > gridMaxX - margin || maxWorldY > gridMaxY - margin;
        if (!needsExtend)
        {
            if (!isLiveDrag)
                PerfLog.Log("Coverage", "Kontrola pokrytí: aktuální dlaždice stačí, nic se nenačítá.");
            return;
        }
        PerfLog.Log("Coverage", $"Kontrola pokrytí: hranice dosažena (liveDrag={isLiveDrag}), spouštím re-stitch na pozadí.");

        // Pad a bit beyond the visible box so a follow-up pan in the same direction doesn't
        // immediately need yet another reload. Deliberately half the viewport, not a full one —
        // a bigger pad means fewer re-stitches but a much bigger (and more expensive to build and
        // render) combined grid each time; half a viewport is enough slack for the pixel-jitter
        // scroll deltas a live drag actually produces between throttled coverage checks.
        const double PaddingFraction = 0.5;
        var padX = (maxWorldX - minWorldX) * PaddingFraction;
        var padY = (maxWorldY - minWorldY) * PaddingFraction;
        var boxMinX = minWorldX - padX;
        var boxMinY = minWorldY - padY;
        var boxMaxX = maxWorldX + padX;
        var boxMaxY = maxWorldY + padY;

        // The actual work here — a SQL round-trip (ListHeightmaps) plus decoding and index-copying
        // however many tiles overlap the box — used to run synchronously on the UI thread, which
        // is exactly why panning stuttered: every throttled coverage check blocked mouse-move
        // handling until it finished. Only the WPF part at the end (SwapGridPreservingViewport —
        // touches _grid/_bitmap/OverlayCanvas) has to stay on the UI thread; the loading/stitching
        // itself doesn't touch any UI object, so it runs on the thread pool instead.
        var worldDb = _vm.WorldDb;
        var token = new object();
        _pendingStitchToken = token;

        Task.Run(() =>
        {
            using var scope = PerfLog.Scope("Stitch", "Načítání + skládání dlaždic z terrain.db (SQL + dekódování, mimo UI vlákno)");
            try
            {
                var summaries = worldDb.ListHeightmaps();
                var result = TileStitcher.BuildCombinedGrid(summaries, worldDb.LoadHeightmap, boxMinX, boxMinY, boxMaxX, boxMaxY);
                PerfLog.Log("Stitch", result.Combined is { } c
                    ? $"Výsledek: {result.Sources.Count} zdrojových dlaždic -> combined {c.Width}x{c.Height}"
                    : "Výsledek: žádné dlaždice v oblasti, combined = null");
                return result;
            }
            catch (Exception ex)
            {
                // Best-effort: e.g. the database was closed/reopened while this was in flight.
                // The token check below would likely have discarded the result anyway; this just
                // keeps a lost race from crashing the background thread.
                PerfLog.Log("Stitch", $"Chyba při skládání (ignorováno, race s zavřením db?): {ex.GetType().Name}: {ex.Message}");
                return (Combined: null, Sources: (IReadOnlyList<TerrainHeightmap>)[]);
            }
        }).ContinueWith(t =>
        {
            // Superseded by a newer coverage check (further panning, a zoom, a totally different
            // database opened) that started after this one — its own result (or lack of one) wins.
            if (!ReferenceEquals(_pendingStitchToken, token))
            {
                PerfLog.Log("Coverage", "Výsledek zahozen — mezitím spuštěná novější kontrola pokrytí ho nahradila.");
                return;
            }
            if (_grid is null) return; // database was closed while this was in flight

            var (combined, sources) = t.Result;
            if (combined is null) return;

            var unchanged = Math.Abs(combined.OriginX - _grid.OriginX) < 1e-6 && Math.Abs(combined.OriginY - _grid.OriginY) < 1e-6
                && combined.Width == _grid.Width && combined.Height == _grid.Height;
            if (unchanged)
            {
                PerfLog.Log("Coverage", "Nová combined mřížka je shodná s aktuální — swap přeskočen.");
                return;
            }

            SwapGridPreservingViewport(combined, sources, isLiveDrag);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Identifies the most recently STARTED background stitch (see
    /// <see cref="EnsureViewportCoverage"/>) — its continuation is the only one allowed to apply
    /// its result, so a slow earlier request that finishes after a newer one can't overwrite it.</summary>
    private object? _pendingStitchToken;

    /// <summary>Replaces <see cref="_grid"/> (and <see cref="_combinedSources"/>) and re-renders,
    /// keeping whatever world-space point was centered in the viewport still centered afterward —
    /// the new grid's OriginX/OriginY generally differ from the old one's, so the raw scroll
    /// offsets would otherwise land on the wrong spot.</summary>
    private void SwapGridPreservingViewport(TerrainHeightmap newGrid, IReadOnlyList<TerrainHeightmap> sources, bool isLiveDrag)
    {
        var viewportCenterCanvasX = (MapScrollViewer.HorizontalOffset + MapScrollViewer.ViewportWidth / 2.0) / _vm.Zoom;
        var viewportCenterCanvasY = (MapScrollViewer.VerticalOffset + MapScrollViewer.ViewportHeight / 2.0) / _vm.Zoom;
        var (worldCenterX, worldCenterY) = CanvasToWorld(viewportCenterCanvasX, viewportCenterCanvasY);

        var oldGrid = _grid;
        _grid = newGrid;
        _combinedSources = sources;
        RenderGrid();

        // Full overlay rebuild (contours via marching squares over a possibly million-plus-cell
        // grid, plus every marker/connection) is too expensive to run on every intermediate swap
        // during a live drag. Earlier this just SKIPPED the overlay during a drag instead — but
        // losing every location marker/contour mid-pan is exactly what made panning feel
        // disorienting ("don't know where I am"). Cheaper middle ground: shift the EXISTING overlay
        // visuals by the exact canvas-space delta the grid's own origin just moved (one
        // TranslateTransform on the whole OverlayCanvas — O(1), not O(markers)) so they track the
        // new grid correctly without a full rebuild, then do the real rebuild once the drag ends
        // (MapScrollViewer_PreviewMouseUp always calls this with isLiveDrag=false).
        if (isLiveDrag && oldGrid is not null)
        {
            var deltaCanvasX = (oldGrid.OriginX - newGrid.OriginX) / newGrid.CellSizeMeters;
            var deltaCanvasY = (oldGrid.OriginY - newGrid.OriginY) / newGrid.CellSizeMeters;
            _overlayShift.X += deltaCanvasX;
            _overlayShift.Y += deltaCanvasY;
        }
        else
        {
            RenderOverlay();
            _overlayShift.X = 0;
            _overlayShift.Y = 0;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            MapScrollViewer.UpdateLayout();
            var (cx, cy) = WorldToCanvas(worldCenterX, worldCenterY);
            MapScrollViewer.ScrollToHorizontalOffset(cx * _vm.Zoom - MapScrollViewer.ViewportWidth / 2.0);
            MapScrollViewer.ScrollToVerticalOffset(cy * _vm.Zoom - MapScrollViewer.ViewportHeight / 2.0);
        }), DispatcherPriority.Loaded);
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
