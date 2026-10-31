using System.Windows;
using UpdateLens.Win.ViewModels;

namespace UpdateLens.Win.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainViewModel();
        DataContext = vm;
        Loaded += async (_, _) => await vm.RefreshAsync();
    }
}
