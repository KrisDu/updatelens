using System.Windows;
using UpdateLens.Win.Services;
using UpdateLens.Win.ViewModels;

namespace UpdateLens.Win.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainViewModel();
        DataContext = vm;
        RestorePlacement();
        Closing += (_, _) => SavePlacement();
        Loaded += async (_, _) => await vm.RefreshAsync();
    }

    private void RestorePlacement()
    {
        try
        {
            var s = AppSettings.Load();
            if (s.WinWidth >= 500 && s.WinHeight >= 350)
            {
                Width = s.WinWidth;
                Height = s.WinHeight;
                if (!double.IsNaN(s.WinLeft) && !double.IsNaN(s.WinTop))
                {
                    Left = s.WinLeft;
                    Top = s.WinTop;
                }
            }
        }
        catch { }
    }

    private void SavePlacement()
    {
        try
        {
            var s = AppSettings.Load();
            s.WinWidth = Width;
            s.WinHeight = Height;
            s.WinLeft = Left;
            s.WinTop = Top;
            s.Save();
        }
        catch { }
    }
}
