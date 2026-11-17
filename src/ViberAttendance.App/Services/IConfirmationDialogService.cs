namespace ViberAttendance.App.Services;

public interface IConfirmationDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string confirmText);
}
