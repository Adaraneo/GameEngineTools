using LogsResolver.Models;
using System.Collections.ObjectModel;

namespace LogsResolver.ViewModels;

public sealed class SessionSummaryViewModel : ViewModelBase
{
    private string? _rootPath;
    private string? _selectedPerson;
    private string? _selectedSubsystem;
    private int _eventCount;
    private int _jsonFileCount;
    private int _textFileCount;
    private int _diagnosticCount;

    public string? RootPath
    {
        get => _rootPath;
        set => SetProperty(ref _rootPath, value);
    }

    public int EventCount
    {
        get => _eventCount;
        set => SetProperty(ref _eventCount, value);
    }

    public int JsonFileCount
    {
        get => _jsonFileCount;
        set => SetProperty(ref _jsonFileCount, value);
    }

    public int TextFileCount
    {
        get => _textFileCount;
        set => SetProperty(ref _textFileCount, value);
    }

    public int DiagnosticCount
    {
        get => _diagnosticCount;
        set => SetProperty(ref _diagnosticCount, value);
    }

    public ObservableCollection<string> Persons { get; } = new();

    public ObservableCollection<string> Subsystems { get; } = new();

    public ObservableCollection<string> RawFiles { get; } = new();

    public string? SelectedPerson
    {
        get => _selectedPerson;
        set => SetProperty(ref _selectedPerson, value);
    }

    public string? SelectedSubsystem
    {
        get => _selectedSubsystem;
        set => SetProperty(ref _selectedSubsystem, value);
    }

    public void Load(LogSessionLoadResult result)
    {
        RootPath = result.Session.RootPath;
        EventCount = result.Events.Count;
        JsonFileCount = result.JsonFileCount;
        TextFileCount = result.TextFileCount;
        DiagnosticCount = result.Diagnostics.Count;

        Reset(Persons, result.Events.Select(e => e.PersonId?.ToString()).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().OrderBy(v => v)!);
        Reset(Subsystems, result.Events.Select(e => e.Subsystem).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().OrderBy(v => v)!);
        Reset(RawFiles, result.Session.AllJsonLinesFiles.Select(f => f.FilePath).Concat(result.Session.AllTextLogFiles).Distinct().OrderBy(v => v));
    }

    public void Clear()
    {
        RootPath = null;
        EventCount = 0;
        JsonFileCount = 0;
        TextFileCount = 0;
        DiagnosticCount = 0;
        Persons.Clear();
        Subsystems.Clear();
        RawFiles.Clear();
        SelectedPerson = null;
        SelectedSubsystem = null;
    }

    private static void Reset(ObservableCollection<string> collection, IEnumerable<string> values)
    {
        collection.Clear();
        foreach (var value in values)
        {
            collection.Add(value);
        }
    }
}
