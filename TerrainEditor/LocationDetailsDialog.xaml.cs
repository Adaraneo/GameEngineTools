using System.Globalization;
using System.Windows;
using GameEngineTools.World.Location;
using TerrainEditor.Models;

namespace TerrainEditor;

/// <summary>
/// Modal prompt for a location's name and social parameters (the fields
/// <see cref="GameEngineTools.World.Location.LocationDescriptor"/> needs that a map click/drag
/// alone can't supply). Serves two callers: a newly placed marker (no <paramref name="existing"/>
/// — starts from neutral defaults, "Vytvořit") and double-clicking an existing marker to edit it
/// (<paramref name="existing"/> pre-fills every field, "Uložit").
/// </summary>
public partial class LocationDetailsDialog : Window
{
    private readonly bool _isEditMode;

    public string EnteredName { get; private set; } = string.Empty;
    public LocationType SelectedType { get; private set; } = LocationType.Public;
    public TerrainType SelectedTerrain { get; private set; } = TerrainType.Courtyard;
    public double BaseNoise { get; private set; } = 0.2;
    public double NoisePerPerson { get; private set; } = 0.04;
    public int Capacity { get; private set; } = 20;
    public bool AllowsPrivacy { get; private set; }

    public LocationDetailsDialog(LocationInfo? existing = null)
    {
        InitializeComponent();

        TypeComboBox.ItemsSource = Enum.GetValues<LocationType>();
        TerrainComboBox.ItemsSource = Enum.GetValues<TerrainType>();

        _isEditMode = existing is not null;
        if (existing is { } info)
        {
            Title = $"Upravit lokaci — {info.DisplayName}";
            OkButton.Content = "Uložit";
            NameTextBox.Text = info.DisplayName;
            TypeComboBox.SelectedItem = info.Type;
            TerrainComboBox.SelectedItem = info.Terrain;
            BaseNoiseTextBox.Text = info.BaseNoise.ToString(CultureInfo.InvariantCulture);
            NoisePerPersonTextBox.Text = info.NoisePerPerson.ToString(CultureInfo.InvariantCulture);
            CapacityTextBox.Text = info.Capacity.ToString(CultureInfo.InvariantCulture);
            AllowsPrivacyCheckBox.IsChecked = info.AllowsPrivacy;
        }
        else
        {
            Title = "Nová lokace";
            TypeComboBox.SelectedItem = LocationType.Public;
            TerrainComboBox.SelectedItem = TerrainType.Courtyard;
        }

        Loaded += (_, _) =>
        {
            NameTextBox.Focus();
            if (_isEditMode) NameTextBox.SelectAll();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();
        if (name.Length == 0) return; // require a name — just ignore the click otherwise

        if (!double.TryParse(BaseNoiseTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var baseNoise) ||
            !double.TryParse(NoisePerPersonTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var noisePerPerson) ||
            !int.TryParse(CapacityTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var capacity))
        {
            MessageBox.Show("Hlučnost, hlučnost/osoba a kapacita musí být platná čísla.", "Neplatná hodnota",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        EnteredName = name;
        SelectedType = (LocationType)TypeComboBox.SelectedItem;
        SelectedTerrain = (TerrainType)TerrainComboBox.SelectedItem;
        BaseNoise = Math.Clamp(baseNoise, 0.0, 1.0);
        NoisePerPerson = Math.Clamp(noisePerPerson, 0.0, 1.0);
        Capacity = Math.Max(1, capacity);
        AllowsPrivacy = AllowsPrivacyCheckBox.IsChecked == true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
