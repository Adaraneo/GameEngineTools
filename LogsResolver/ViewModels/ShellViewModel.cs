using System.Windows.Input;
using LogsResolver.Commands;
using LogsResolver.Models;
using LogsResolver.Services;

namespace LogsResolver.ViewModels;

public sealed class ShellViewModel : ViewModelBase
{
    private readonly FolderPickerService _folderPicker;
    private readonly LogSessionLoader _loader;
    private string? _currentFolder;
    private string _statusText = "Open a GameEngineTools Characters log folder to begin.";
    private bool _isLoading;
    private string? _selectedRawFile;

    public ShellViewModel(
        FolderPickerService folderPicker,
        LogSessionLoader loader,
        SessionSummaryViewModel summary,
        EventsExplorerViewModel eventsExplorer,
        EventDetailsViewModel eventDetails,
        DiagnosticsViewModel diagnostics,
        RawFileViewModel rawFile)
    {
        _folderPicker = folderPicker;
        _loader = loader;
        Summary = summary;
        EventsExplorer = eventsExplorer;
        EventDetails = eventDetails;
        Diagnostics = diagnostics;
        RawFile = rawFile;

        OpenFolderCommand = new AsyncRelayCommand(OpenFolderAsync, () => !IsLoading);
        ReloadSessionCommand = new AsyncRelayCommand(ReloadSessionAsync, () => !IsLoading && !string.IsNullOrWhiteSpace(CurrentFolder));
        ApplyQuickFiltersCommand = new RelayCommand(ApplyQuickFilters);
        OpenSelectedRawFileCommand = new AsyncRelayCommand(OpenSelectedRawFileAsync, () => !string.IsNullOrWhiteSpace(SelectedRawFile));

        EventsExplorer.SelectedEventChanged += (_, ev) => EventDetails.SelectedEvent = ev;
    }

    public SessionSummaryViewModel Summary { get; }

    public EventsExplorerViewModel EventsExplorer { get; }

    public EventDetailsViewModel EventDetails { get; }

    public DiagnosticsViewModel Diagnostics { get; }

    public RawFileViewModel RawFile { get; }

    public ICommand OpenFolderCommand { get; }

    public ICommand ReloadSessionCommand { get; }

    public ICommand ApplyQuickFiltersCommand { get; }

    public ICommand OpenSelectedRawFileCommand { get; }

    public string? CurrentFolder
    {
        get => _currentFolder;
        private set => SetProperty(ref _currentFolder, value);
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
                RaiseCommandStates();
            }
        }
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
        var folder = _folderPicker.PickFolder();
        if (folder is null)
        {
            return;
        }

        CurrentFolder = folder;
        await LoadSessionAsync(folder).ConfigureAwait(true);
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
        try
        {
            var result = await _loader.LoadAsync(folder).ConfigureAwait(true);
            ApplyResult(result);
            StatusText = $"Loaded {result.Events.Count} logical events from {result.JsonFileCount} JSONL file(s).";
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
        if (ReloadSessionCommand is AsyncRelayCommand reload) reload.RaiseCanExecuteChanged();
        if (OpenSelectedRawFileCommand is AsyncRelayCommand raw) raw.RaiseCanExecuteChanged();
    }
}
