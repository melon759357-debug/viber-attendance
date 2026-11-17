namespace ViberAttendance.App.Services;

public interface IFileDialogService
{
    string? ChooseUiAutomationJsonPath(string initialFolder);
    string? ChooseWindowDiagnosticsJsonPath(string initialFolder);
    string? ChooseFullDiagnosticJsonPath(string initialFolder);
    string? ChooseServiceAccountJsonPath();
    string? ChooseHistoryExportPath();
    string? ChooseHistoryImportPath();
}
