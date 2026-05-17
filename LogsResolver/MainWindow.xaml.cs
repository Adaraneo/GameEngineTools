using LogsResolver.ViewModels;
using System.Windows;

namespace LogsResolver;

public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
