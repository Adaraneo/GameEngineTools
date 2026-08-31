using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using TerrainEditor.Services;
using TerrainEditor.ViewModels;

namespace TerrainEditor;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Without this, any unhandled exception (e.g. from a button click handler) silently
        // kills the whole app with no diagnostic — show it instead so a crash is reportable.
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.ToString(), "Neošetřená výjimka",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        var window = _serviceProvider.GetRequiredService<MainWindow>();
        window.Show();
    }

    private static void ConfigureServices(ServiceCollection services)
    {
        services.AddSingleton<WorldDatabaseService>();
        services.AddSingleton<ContourGenerator>();

        services.AddSingleton<ShellViewModel>();

        services.AddSingleton<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.GetService<WorldDatabaseService>()?.Dispose();
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
