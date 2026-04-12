using System.Windows;
using LogsResolver.Services;
using LogsResolver.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace LogsResolver;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        var window = _serviceProvider.GetRequiredService<MainWindow>();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<FolderPickerService>();
        services.AddSingleton<JsonLogSessionDiscoveryService>();
        services.AddSingleton<JsonLogFileReader>();
        services.AddSingleton<LogIntegrityAnalyzer>();
        services.AddSingleton<LogSessionLoader>();
        services.AddSingleton<LogQueryEngine>();
        services.AddSingleton<RawFileService>();
        services.AddSingleton<NpcCharacterJsonReader>();

        services.AddSingleton<SessionSummaryViewModel>();
        services.AddSingleton<EventsExplorerViewModel>();
        services.AddSingleton<EventDetailsViewModel>();
        services.AddSingleton<DiagnosticsViewModel>();
        services.AddSingleton<RawFileViewModel>();
        services.AddSingleton<CharacterTimelineViewModel>();
        services.AddSingleton<ShellViewModel>();

        services.AddSingleton<MainWindow>();
    }
}
