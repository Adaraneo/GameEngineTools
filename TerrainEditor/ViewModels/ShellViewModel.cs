using System.IO;
using System.Windows;
using Microsoft.Win32;
using TerrainEditor.Commands;
using TerrainEditor.Models;
using TerrainEditor.Services;

namespace TerrainEditor.ViewModels;

/// <summary>
/// Top-level view model: owns the world-database connection, brush settings, and status text.
/// The actual heightmap paint surface / location dragging lives in MainWindow's code-behind
/// (a WriteableBitmap + overlay Canvas aren't practically MVVM-bindable) — this VM coordinates
/// via events instead of owning that state directly.
/// </summary>
public sealed class ShellViewModel : ViewModelBase
{
    public ShellViewModel(WorldDatabaseService worldDb)
    {
        WorldDb = worldDb;

        OpenDatabaseCommand = new RelayCommand(OpenDatabase);
        NewWorldCommand = new RelayCommand(NewWorld);
        OpenTerrainOnlyCommand = new RelayCommand(OpenTerrainOnly);
        SaveCommand = new RelayCommand(Save, () => WorldDb.IsOpen);
        // Locations/Connections don't exist in terrain-only mode (OpenTerrainOnly) — no world.db.
        ExportSeedCommand = new RelayCommand(ExportSeed, () => WorldDb.IsOpen && !WorldDb.IsTerrainOnly);
        GenerateRoadsCommand = new RelayCommand(
            () => GenerateRoadsRequested?.Invoke(this, EventArgs.Empty),
            () => WorldDb.IsOpen && !WorldDb.IsTerrainOnly);
        GenerateLakesCommand = new RelayCommand(
            () => GenerateLakesRequested?.Invoke(this, EventArgs.Empty),
            () => WorldDb.IsOpen);
        AssignRegionsCommand = new RelayCommand(
            () => AssignRegionsRequested?.Invoke(this, EventArgs.Empty),
            () => WorldDb.IsOpen && !WorldDb.IsTerrainOnly);
        GoToLatLonCommand = new RelayCommand(
            () => GoToLatLonRequested?.Invoke(this, EventArgs.Empty),
            () => WorldDb.IsOpen);
        OpenTileBrowserCommand = new RelayCommand(
            () => OpenTileBrowserRequested?.Invoke(this, EventArgs.Empty),
            () => WorldDb.IsOpen);

        ZoomInCommand = new RelayCommand(() => Zoom *= 1.25);
        ZoomOutCommand = new RelayCommand(() => Zoom /= 1.25);
        ZoomResetCommand = new RelayCommand(() => Zoom = 1.0);
    }

    public WorldDatabaseService WorldDb { get; }

    public RelayCommand OpenDatabaseCommand { get; }
    public RelayCommand NewWorldCommand { get; }
    public RelayCommand OpenTerrainOnlyCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand ExportSeedCommand { get; }
    public RelayCommand GenerateRoadsCommand { get; }
    public RelayCommand GenerateLakesCommand { get; }
    public RelayCommand AssignRegionsCommand { get; }
    public RelayCommand GoToLatLonCommand { get; }
    public RelayCommand OpenTileBrowserCommand { get; }

    private bool _isAddingLocation;
    /// <summary>Arms "click the map to place a new location" mode — MainWindow checks this on the
    /// heightmap's mouse-down and disarms it after placing one (single-shot, not a persistent tool).</summary>
    public bool IsAddingLocation { get => _isAddingLocation; set => SetProperty(ref _isAddingLocation, value); }

    private bool _isConnectingLocations;
    /// <summary>Arms "click two locations to connect them" mode — MainWindow tracks the first
    /// click and completes the connection (both directions) on the second. Stays armed after
    /// completing one so you can chain several connections without re-toggling; MainWindow resets
    /// its pending "from" marker whenever this goes back to false.</summary>
    public bool IsConnectingLocations { get => _isConnectingLocations; set => SetProperty(ref _isConnectingLocations, value); }

    private LocationMarkerViewModel? _selectedMarker;
    /// <summary>The location last clicked on the map — MainWindow sets this on marker mouse-down.
    /// Bound directly in XAML (the Region textbox) so edits write straight into the marker, the
    /// same object Save later persists.</summary>
    public LocationMarkerViewModel? SelectedMarker { get => _selectedMarker; set => SetProperty(ref _selectedMarker, value); }

    private double _terrainAmplitude = 200.0;
    /// <summary>Peak-to-trough elevation range (meters) for generated terrain.</summary>
    public double TerrainAmplitude { get => _terrainAmplitude; set => SetProperty(ref _terrainAmplitude, value); }

    private double _erosionStrength = 50.0;
    /// <summary>0-100 dial for how much hydraulic erosion to run after generating terrain — 0
    /// leaves the raw landmass+mountain noise untouched, 100 runs heavy erosion. MainWindow scales
    /// this into an actual droplet count relative to the grid's own cell count.</summary>
    public double ErosionStrength { get => _erosionStrength; set => SetProperty(ref _erosionStrength, Math.Clamp(value, 0.0, 100.0)); }

    private double _targetLatitude;
    /// <summary>Latitude (degrees, -90..90) to jump to via "Go to Lat/Long" — bound to the toolbar
    /// textbox; MainWindow reads it when the command fires.</summary>
    public double TargetLatitude { get => _targetLatitude; set => SetProperty(ref _targetLatitude, Math.Clamp(value, -90.0, 90.0)); }

    private double _targetLongitude;
    /// <summary>Longitude (degrees, -180..180) to jump to via "Go to Lat/Long".</summary>
    public double TargetLongitude { get => _targetLongitude; set => SetProperty(ref _targetLongitude, Math.Clamp(value, -180.0, 180.0)); }

    private double _targetMapSizeKm = 5.0;
    /// <summary>Width/height (km) of the local window "Go to Lat/Long" builds — a single click
    /// both sizes AND generates the region at the chosen planetary position, instead of the old
    /// two-step "Expand Map, then remember to Generate Terrain again" dance.</summary>
    public double TargetMapSizeKm { get => _targetMapSizeKm; set => SetProperty(ref _targetMapSizeKm, Math.Max(value, 0.05)); }

    private double? _currentCenterLatitude;
    /// <summary>Center latitude of the currently-generated local window — null until the first
    /// sphere-based generation (a flat "Generate Terrain" doesn't have a planetary position).
    /// Set by MainWindow after <c>GoToLatLon</c>, shown in the status area for orientation.</summary>
    public double? CurrentCenterLatitude { get => _currentCenterLatitude; set { SetProperty(ref _currentCenterLatitude, value); OnPropertyChanged(nameof(CurrentCenterLabel)); } }

    private double? _currentCenterLongitude;
    public double? CurrentCenterLongitude { get => _currentCenterLongitude; set { SetProperty(ref _currentCenterLongitude, value); OnPropertyChanged(nameof(CurrentCenterLabel)); } }

    public string CurrentCenterLabel => CurrentCenterLatitude is { } lat && CurrentCenterLongitude is { } lon
        ? $"{lat:0.00}°, {lon:0.00}°"
        : "(plochá generace, žádná planetární pozice)";

    public RelayCommand ZoomInCommand { get; }
    public RelayCommand ZoomOutCommand { get; }
    public RelayCommand ZoomResetCommand { get; }

    public const double MinZoom = 0.1;
    public const double MaxZoom = 10.0;

    private double _zoom = 1.0;
    /// <summary>Map canvas scale factor, applied via a LayoutTransform in MainWindow so mouse
    /// coordinates read against the Image/Canvas stay in true grid-cell space regardless of zoom.</summary>
    public double Zoom
    {
        get => _zoom;
        set => SetProperty(ref _zoom, Math.Clamp(value, MinZoom, MaxZoom));
    }

    private string _statusText = "Otevři world.db tlačítkem „Open World DB…“, nebo jen terrain.db tlačítkem „Open Terrain Only…“.";
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    private string _cursorWorldPosition = string.Empty;
    public string CursorWorldPosition { get => _cursorWorldPosition; set => SetProperty(ref _cursorWorldPosition, value); }

    /// <summary>Raised after a database is (re)opened — MainWindow (re)loads the heightmap, and
    /// also the locations/connections unless <see cref="Services.WorldDatabaseService.IsTerrainOnly"/> is true.</summary>
    public event EventHandler? DatabaseOpened;

    /// <summary>Raised when Save is requested — MainWindow writes its current in-memory state back.</summary>
    public event EventHandler? SaveRequested;

    /// <summary>Raised when Export is requested, carrying the chosen destination file path.</summary>
    public event EventHandler<string>? ExportRequested;

    /// <summary>Raised when "Generate Roads" is clicked — MainWindow runs terrain-aware
    /// pathfinding for every loaded Connection and redraws the road overlay.</summary>
    public event EventHandler? GenerateRoadsRequested;

    /// <summary>Raised when "Generate Lakes" is clicked — MainWindow floods basins in the
    /// current terrain.</summary>
    public event EventHandler? GenerateLakesRequested;

    /// <summary>Raised when "Assign Regions" is clicked — MainWindow suggests a Region label
    /// for every loaded location from its terrain context.</summary>
    public event EventHandler? AssignRegionsRequested;

    /// <summary>Raised when "Go to Lat/Long" is clicked — MainWindow regenerates the current
    /// local window centered on (<see cref="TargetLatitude"/>, <see cref="TargetLongitude"/>).</summary>
    public event EventHandler? GoToLatLonRequested;

    /// <summary>Raised when "Dlaždice…" is clicked — MainWindow opens a list of every heightmap
    /// saved in the current terrain.db (e.g. tiles from a TerraGen batch run) to pick one to load.</summary>
    public event EventHandler? OpenTileBrowserRequested;

    private void OpenDatabase()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open world.db",
            Filter = "SQLite database (*.db)|*.db|All files (*.*)|*.*",
            CheckFileExists = false, // a not-yet-existing path is fine — it gets created and seeded
        };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            WorldDb.Open(dlg.FileName);
            var cosmologyNote = WorldDb.CosmologyConfig is not null
                ? $"kosmologie načtena z {WorldSettingsLoader.FileName}"
                : $"{WorldSettingsLoader.FileName} nenalezen ve složce databáze ani jejích rodičích, používají se výchozí hodnoty";
            OnDatabaseOpened($"Otevřeno: {dlg.FileName} ({cosmologyNote})");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Nepodařilo se otevřít databázi:\n{ex.Message}", "Chyba",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Creates a brand-new, entirely empty world (schema only, no default castle/village
    /// locations) and opens it — for building a custom world from scratch using the editor's own
    /// tools (Add Location, Generate Terrain, ...) instead of starting from the built-in seed.
    /// </summary>
    private void NewWorld()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Nový svět (world.db)",
            Filter = "SQLite database (*.db)|*.db|All files (*.*)|*.*",
            FileName = "world.db",
        };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            // SaveFileDialog already asked the user to confirm overwriting an existing file at
            // this path — deleting it (plus WAL/SHM side files from any previous SQLite session)
            // is what makes "New World" an actually clean slate rather than just attaching to
            // whatever schema-idempotent tables were already there.
            foreach (var candidate in new[] { dlg.FileName, dlg.FileName + "-wal", dlg.FileName + "-shm" })
            {
                if (File.Exists(candidate))
                    File.Delete(candidate);
            }

            WorldDb.OpenBlank(dlg.FileName);
            OnDatabaseOpened($"Nový prázdný svět vytvořen: {dlg.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Nepodařilo se vytvořit nový svět:\n{ex.Message}", "Chyba",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Opens ONLY a <c>terrain.db</c> — no paired world.db is created or required. For working
    /// purely with heightmap tiles (e.g. inspecting/editing a standalone terrain.db produced by
    /// TerraGen or WorldObserver) without a world to go with it. Location/road/region/export
    /// commands are disabled while this mode is active — see <see cref="WorldDatabaseService.IsTerrainOnly"/>.
    /// </summary>
    private void OpenTerrainOnly()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open terrain.db (only)",
            Filter = "SQLite database (*.db)|*.db|All files (*.*)|*.*",
            CheckFileExists = false, // a not-yet-existing path is fine — it gets created and seeded
        };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            WorldDb.OpenTerrainOnly(dlg.FileName);
            OnDatabaseOpened($"Otevřeno pouze terén (bez world.db): {dlg.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Nepodařilo se otevřít terénní databázi:\n{ex.Message}", "Chyba",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Shared post-open bookkeeping for <see cref="OpenDatabase"/>, <see cref="NewWorld"/>,
    /// and <see cref="OpenTerrainOnly"/>.</summary>
    private void OnDatabaseOpened(string statusText)
    {
        StatusText = statusText;
        SaveCommand.RaiseCanExecuteChanged();
        ExportSeedCommand.RaiseCanExecuteChanged();
        GenerateRoadsCommand.RaiseCanExecuteChanged();
        GenerateLakesCommand.RaiseCanExecuteChanged();
        AssignRegionsCommand.RaiseCanExecuteChanged();
        GoToLatLonCommand.RaiseCanExecuteChanged();
        OpenTileBrowserCommand.RaiseCanExecuteChanged();
        CurrentCenterLatitude = null;
        CurrentCenterLongitude = null;
        DatabaseOpened?.Invoke(this, EventArgs.Empty);
    }

    private void Save()
    {
        SaveRequested?.Invoke(this, EventArgs.Empty);
        StatusText = "Uloženo.";
    }

    private void ExportSeed()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Export seed_data.sql",
            Filter = "SQL script (*.sql)|*.sql|All files (*.*)|*.*",
            FileName = "seed_data.sql",
        };
        if (dlg.ShowDialog() != true)
            return;

        ExportRequested?.Invoke(this, dlg.FileName);
        StatusText = $"Exportováno: {dlg.FileName}";
    }
}
