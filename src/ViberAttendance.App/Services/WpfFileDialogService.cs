using System.IO;
using Microsoft.Win32;
using ViberAttendance.Core.Models;

namespace ViberAttendance.App.Services;

public sealed class WpfFileDialogService : IFileDialogService
{
    public string? ChooseUiAutomationJsonPath(string initialFolder)
    {
        Directory.CreateDirectory(initialFolder);
        var dialog = new SaveFileDialog
        {
            Title = "导出 Viber UIA 诊断树（同时生成 JSON 和 TXT）",
            InitialDirectory = initialFolder,
            FileName = $"viber-uia-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json",
            DefaultExt = ".json",
            AddExtension = true,
            Filter = "JSON 诊断文件 (*.json)|*.json"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ChooseWindowDiagnosticsJsonPath(string initialFolder)
    {
        Directory.CreateDirectory(initialFolder);
        var dialog = new SaveFileDialog
        {
            Title = "导出 Viber 窗口诊断 JSON",
            InitialDirectory = initialFolder,
            FileName = $"viber-window-diagnostics-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json",
            DefaultExt = ".json",
            AddExtension = true,
            Filter = "JSON 诊断文件 (*.json)|*.json"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ChooseFullDiagnosticJsonPath(string initialFolder)
    {
        Directory.CreateDirectory(initialFolder);
        var dialog = new SaveFileDialog
        {
            Title = "导出完整诊断 JSON",
            InitialDirectory = initialFolder,
            FileName = FullDiagnosticFileNames.CreateJsonFileName(DateTimeOffset.Now),
            DefaultExt = ".json",
            AddExtension = true,
            Filter = "JSON 诊断文件 (*.json)|*.json"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ChooseFolder(string title, string? initialFolder)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false
        };
        if (!string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder))
        {
            dialog.InitialDirectory = initialFolder;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? ChooseServiceAccountJsonPath()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择谷歌服务账号密钥",
            Filter = "JSON 密钥 (*.json)|*.json",
            CheckFileExists = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ChooseHistoryExportPath()
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出历史记录",
            FileName = $"Viber历史记录-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json",
            DefaultExt = ".json",
            AddExtension = true,
            Filter = "历史记录 (*.json)|*.json"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ChooseHistoryImportPath()
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入历史记录",
            Filter = "历史记录 (*.json)|*.json",
            CheckFileExists = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
