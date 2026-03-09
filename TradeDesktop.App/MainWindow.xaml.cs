using System.Windows;
using TradeDesktop.App.ViewModels;

namespace TradeDesktop.App;

public partial class MainWindow : Window
{
    public MainWindow(DashboardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}