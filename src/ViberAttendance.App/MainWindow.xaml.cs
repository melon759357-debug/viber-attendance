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
        SheetSetupPanel.DataContext = settingsViewModel;
        HistoryTab.DataContext = historyViewModel;
        CustomNumbersTab.DataContext = settingsViewModel;
        LiveSheetTab.DataContext = settingsViewModel;
        FinalSheetTab.DataContext = settingsViewModel;
        SettingsTab.DataContext = settingsViewModel;
        AdvancedTab.DataContext = advancedDiagnosticsViewModel;
        mainViewModel.SetOpenHistoryAction(OpenHistory);
        settingsViewModel.AdvancedDiagnosticsUnlocked += OnAdvancedDiagnosticsUnlocked;
        settingsViewModel.CustomNumbersChanged += OnCustomNumbersChanged;
        Closed += OnClosed;
        WindowStartupLocation = WindowStartupLocation.Manual;
        FitToWorkArea();
    }

    private void FitToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        if (area.Width < MinWidth)
        {
            MinWidth = Math.Max(560, area.Width);
        }

        if (area.Height < MinHeight)
        {
            MinHeight = Math.Max(420, area.Height);
        }

        Width = Math.Min(Width, area.Width);
        Height = Math.Min(Height, area.Height);
        MaxWidth = area.Width;
        MaxHeight = area.Height;
        Left = area.Left + Math.Max(0, (area.Width - Width) / 2);
        Top = area.Top + Math.Max(0, (area.Height - Height) / 2);
    }

    private void OpenHistory() => HistoryNavButton.IsChecked = true;

    private void OnCustomNumbersChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel mainViewModel)
        {
            mainViewModel.RefreshDisplayedNumbers();
        }

        _historyViewModel.RefreshMatchedNumbers();
    }

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
                if (DataContext is MainViewModel mainViewModel)
                {
                    mainViewModel.RefreshDisplayedNumbers();
                }

                break;
            case "History":
                MainTabs.SelectedItem = HistoryTab;
                await _historyViewModel.RefreshAsync().ConfigureAwait(true);
                break;
            case "CustomNumbers":
                MainTabs.SelectedItem = CustomNumbersTab;
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
        _settingsViewModel.CustomNumbersChanged -= OnCustomNumbersChanged;
        Closed -= OnClosed;
    }
}
