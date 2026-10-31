using System.Windows;
using System.Windows.Controls;
using UpdateLens.Win.Services;

namespace UpdateLens.Win.Views;

public partial class SettingsWindow : Window
{
    public bool NeedsRefresh { get; private set; }

    public SettingsWindow()
    {
        InitializeComponent();
        ThemeBox.ItemsSource = new[] { "System", "Light", "Dark" };
        var current = ThemeService.CurrentMode();
        ThemeBox.SelectedItem = current switch
        {
            "light" => "Light",
            "dark" => "Dark",
            _ => "System",
        };
        ThemeBox.SelectionChanged += (_, _) =>
        {
            if (ThemeBox.SelectedItem is string s)
                ThemeService.SetMode(s.ToLowerInvariant());
        };
        LaunchBox.IsChecked = SettingsService.GetLaunchAtLogin();
        DailyBox.IsChecked = SettingsService.GetDailyCheck();
        LaunchBox.Checked += (_, _) => SettingsService.SetLaunchAtLogin(true);
        LaunchBox.Unchecked += (_, _) => SettingsService.SetLaunchAtLogin(false);
        DailyBox.Checked += (_, _) => SettingsService.SetDailyCheck(true);
        DailyBox.Unchecked += (_, _) => SettingsService.SetDailyCheck(false);
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        new IgnoreStore().Reset();
        NeedsRefresh = true;
        System.Windows.MessageBox.Show("Cleared all skips and hidden apps.", "UpdateLens", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = NeedsRefresh;
        Close();
    }
}
