namespace LogsResolver.ViewModels;

public sealed class GridColumnVisibilityViewModel : ViewModelBase
{
    private bool _eventsSeq = true;
    private bool _eventsRealTime = true;
    private bool _eventsWorld = true;
    private bool _eventsLevel = true;
    private bool _eventsPerson = true;
    private bool _eventsSubsystem = true;
    private bool _eventsEventId = true;
    private bool _eventsCategory = true;
    private bool _eventsMessage = true;
    private bool _eventsSources = true;
    private bool _timelineWorld = true;
    private bool _timelineRealTime = true;
    private bool _timelineSeq = true;
    private bool _timelineRole = true;
    private bool _timelineLevel = true;
    private bool _timelineSubsystem = true;
    private bool _timelineCategory = true;
    private bool _timelineTick = true;
    private bool _timelineRelated = true;
    private bool _timelineMessage = true;
    private bool _sourcesKind = true;
    private bool _sourcesPerson = true;
    private bool _sourcesSubsystem = true;
    private bool _sourcesLine = true;
    private bool _sourcesFile = true;
    private bool _diagnosticsKind = true;
    private bool _diagnosticsTitle = true;
    private bool _diagnosticsLine = true;

    public bool EventsSeq { get => _eventsSeq; set => SetProperty(ref _eventsSeq, value); }

    public bool EventsRealTime { get => _eventsRealTime; set => SetProperty(ref _eventsRealTime, value); }

    public bool EventsWorld { get => _eventsWorld; set => SetProperty(ref _eventsWorld, value); }

    public bool EventsLevel { get => _eventsLevel; set => SetProperty(ref _eventsLevel, value); }

    public bool EventsPerson { get => _eventsPerson; set => SetProperty(ref _eventsPerson, value); }

    public bool EventsSubsystem { get => _eventsSubsystem; set => SetProperty(ref _eventsSubsystem, value); }

    public bool EventsEventId { get => _eventsEventId; set => SetProperty(ref _eventsEventId, value); }

    public bool EventsCategory { get => _eventsCategory; set => SetProperty(ref _eventsCategory, value); }

    public bool EventsMessage { get => _eventsMessage; set => SetProperty(ref _eventsMessage, value); }

    public bool EventsSources { get => _eventsSources; set => SetProperty(ref _eventsSources, value); }

    public bool TimelineWorld { get => _timelineWorld; set => SetProperty(ref _timelineWorld, value); }

    public bool TimelineRealTime { get => _timelineRealTime; set => SetProperty(ref _timelineRealTime, value); }

    public bool TimelineSeq { get => _timelineSeq; set => SetProperty(ref _timelineSeq, value); }

    public bool TimelineRole { get => _timelineRole; set => SetProperty(ref _timelineRole, value); }

    public bool TimelineLevel { get => _timelineLevel; set => SetProperty(ref _timelineLevel, value); }

    public bool TimelineSubsystem { get => _timelineSubsystem; set => SetProperty(ref _timelineSubsystem, value); }

    public bool TimelineCategory { get => _timelineCategory; set => SetProperty(ref _timelineCategory, value); }

    public bool TimelineTick { get => _timelineTick; set => SetProperty(ref _timelineTick, value); }

    public bool TimelineRelated { get => _timelineRelated; set => SetProperty(ref _timelineRelated, value); }

    public bool TimelineMessage { get => _timelineMessage; set => SetProperty(ref _timelineMessage, value); }

    public bool SourcesKind { get => _sourcesKind; set => SetProperty(ref _sourcesKind, value); }

    public bool SourcesPerson { get => _sourcesPerson; set => SetProperty(ref _sourcesPerson, value); }

    public bool SourcesSubsystem { get => _sourcesSubsystem; set => SetProperty(ref _sourcesSubsystem, value); }

    public bool SourcesLine { get => _sourcesLine; set => SetProperty(ref _sourcesLine, value); }

    public bool SourcesFile { get => _sourcesFile; set => SetProperty(ref _sourcesFile, value); }

    public bool DiagnosticsKind { get => _diagnosticsKind; set => SetProperty(ref _diagnosticsKind, value); }

    public bool DiagnosticsTitle { get => _diagnosticsTitle; set => SetProperty(ref _diagnosticsTitle, value); }

    public bool DiagnosticsLine { get => _diagnosticsLine; set => SetProperty(ref _diagnosticsLine, value); }
}
