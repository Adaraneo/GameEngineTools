using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using TerrainEditor.Diagnostics;

namespace TerrainEditor;

/// <summary>
/// Non-modal live view over <see cref="PerfLog"/> — every recorded step of the continuous-tile-
/// panning pipeline (coverage checks, background stitching, grid/overlay rendering, cache hits)
/// plus periodic memory readings, so it's possible to actually SEE whether a given optimization
/// helped instead of guessing from how panning feels. Safe to leave open across the whole session;
/// closing it just unsubscribes from <see cref="PerfLog.EntryAdded"/>.
/// </summary>
public partial class PerfLogWindow : Window
{
    private readonly DispatcherTimer _memoryTimer;

    public PerfLogWindow()
    {
        InitializeComponent();

        foreach (var entry in PerfLog.Snapshot())
            LogListView.Items.Add(ToRow(entry));
        ScrollToEndIfEnabled();

        PerfLog.EntryAdded += OnEntryAdded;
        Closed += (_, _) => PerfLog.EntryAdded -= OnEntryAdded;

        _memoryTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _memoryTimer.Tick += (_, _) => UpdateMemoryLabel();
        _memoryTimer.Start();
        Closed += (_, _) => _memoryTimer.Stop();

        UpdateMemoryLabel();
    }

    private void OnEntryAdded(PerfLog.Entry entry)
    {
        // PerfLog.Log can be called from a background thread (the stitch pipeline runs there
        // deliberately — see MainWindow.EnsureViewportCoverage) — marshal to the UI thread before
        // touching the ListView.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            LogListView.Items.Add(ToRow(entry));
            const int MaxRows = 1000;
            while (LogListView.Items.Count > MaxRows)
                LogListView.Items.RemoveAt(0);
            ScrollToEndIfEnabled();
        }));
    }

    private void ScrollToEndIfEnabled()
    {
        if (AutoScrollCheckBox.IsChecked == true && LogListView.Items.Count > 0)
            LogListView.ScrollIntoView(LogListView.Items[^1]);
    }

    private void UpdateMemoryLabel()
    {
        var managedMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
        var workingSetMb = Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0);
        MemoryLabel.Text = $"Spravovaná halda: {managedMb:0.0} MB · Working set: {workingSetMb:0.0} MB";
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        PerfLog.Clear();
        LogListView.Items.Clear();
    }

    private void ForceGcButton_Click(object sender, RoutedEventArgs e)
    {
        var before = GC.GetTotalMemory(false);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var after = GC.GetTotalMemory(true);
        PerfLog.Log("GC", $"Vynucený GC.Collect: {before / 1024.0 / 1024.0:0.0} MB -> {after / 1024.0 / 1024.0:0.0} MB");
        UpdateMemoryLabel();
    }

    private static Row ToRow(PerfLog.Entry entry) => new(
        entry.Timestamp.ToString("HH:mm:ss.fff"),
        entry.Category,
        entry.Message,
        entry.DurationMs is { } ms ? $"{ms:0.0} ms" : "",
        (entry.ManagedMemoryBytes / (1024.0 * 1024.0)).ToString("0.0"),
        (entry.WorkingSetBytes / (1024.0 * 1024.0)).ToString("0.0"));

    private sealed record Row(string TimeText, string Category, string Message, string DurationText, string ManagedMbText, string WorkingSetMbText);
}
