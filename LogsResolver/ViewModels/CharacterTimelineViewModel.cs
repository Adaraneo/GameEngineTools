using System.Collections.ObjectModel;
using LogsResolver.Models;

namespace LogsResolver.ViewModels;

public sealed class CharacterTimelineViewModel : ViewModelBase
{
    private const int MaxTimelineEntries = 5_000;

    private IReadOnlyList<ResolvedLogEvent> _events = Array.Empty<ResolvedLogEvent>();
    private NpcCharacterDescriptor? _selectedCharacter;
    private string? _selectedPersonIdText;
    private string _summaryText = "Load logs and optionally NPC JSON files to inspect character progression.";
    private int _characterCount;
    private int _matchingEventCount;
    private bool _isUpdatingSelection;

    public ObservableCollection<NpcCharacterDescriptor> Characters { get; } = new();

    public ObservableCollection<CharacterTimelineEntry> Entries { get; } = new();

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
        private set => SetProperty(ref _matchingEventCount, value);
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

        var matchingCount = 0;
        foreach (var logEvent in _events)
        {
            if (logEvent.PersonId != personId && logEvent.RelatedPersonId != personId)
            {
                continue;
            }

            matchingCount++;
            if (Entries.Count < MaxTimelineEntries)
            {
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
        }

        MatchingEventCount = matchingCount;
        var name = SelectedCharacter?.DisplayName ?? personId.ToString();
        SummaryText = matchingCount > MaxTimelineEntries
            ? $"{name}: showing first {MaxTimelineEntries} of {matchingCount} related event(s). Narrow filters in the main grid for a smaller working set."
            : $"{name}: {matchingCount} related event(s) in timeline.";
    }
}
