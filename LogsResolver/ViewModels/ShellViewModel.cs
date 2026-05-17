using LogsResolver.Commands;
using LogsResolver.Models;
using LogsResolver.Services;
using System.Windows.Input;

namespace LogsResolver.ViewModels;

public sealed class ShellViewModel : ViewModelBase
{
    private readonly FolderPickerService _folderPicker;
    private readonly LogSessionLoader _loader;
    private readonly NpcCharacterJsonReader _npcReader;
    private string? _currentFolder;
    private string? _currentNpcFolder;
    private string _statusText = "Open a GameEngineTools Characters log folder to begin.";
    private string? _loadingProgressText;
    private bool _isLoading;
    private string? _selectedRawFile;

    public ShellViewModel(
        FolderPickerService folderPicker,
        LogSessionLoader loader,
        NpcCharacterJsonReader npcReader,
        SessionSummaryViewModel summary,
        EventsExplorerViewModel eventsExplorer,
        EventDetailsViewModel eventDetails,
        DiagnosticsViewModel diagnostics,
        RawFileViewModel rawFile,
        CharacterTimelineViewModel characterTimeline)
    {
        _folderPicker = folderPicker;
        _loader = loader;
        _npcReader = npcReader;
        Summary = summary;
        EventsExplorer = eventsExplorer;
        EventDetails = eventDetails;
        Diagnostics = diagnostics;
        RawFile = rawFile;
        CharacterTimeline = characterTimeline;
        Columns = new GridColumnVisibilityViewModel();

        OpenFolderCommand = new AsyncRelayCommand(OpenFolderAsync, () => !IsLoading);
        OpenNpcFolderCommand = new AsyncRelayCommand(OpenNpcFolderAsync, () => !IsLoading);
        ReloadSessionCommand = new AsyncRelayCommand(ReloadSessionAsync, () => !IsLoading && !string.IsNullOrWhiteSpace(CurrentFolder));
        ApplyQuickFiltersCommand = new RelayCommand(ApplyQuickFilters);
        OpenSelectedRawFileCommand = new AsyncRelayCommand(OpenSelectedRawFileAsync, () => !string.IsNullOrWhiteSpace(SelectedRawFile));

        EventsExplorer.SelectedEventChanged += (_, ev) =>
        {
            EventDetails.SelectedEvent = ev;
            CharacterTimeline.FocusEvent(ev);
        };
    }

    public SessionSummaryViewModel Summary { get; }

    public EventsExplorerViewModel EventsExplorer { get; }

    public EventDetailsViewModel EventDetails { get; }

    public DiagnosticsViewModel Diagnostics { get; }

    public RawFileViewModel RawFile { get; }

    public CharacterTimelineViewModel CharacterTimeline { get; }

    public GridColumnVisibilityViewModel Columns { get; }

    public ICommand OpenFolderCommand { get; }

    public ICommand OpenNpcFolderCommand { get; }

    public ICommand ReloadSessionCommand { get; }

    public ICommand ApplyQuickFiltersCommand { get; }

    public ICommand OpenSelectedRawFileCommand { get; }

    public string? CurrentFolder
    {
        get => _currentFolder;
        private set => SetProperty(ref _currentFolder, value);
    }

    public string? CurrentNpcFolder
    {
        get => _currentNpcFolder;
        private set => SetProperty(ref _currentNpcFolder, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(IsInterfaceEnabled));
                RaiseCommandStates();
            }
        }
    }

    public bool IsInterfaceEnabled => !IsLoading;

    public string? LoadingProgressText
    {
        get => _loadingProgressText;
        private set => SetProperty(ref _loadingProgressText, value);
    }

    public string? SelectedRawFile
    {
        get => _selectedRawFile;
        set
        {
            if (SetProperty(ref _selectedRawFile, value))
            {
                RaiseCommandStates();
            }
        }
    }

    private async Task OpenFolderAsync()
    {
        var folder = _folderPicker.PickFolder("Select Characters logs folder, logs folder, or repository root");
        if (folder is null)
        {
            return;
        }

        CurrentFolder = folder;
        await LoadSessionAsync(folder).ConfigureAwait(true);
    }

    private async Task OpenNpcFolderAsync()
    {
        var folder = _folderPicker.PickFolder("Select NPC JSON export folder");
        if (folder is null)
        {
            return;
        }

        IsLoading = true;
        CurrentNpcFolder = folder;
        StatusText = "Loading NPC JSON files...";
        LoadingProgressText = "Loading NPC JSON files...";
        try
        {
            var characters = await _npcReader.LoadAsync(folder).ConfigureAwait(true);
            CharacterTimeline.LoadCharacters(characters);
            StatusText = $"Loaded {characters.Count} NPC character JSON file(s).";
            LoadingProgressText = StatusText;
        }
        catch (Exception ex)
        {
            StatusText = $"NPC load failed: {ex.Message}";
            LoadingProgressText = StatusText;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ReloadSessionAsync()
    {
        if (!string.IsNullOrWhiteSpace(CurrentFolder))
        {
            await LoadSessionAsync(CurrentFolder).ConfigureAwait(true);
        }
    }

    private async Task LoadSessionAsync(string folder)
    {
        IsLoading = true;
        StatusText = "Loading session...";
        LoadingProgressText = "Starting load...";
        try
        {
            var progress = new Progress<LogLoadProgress>(p =>
            {
                LoadingProgressText = p.DisplayText;
                StatusText = p.DisplayText;
            });
            var result = await _loader.LoadAsync(folder, progress).ConfigureAwait(true);
            ApplyResult(result);
            StatusText = $"Loaded {result.Events.Count} logical events from {result.JsonFileCount} JSONL file(s).";
            LoadingProgressText = StatusText;
        }
        catch (Exception ex)
        {
            StatusText = $"Load failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyResult(LogSessionLoadResult result)
    {
        Summary.Load(result);
        EventsExplorer.Load(result.Events);
        CharacterTimeline.LoadEvents(result.Events);
        Diagnostics.Load(result.Diagnostics);
        SelectedRawFile = Summary.RawFiles.FirstOrDefault();
    }

    private void ApplyQuickFilters()
        => EventsExplorer.ApplyQuickFilters(Summary.SelectedPerson, Summary.SelectedSubsystem);

    private async Task OpenSelectedRawFileAsync()
    {
        if (!string.IsNullOrWhiteSpace(SelectedRawFile))
        {
            await RawFile.LoadAsync(SelectedRawFile).ConfigureAwait(true);
        }
    }

    private void RaiseCommandStates()
    {
        if (OpenFolderCommand is AsyncRelayCommand open) open.RaiseCanExecuteChanged();
        if (OpenNpcFolderCommand is AsyncRelayCommand npc) npc.RaiseCanExecuteChanged();
        if (ReloadSessionCommand is AsyncRelayCommand reload) reload.RaiseCanExecuteChanged();
        if (OpenSelectedRawFileCommand is AsyncRelayCommand raw) raw.RaiseCanExecuteChanged();
    }
}
