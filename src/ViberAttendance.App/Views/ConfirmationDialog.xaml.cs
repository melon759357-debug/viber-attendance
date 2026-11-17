using System.Windows;
using System.Windows.Input;

namespace ViberAttendance.App.Views;

public partial class ConfirmationDialog : Window
{
    public ConfirmationDialog(string title, string message, string confirmText)
    {
        InitializeComponent();
        DialogTitle = title;
        DialogMessage = message;
        ConfirmText = confirmText;
        DataContext = this;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public string DialogTitle { get; }
    public string DialogMessage { get; }
    public string ConfirmText { get; }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
    }
}
