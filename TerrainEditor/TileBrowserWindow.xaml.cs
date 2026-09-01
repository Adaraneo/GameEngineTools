using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameEngineTools.World.Data;
using TerrainEditor.Rendering;

namespace TerrainEditor;

/// <summary>
/// Non-modal browser over every heightmap saved in the open terrain.db — renders a composite map
/// stitched from the tiles' ACTUAL saved elevation data (positioned by their real OriginX/OriginY,
/// e.g. tiles from a TerraGen batch run), not a fresh noise preview like the old Planet Overview
/// was. Pick a tile by clicking the map or the list, then "Načíst" loads it into the main window's
/// editing surface. Tiles must share one flat local coordinate frame to compose meaningfully — true
/// for tiles from a single TerraGen run, since they all measure position from that run's own shared
/// reference point (see TerraGen's TileGenerator remarks); mixing tiles from unrelated runs/frames
/// would just place them at whatever numeric OriginX/OriginY they happen to carry.
/// </summary>
public partial class TileBrowserWindow : Window
{
    private readonly List<TerrainHeightmap> _tiles;
    private readonly Dictionary<string, Row> _rowsById = new();

    private double _minX, _minY, _worldWidth, _worldHeight;
    private int _pixelWidth, _pixelHeight;

    /// <summary>Raised when "Načíst" (or a double-click on the list/map) confirms a pick, carrying
    /// the chosen heightmap's id.</summary>
    public event Action<string>? TileChosen;

    public TileBrowserWindow(Func<string, TerrainHeightmap?> loadTile, IReadOnlyList<TerrainHeightmapSummary> summaries)
    {
        InitializeComponent();

        _tiles = summaries
            .Select(s => loadTile(s.Id))
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList();

        var rows = summaries
            .Where(s => _tiles.Any(t => t.Id == s.Id))
            .Select(s => new Row(s.Id, s.OriginX, s.OriginY))
            .ToList();
        foreach (var row in rows) _rowsById[row.Id] = row;
        TileListView.ItemsSource = rows;

        RenderMap();
    }

    private void RenderMap()
    {
        if (_tiles.Count == 0)
        {
            MapEmptyLabel.Visibility = Visibility.Visible;
            return;
        }

        _minX = _tiles.Min(t => t.OriginX);
        _minY = _tiles.Min(t => t.OriginY);
        var maxX = _tiles.Max(t => t.OriginX + t.Width * t.CellSizeMeters);
        var maxY = _tiles.Max(t => t.OriginY + t.Height * t.CellSizeMeters);
        _worldWidth = Math.Max(maxX - _minX, 1.0);
        _worldHeight = Math.Max(maxY - _minY, 1.0);

        const int MaxDimension = 640;
        var aspect = _worldWidth / _worldHeight;
        if (aspect >= 1.0)
        {
            _pixelWidth = MaxDimension;
            _pixelHeight = Math.Max(1, (int)(MaxDimension / aspect));
        }
        else
        {
            _pixelHeight = MaxDimension;
            _pixelWidth = Math.Max(1, (int)(MaxDimension * aspect));
        }

        var globalMin = _tiles.SelectMany(t => t.Values).Min();
        var globalMax = _tiles.SelectMany(t => t.Values).Max();
        var background = Color.FromRgb(0x15, 0x15, 0x15); // "not generated" areas between/around tiles

        var bitmap = new WriteableBitmap(_pixelWidth, _pixelHeight, 96, 96, PixelFormats.Bgr32, null);
        var stride = _pixelWidth * 4;
        var pixels = new byte[stride * _pixelHeight];

        for (var py = 0; py < _pixelHeight; py++)
        {
            var worldY = _minY + py / (double)Math.Max(_pixelHeight - 1, 1) * _worldHeight;
            for (var px = 0; px < _pixelWidth; px++)
            {
                var worldX = _minX + px / (double)Math.Max(_pixelWidth - 1, 1) * _worldWidth;
                var tile = FindTileContaining(worldX, worldY);
                var color = tile is null
                    ? background
                    : TerrainColorRamp.ForHeight((float)tile.SampleAt(worldX, worldY), globalMin, globalMax);

                var offset = py * stride + px * 4;
                pixels[offset] = color.B;
                pixels[offset + 1] = color.G;
                pixels[offset + 2] = color.R;
                pixels[offset + 3] = 255;
            }
        }

        bitmap.WritePixels(new Int32Rect(0, 0, _pixelWidth, _pixelHeight), pixels, stride, 0);
        MapImage.Source = bitmap;
    }

    private TerrainHeightmap? FindTileContaining(double worldX, double worldY)
    {
        foreach (var t in _tiles)
        {
            var maxX = t.OriginX + t.Width * t.CellSizeMeters;
            var maxY = t.OriginY + t.Height * t.CellSizeMeters;
            if (worldX >= t.OriginX && worldX <= maxX && worldY >= t.OriginY && worldY <= maxY)
                return t;
        }
        return null;
    }

    private void MapImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (MapImage.ActualWidth <= 0 || MapImage.ActualHeight <= 0 || _tiles.Count == 0) return;

        var pos = e.GetPosition(MapImage);
        var worldX = _minX + pos.X / MapImage.ActualWidth * _worldWidth;
        var worldY = _minY + pos.Y / MapImage.ActualHeight * _worldHeight;

        var tile = FindTileContaining(worldX, worldY);
        if (tile is null) return;

        Select(tile.Id);
        if (e.ClickCount >= 2) Confirm();
    }

    private void TileListView_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (TileListView.SelectedItem is Row row) Select(row.Id, syncList: false);
        else { LoadButton.IsEnabled = false; SelectionLabel.Text = "Nic nevybráno"; }
    }

    private void Select(string id, bool syncList = true)
    {
        LoadButton.IsEnabled = true;
        SelectionLabel.Text = $"Vybráno: {id}";
        if (syncList && _rowsById.TryGetValue(id, out var row))
            TileListView.SelectedItem = row;
    }

    private void LoadButton_Click(object sender, RoutedEventArgs e) => Confirm();

    private void TileListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TileListView.SelectedItem is not null) Confirm();
    }

    private void Confirm()
    {
        if (TileListView.SelectedItem is Row row)
            TileChosen?.Invoke(row.Id);
    }

    private sealed record Row(string Id, double OriginX, double OriginY);
}
