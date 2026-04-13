using System.Collections.ObjectModel;
using LogsResolver.Models;

namespace LogsResolver.ViewModels;

public sealed class DiagnosticsViewModel : ViewModelBase
{
    private LogDiagnosticIssue? _selectedIssue;

    public ObservableCollection<LogDiagnosticIssue> Issues { get; } = new();

    public LogDiagnosticIssue? SelectedIssue
    {
        get => _selectedIssue;
        set => SetProperty(ref _selectedIssue, value);
    }

    public void Load(IEnumerable<LogDiagnosticIssue> issues)
    {
        Issues.Clear();
        foreach (var issue in issues)
        {
            Issues.Add(issue);
        }

        SelectedIssue = Issues.FirstOrDefault();
    }

    public void Clear()
    {
        Issues.Clear();
        SelectedIssue = null;
    }
}
