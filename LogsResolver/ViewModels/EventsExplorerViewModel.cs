using System.Collections.ObjectModel;
using System.Windows.Input;
using LogsResolver.Commands;
using LogsResolver.Models;
using LogsResolver.Services;

namespace LogsResolver.ViewModels;

public sealed class EventsExplorerViewModel : ViewModelBase
{
    private readonly LogQueryEngine _queryEngine;
    private ResolvedLogEvent? _selectedEvent;
    private string? _freeText;
    private string? _level;
    private string? _personIdText;
    private string? _subsystem;
    private string? _category;
    private string? _eventIdText;
    private string? _correlationId;
    private string? _interactionId;
    private string? _decisionId;
    private string? _relatedPersonIdText;
    private string? _tickKey;
    private string? _fromText;
    private string? _toText;
    private string _maxResultsText = "2000";
    private bool _exceptionsOnly;
    private bool _globalOnly;
    private bool _scopedOnly;

    public EventsExplorerViewModel(LogQueryEngine queryEngine)
    {
        _queryEngine = queryEngine;
        ApplyFiltersCommand = new RelayCommand(ApplyFilters);
        ClearFiltersCommand = new RelayCommand(ClearFilters);
    }

    public event EventHandler<ResolvedLogEvent?>? SelectedEventChanged;

    public ObservableCollection<ResolvedLogEvent> Events { get; } = new();

    public ICommand ApplyFiltersCommand { get; }

    public ICommand ClearFiltersCommand { get; }

    public ResolvedLogEvent? SelectedEvent
    {
        get => _selectedEvent;
        set
        {
            if (SetProperty(ref _selectedEvent, value))
            {
                SelectedEventChanged?.Invoke(this, value);
            }
        }
    }

    public string? FreeText
    {
        get => _freeText;
        set => SetProperty(ref _freeText, value);
    }

    public string? Level
    {
        get => _level;
        set => SetProperty(ref _level, value);
    }

    public string? PersonIdText
    {
        get => _personIdText;
        set => SetProperty(ref _personIdText, value);
    }

    public string? Subsystem
    {
        get => _subsystem;
        set => SetProperty(ref _subsystem, value);
    }

    public string? Category
    {
        get => _category;
        set => SetProperty(ref _category, value);
    }

    public string? EventIdText
    {
        get => _eventIdText;
        set => SetProperty(ref _eventIdText, value);
    }

    public string? CorrelationId
    {
        get => _correlationId;
        set => SetProperty(ref _correlationId, value);
    }

    public string? InteractionId
    {
        get => _interactionId;
        set => SetProperty(ref _interactionId, value);
    }

    public string? DecisionId
    {
        get => _decisionId;
        set => SetProperty(ref _decisionId, value);
    }

    public string? RelatedPersonIdText
    {
        get => _relatedPersonIdText;
        set => SetProperty(ref _relatedPersonIdText, value);
    }

    public string? TickKey
    {
        get => _tickKey;
        set => SetProperty(ref _tickKey, value);
    }

    public string? FromText
    {
        get => _fromText;
        set => SetProperty(ref _fromText, value);
    }

    public string? ToText
    {
        get => _toText;
        set => SetProperty(ref _toText, value);
    }

    public bool ExceptionsOnly
    {
        get => _exceptionsOnly;
        set => SetProperty(ref _exceptionsOnly, value);
    }

    public bool GlobalOnly
    {
        get => _globalOnly;
        set => SetProperty(ref _globalOnly, value);
    }

    public bool ScopedOnly
    {
        get => _scopedOnly;
        set => SetProperty(ref _scopedOnly, value);
    }

    public string MaxResultsText
    {
        get => _maxResultsText;
        set => SetProperty(ref _maxResultsText, value);
    }

    public void Load(IReadOnlyList<ResolvedLogEvent> events)
    {
        _queryEngine.SetEvents(events);
        ApplyFilters();
    }

    public void ApplyQuickFilters(string? personId, string? subsystem)
    {
        PersonIdText = personId;
        Subsystem = subsystem;
        ApplyFilters();
    }

    public void ApplyFilters()
    {
        var query = BuildQuery();
        var selectedId = SelectedEvent?.EventInstanceId;
        Events.Clear();
        foreach (var ev in _queryEngine.Query(query))
        {
            Events.Add(ev);
        }

        SelectedEvent = selectedId.HasValue
            ? Events.FirstOrDefault(e => e.EventInstanceId == selectedId.Value) ?? Events.FirstOrDefault()
            : Events.FirstOrDefault();
    }

    public void ClearFilters()
    {
        FreeText = null;
        Level = null;
        PersonIdText = null;
        Subsystem = null;
        Category = null;
        EventIdText = null;
        CorrelationId = null;
        InteractionId = null;
        DecisionId = null;
        RelatedPersonIdText = null;
        TickKey = null;
        FromText = null;
        ToText = null;
        ExceptionsOnly = false;
        GlobalOnly = false;
        ScopedOnly = false;
        MaxResultsText = "2000";
        ApplyFilters();
    }

    private LogQuery BuildQuery()
    {
        _ = Guid.TryParse(PersonIdText, out var personId);
        _ = Guid.TryParse(RelatedPersonIdText, out var relatedPersonId);
        _ = int.TryParse(EventIdText, out var eventId);
        _ = DateTimeOffset.TryParse(FromText, out var from);
        _ = DateTimeOffset.TryParse(ToText, out var to);
        _ = int.TryParse(MaxResultsText, out var maxResults);

        return new LogQuery
        {
            From = DateTimeOffset.TryParse(FromText, out from) ? from : null,
            To = DateTimeOffset.TryParse(ToText, out to) ? to : null,
            FreeText = FreeText,
            Level = Level,
            PersonId = Guid.TryParse(PersonIdText, out personId) ? personId : null,
            Subsystem = Subsystem,
            Category = Category,
            EventId = int.TryParse(EventIdText, out eventId) ? eventId : null,
            CorrelationId = CorrelationId,
            InteractionId = InteractionId,
            DecisionId = DecisionId,
            RelatedPersonId = Guid.TryParse(RelatedPersonIdText, out relatedPersonId) ? relatedPersonId : null,
            TickKey = TickKey,
            ExceptionsOnly = ExceptionsOnly,
            GlobalOnly = GlobalOnly,
            ScopedOnly = ScopedOnly,
            MaxResults = int.TryParse(MaxResultsText, out maxResults)
                ? Math.Clamp(maxResults, 100, 100_000)
                : 2_000
        };
    }
}
