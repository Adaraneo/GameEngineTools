using System.Windows;
using LogsResolver.ViewModels;

namespace LogsResolver;

public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
