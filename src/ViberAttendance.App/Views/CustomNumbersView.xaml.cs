using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ViberAttendance.App.ViewModels;

namespace ViberAttendance.App.Views;

public partial class CustomNumbersView : UserControl
{
    public CustomNumbersView()
    {
        InitializeComponent();
    }

    private int _rememberQueued;

    private void CustomNumbers_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || DataContext is not SettingsViewModel settings)
        {
            return;
        }

        _rememberQueued++;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (--_rememberQueued > 0)
            {
                return;
            }

            _ = settings.RememberCustomNumbersAsync();
        }), DispatcherPriority.ContextIdle);
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
