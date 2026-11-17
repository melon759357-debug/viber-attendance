using System.Windows;
using System.Windows.Controls;
using ViberAttendance.App.ViewModels;

namespace ViberAttendance.App;

public partial class MainWindow : Window
{
    private readonly HistoryViewModel _historyViewModel;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly AdvancedDiagnosticsViewModel _advancedDiagnosticsViewModel;

    public MainWindow(
        MainViewModel mainViewModel,
        HistoryViewModel historyViewModel,
        SettingsViewModel settingsViewModel,
        AdvancedDiagnosticsViewModel advancedDiagnosticsViewModel)
    {
        InitializeComponent();
        DataContext = mainViewModel;
        _historyViewModel = historyViewModel;
        _settingsViewModel = settingsViewModel;
        _advancedDiagnosticsViewModel = advancedDiagnosticsViewModel;
        HistoryTab.DataContext = historyViewModel;
        LiveSheetTab.DataContext = settingsViewModel;
        FinalSheetTab.DataContext = settingsViewModel;
        SettingsTab.DataContext = settingsViewModel;
        AdvancedTab.DataContext = advancedDiagnosticsViewModel;
        mainViewModel.SetOpenHistoryAction(OpenHistory);
        settingsViewModel.AdvancedDiagnosticsUnlocked += OnAdvancedDiagnosticsUnlocked;
        Closed += OnClosed;
    }

    private void OpenHistory() => HistoryNavButton.IsChecked = true;

    private async void OnAdvancedDiagnosticsUnlocked(object? sender, EventArgs e)
    {
        AdvancedNavButton.Visibility = Visibility.Visible;
        AdvancedTab.Visibility = Visibility.Visible;
        AdvancedNavButton.IsChecked = true;
        await _advancedDiagnosticsViewModel.InitializeAsync().ConfigureAwait(true);
    }

    private async void Navigation_Checked(object sender, RoutedEventArgs e)
    {
        if (MainTabs is null || sender is not RadioButton { Tag: string page })
        {
            return;
        }

        switch (page)
        {
            case "Current":
                MainTabs.SelectedItem = CurrentTab;
                break;
            case "History":
                MainTabs.SelectedItem = HistoryTab;
                await _historyViewModel.RefreshAsync().ConfigureAwait(true);
                break;
            case "LiveSheet":
                MainTabs.SelectedItem = LiveSheetTab;
                break;
            case "FinalSheet":
                MainTabs.SelectedItem = FinalSheetTab;
                break;
            case "Settings":
                MainTabs.SelectedItem = SettingsTab;
                break;
            case "Advanced":
                MainTabs.SelectedItem = AdvancedTab;
                break;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _settingsViewModel.AdvancedDiagnosticsUnlocked -= OnAdvancedDiagnosticsUnlocked;
        Closed -= OnClosed;
    }
}
