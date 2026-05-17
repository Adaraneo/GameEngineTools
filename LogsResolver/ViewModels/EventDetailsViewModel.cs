using LogsResolver.Commands;
using LogsResolver.Models;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace LogsResolver.ViewModels;

public sealed class EventDetailsViewModel : ViewModelBase
{
    private ResolvedLogEvent? _selectedEvent;
    private ResolvedLogEventSource? _selectedSource;

    public EventDetailsViewModel(RawFileViewModel rawFile)
    {
        RawFile = rawFile;
        OpenRawSourceCommand = new AsyncRelayCommand(OpenSelectedSourceAsync, () => SelectedSource is not null);
        CopyEventIdCommand = new RelayCommand(CopyEventId, () => SelectedEvent is not null);
    }

    public RawFileViewModel RawFile { get; }

    public ICommand OpenRawSourceCommand { get; }

    public ICommand CopyEventIdCommand { get; }

    public ResolvedLogEvent? SelectedEvent
    {
        get => _selectedEvent;
        set
        {
            if (!SetProperty(ref _selectedEvent, value))
            {
                return;
            }

            Sources.Clear();
            if (value is not null)
            {
                foreach (var source in value.Sources)
                {
                    Sources.Add(source);
                }
            }

            SelectedSource = Sources.FirstOrDefault();
            OnPropertyChanged(nameof(HasEvent));
            RaiseCommands();
        }
    }

    public bool HasEvent => SelectedEvent is not null;

    public ObservableCollection<ResolvedLogEventSource> Sources { get; } = new();

    public ResolvedLogEventSource? SelectedSource
    {
        get => _selectedSource;
        set
        {
            if (SetProperty(ref _selectedSource, value))
            {
                RaiseCommands();
            }
        }
    }

    private async Task OpenSelectedSourceAsync()
    {
        if (SelectedSource is not null)
        {
            await RawFile.LoadAsync(SelectedSource.FilePath).ConfigureAwait(true);
        }
    }

    private void CopyEventId()
    {
        if (SelectedEvent is not null)
        {
            System.Windows.Clipboard.SetText(SelectedEvent.EventInstanceId.ToString());
        }
    }

    private void RaiseCommands()
    {
        if (OpenRawSourceCommand is AsyncRelayCommand asyncCommand)
        {
            asyncCommand.RaiseCanExecuteChanged();
        }

        if (CopyEventIdCommand is RelayCommand relayCommand)
        {
            relayCommand.RaiseCanExecuteChanged();
        }
    }
}
