using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ViberAttendance.App.ViewModels;

namespace ViberAttendance.App.Views;

public partial class CustomNumbersView : UserControl
{
    public CustomNumbersView()
    {
        InitializeComponent();
    }

    private void CustomNumbers_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || DataContext is not SettingsViewModel settings)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() => _ = settings.RememberCustomNumbersAsync()));
    }

    private void CustomNumbers_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control || DataContext is not SettingsViewModel settings)
        {
            return;
        }

        string? text;
        try
        {
            text = Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (Exception)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(text) || (!text.Contains('\t') && !text.Contains('\n') && !text.Contains('\r')))
        {
            return;
        }

        e.Handled = true;
        settings.PasteCustomNumbersCommand.Execute(null);
    }
}
