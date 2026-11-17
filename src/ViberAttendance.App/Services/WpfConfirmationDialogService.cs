using System.Windows;
using ViberAttendance.App.Views;

namespace ViberAttendance.App.Services;

public sealed class WpfConfirmationDialogService : IConfirmationDialogService
{
    public Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        var dialog = new ConfirmationDialog(title, message, confirmText)
        {
            Owner = Application.Current?.MainWindow
        };
        return Task.FromResult(dialog.ShowDialog() == true);
    }
}
