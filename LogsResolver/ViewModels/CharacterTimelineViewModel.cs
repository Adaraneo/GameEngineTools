using LogsResolver.Commands;
using LogsResolver.Models;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace LogsResolver.ViewModels;

public sealed class CharacterTimelineViewModel : ViewModelBase
{
    private const int DefaultPageSize = 500;
    private const int MaxPageSize = 5_000;

    private IReadOnlyList<ResolvedLogEvent> _events = Array.Empty<ResolvedLogEvent>();
    private NpcCharacterDescriptor? _selectedCharacter;
    private string? _selectedPersonIdText;
    private string _summaryText = "Load logs and optionally NPC JSON files to inspect character progression.";
    private string _pageSizeText = DefaultPageSize.ToString();
    private string _pageNumberText = "Page 0 / 0";
    private int _characterCount;
    private int _matchingEventCount;
    private int _pageIndex;
    private int _totalPages;
    private bool _isUpdatingSelection;

    public CharacterTimelineViewModel()
    {
        FirstPageCommand = new RelayCommand(() => MoveToPage(0), () => PageIndex > 0);
        PreviousPageCommand = new RelayCommand(() => MoveToPage(PageIndex - 1), () => PageIndex > 0);
        NextPageCommand = new RelayCommand(() => MoveToPage(PageIndex + 1), () => PageIndex + 1 < TotalPages);
        LastPageCommand = new RelayCommand(() => MoveToPage(TotalPages - 1), () => TotalPages > 0 && PageIndex + 1 < TotalPages);
    }

    public ObservableCollection<NpcCharacterDescriptor> Characters { get; } = new();

    public ObservableCollection<CharacterTimelineEntry> Entries { get; } = new();

    public ICommand FirstPageCommand { get; }

    public ICommand PreviousPageCommand { get; }

    public ICommand NextPageCommand { get; }

    public ICommand LastPageCommand { get; }

    public NpcCharacterDescriptor? SelectedCharacter
    {
        get => _selectedCharacter;
        set
        {
            if (SetProperty(ref _selectedCharacter, value))
            {
                if (!_isUpdatingSelection)
                {
                    _isUpdatingSelection = true;
                    try
                    {
                        _selectedPersonIdText = value?.PersonId.ToString();
                        OnPropertyChanged(nameof(SelectedPersonIdText));
                    }
                    finally
                    {
                        _isUpdatingSelection = false;
                    }
                }

                PageIndex = 0;
                Rebuild();
            }
        }
    }

    public string? SelectedPersonIdText
    {
        get => _selectedPersonIdText;
        set
        {
            if (SetProperty(ref _selectedPersonIdText, value))
            {
                if (!_isUpdatingSelection)
                {
                    SyncSelectedCharacterFromText();
                }

                PageIndex = 0;
                Rebuild();
            }
        }
    }

    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    public int CharacterCount
    {
        get => _characterCount;
        private set => SetProperty(ref _characterCount, value);
    }

    public int MatchingEventCount
    {
        get => _matchingEventCount;
        private set
        {
            if (SetProperty(ref _matchingEventCount, value))
            {
                TotalPages = CalculateTotalPages(value, PageSize);
            }
        }
    }

    public int PageIndex
    {
        get => _pageIndex;
        private set
        {
            var normalized = Math.Max(0, value);
            if (SetProperty(ref _pageIndex, normalized))
            {
                UpdatePageText();
                RaisePageCommandStates();
            }
        }
    }

    public int TotalPages
    {
        get => _totalPages;
        private set
        {
            if (SetProperty(ref _totalPages, value))
            {
                if (PageIndex >= value && value > 0)
                {
                    PageIndex = value - 1;
                }

                UpdatePageText();
                RaisePageCommandStates();
            }
        }
    }

    public string PageSizeText
    {
        get => _pageSizeText;
        set
        {
            if (SetProperty(ref _pageSizeText, value))
            {
                PageIndex = 0;
                Rebuild();
            }
        }
    }

    public string PageNumberText
    {
        get => _pageNumberText;
        private set => SetProperty(ref _pageNumberText, value);
    }

    public void LoadCharacters(IEnumerable<NpcCharacterDescriptor> characters)
    {
        Characters.Clear();
        foreach (var character in characters)
        {
            Characters.Add(character);
        }

        CharacterCount = Characters.Count;
        SyncSelectedCharacterFromText();
        Rebuild();
    }

    public void LoadEvents(IReadOnlyList<ResolvedLogEvent> events)
    {
        _events = events;
        Rebuild();
    }

    public void FocusEvent(ResolvedLogEvent? logEvent)
    {
        if (logEvent?.PersonId is not Guid personId)
        {
            return;
        }

        SelectPerson(personId);
    }

    public void SelectPerson(Guid personId)
    {
        _isUpdatingSelection = true;
        try
        {
            _selectedPersonIdText = personId.ToString();
            OnPropertyChanged(nameof(SelectedPersonIdText));
            _selectedCharacter = Characters.FirstOrDefault(c => c.PersonId == personId);
            OnPropertyChanged(nameof(SelectedCharacter));
        }
        finally
        {
            _isUpdatingSelection = false;
        }

        PageIndex = 0;
        Rebuild();
    }

    private void SyncSelectedCharacterFromText()
    {
        if (!Guid.TryParse(SelectedPersonIdText, out var personId))
        {
            if (SelectedCharacter is not null)
            {
                _isUpdatingSelection = true;
                try
                {
                    _selectedCharacter = null;
                    OnPropertyChanged(nameof(SelectedCharacter));
                }
                finally
                {
                    _isUpdatingSelection = false;
                }
            }

            return;
        }

        var character = Characters.FirstOrDefault(c => c.PersonId == personId);
        if (!Equals(SelectedCharacter, character))
        {
            _isUpdatingSelection = true;
            try
            {
                _selectedCharacter = character;
                OnPropertyChanged(nameof(SelectedCharacter));
            }
            finally
            {
                _isUpdatingSelection = false;
            }
        }
    }

    private void Rebuild()
    {
        Entries.Clear();

        if (!Guid.TryParse(SelectedPersonIdText, out var personId))
        {
            MatchingEventCount = 0;
            SummaryText = CharacterCount == 0
                ? "Load NPC JSON files or select a log event with PersonId."
                : $"Loaded {CharacterCount} NPC character(s). Select one to see progression.";
            return;
        }

        var pageSize = PageSize;
        var pageStart = PageIndex * pageSize;
        var matchingCount = 0;
        foreach (var logEvent in _events)
        {
            if (logEvent.PersonId != personId && logEvent.RelatedPersonId != personId)
            {
                continue;
            }

            matchingCount++;
            if (matchingCount <= pageStart || Entries.Count >= pageSize)
            {
                continue;
            }

            Entries.Add(new CharacterTimelineEntry
            {
                EventInstanceId = logEvent.EventInstanceId,
                RealTimestamp = logEvent.RealTimestamp,
                WorldTimeText = logEvent.WorldTimeText,
                Level = logEvent.Level,
                Category = logEvent.Category,
                EventId = logEvent.EventId,
                Message = logEvent.Message,
                PersonId = logEvent.PersonId,
                RelatedPersonId = logEvent.RelatedPersonId,
                Subsystem = logEvent.Subsystem,
                CorrelationId = logEvent.CorrelationId,
                InteractionId = logEvent.InteractionId,
                DecisionId = logEvent.DecisionId,
                TickKey = logEvent.TickKey,
                Involvement = logEvent.PersonId == personId ? "Primary" : "Related"
            });
        }

        MatchingEventCount = matchingCount;
        if (Entries.Count == 0 && matchingCount > 0 && pageStart >= matchingCount && PageIndex > 0)
        {
            Rebuild();
            return;
        }

        var name = SelectedCharacter?.DisplayName ?? personId.ToString();
        SummaryText = matchingCount > pageSize
            ? $"{name}: showing page {PageIndex + 1} of {TotalPages}, {matchingCount} related event(s)."
            : $"{name}: {matchingCount} related event(s) in timeline.";
    }

    private int PageSize
        => int.TryParse(PageSizeText, out var pageSize)
            ? Math.Clamp(pageSize, 1, MaxPageSize)
            : DefaultPageSize;

    private static int CalculateTotalPages(int totalCount, int pageSize)
        => totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)Math.Max(1, pageSize));

    private void MoveToPage(int pageIndex)
    {
        if (TotalPages <= 0)
        {
            PageIndex = 0;
            return;
        }

        PageIndex = Math.Clamp(pageIndex, 0, TotalPages - 1);
        Rebuild();
    }

    private void UpdatePageText()
        => PageNumberText = TotalPages == 0 ? "Page 0 / 0" : $"Page {PageIndex + 1} / {TotalPages}";

    private void RaisePageCommandStates()
    {
        if (FirstPageCommand is RelayCommand first) first.RaiseCanExecuteChanged();
        if (PreviousPageCommand is RelayCommand previous) previous.RaiseCanExecuteChanged();
        if (NextPageCommand is RelayCommand next) next.RaiseCanExecuteChanged();
        if (LastPageCommand is RelayCommand last) last.RaiseCanExecuteChanged();
    }
}
