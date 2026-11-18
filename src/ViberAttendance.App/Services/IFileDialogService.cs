namespace ViberAttendance.App.Services;

public interface IFileDialogService
{
    string? ChooseUiAutomationJsonPath(string initialFolder);
    string? ChooseWindowDiagnosticsJsonPath(string initialFolder);
    string? ChooseFullDiagnosticJsonPath(string initialFolder);
    string? ChooseServiceAccountJsonPath();
    string? ChooseFolder(string title, string? initialFolder);
    string? ChooseHistoryExportPath();
    string? ChooseHistoryImportPath();
    string? ChooseSheetSetupExportPath();
    string? ChooseSheetSetupImportPath();
}
