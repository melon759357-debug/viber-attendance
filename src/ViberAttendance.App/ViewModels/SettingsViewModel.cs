using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using ViberAttendance.App.Commands;
using ViberAttendance.App.Services;
using ViberAttendance.Core.Configuration;
using ViberAttendance.Core.Contracts;
using ViberAttendance.Core.Sheets;

namespace ViberAttendance.App.ViewModels;

public sealed record ThemeOptionViewModel(string Value, string DisplayName);

public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly ISettingsStore _settingsStore;
    private readonly IFileDialogService _fileDialogs;
    private readonly LinkSheetGridClient _linkClient;
    private readonly ISheetAttendanceSync _sheetSync;
    private int _versionClickCount;
    private DateTimeOffset _lastVersionClick;
    private string _statusMessage = "设置会保存在本机。";
    private string _lastSheetNote = string.Empty;
    private bool _isAdvancedDiagnosticsUnlocked;
    private readonly string _versionText = $"版本 {AppRelease.Version}";
    private readonly string _initialDatabasePath;
    private TimeMarkScheme? _selectedScheme;
    private string _serviceAccountEmail = "未选择密钥";
    private string _customNumberBatchText = string.Empty;
    private string _newCustomNumberName = string.Empty;
    private string _newCustomNumberCode = string.Empty;
    private CustomNumber? _selectedCustomNumber;

    public SettingsViewModel(
        AppSettings settings,
        ISettingsStore settingsStore,
        IFileDialogService fileDialogs,
        LinkSheetGridClient linkClient,
        ISheetAttendanceSync sheetSync)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _fileDialogs = fileDialogs;
        _linkClient = linkClient;
        _sheetSync = sheetSync;
        _initialDatabasePath = settings.DatabasePath;
        Schemes = new ObservableCollection<TimeMarkScheme>(settings.SheetSync.Schemes);
        CustomNumbers = new ObservableCollection<CustomNumber>(settings.SheetSync.CustomNumbers);
        _selectedScheme = Schemes.FirstOrDefault(scheme =>
            scheme.Name.Equals(settings.SheetSync.ActiveSchemeName, StringComparison.OrdinalIgnoreCase)) ?? Schemes[0];
        LoadTiers(_selectedScheme);
        RefreshAccountEmail();
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        RegisterTimeCommand = new AsyncRelayCommand(RegisterTimeAsync);
        VersionClickCommand = new RelayCommand(HandleVersionClick);
        BrowseKeyCommand = new RelayCommand(BrowseKey);
        BrowseDataFolderCommand = new RelayCommand(BrowseDataFolder);
        BrowseExportFolderCommand = new RelayCommand(BrowseExportFolder);
        ResetDataFolderCommand = new RelayCommand(() => DataFolder = string.Empty);
        OpenKeyConsoleCommand = new RelayCommand(() => OpenSite("https://console.cloud.google.com/"));
        OpenSheetsApiCommand = new RelayCommand(() => OpenSite("https://console.cloud.google.com/apis/library/sheets.googleapis.com"));
        CopyScriptCommand = new RelayCommand(CopyScript);
        TryLinkCommand = new AsyncRelayCommand(TryLinkAsync);
        AddSchemeCommand = new RelayCommand(AddScheme);
        DuplicateSchemeCommand = new RelayCommand(DuplicateScheme);
        DeleteSchemeCommand = new RelayCommand(DeleteScheme);
        AddTierCommand = new RelayCommand(AddTier);
        RemoveTierCommand = new RelayCommand(RemoveLastTier);
        AddCustomNumberCommand = new AsyncRelayCommand(AddCustomNumberAsync);
        ApplyCustomNumbersCommand = new AsyncRelayCommand(() => ApplyCustomNumbersAsync(false));
        PasteCustomNumbersCommand = new AsyncRelayCommand(() => ApplyCustomNumbersAsync(true));
        RemoveCustomNumberCommand = new AsyncRelayCommand(RemoveCustomNumberAsync);
        ClearCustomNumbersCommand = new AsyncRelayCommand(ClearCustomNumbersAsync);
        ExportSheetSetupCommand = new AsyncRelayCommand(ExportSheetSetupAsync);
        ImportSheetSetupCommand = new AsyncRelayCommand(ImportSheetSetupAsync);
    }

    public event EventHandler? AdvancedDiagnosticsUnlocked;
    public event EventHandler? CustomNumbersChanged;

    public AppSettings Settings => _settings;
    public IReadOnlyList<ThemeOptionViewModel> ThemeOptions { get; } =
    [
        new("System", "跟随系统"),
        new("Light", "浅色"),
        new("Dark", "深色")
    ];

    public string VersionText => _versionText;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsAdvancedDiagnosticsUnlocked
    {
        get => _isAdvancedDiagnosticsUnlocked;
        private set => SetProperty(ref _isAdvancedDiagnosticsUnlocked, value);
    }

    public ObservableCollection<TimeMarkScheme> Schemes { get; }
    public ObservableCollection<TimeMarkTier> SelectedTiers { get; } = [];

    public TimeMarkScheme? SelectedScheme
    {
        get => _selectedScheme;
        set
        {
            RememberTiers();
            if (!SetProperty(ref _selectedScheme, value) || value is null)
            {
                return;
            }

            _settings.SheetSync.ActiveSchemeName = value.Name;
            LoadTiers(value);
        }
    }

    public string ServiceAccountJsonPath
    {
        get => _settings.SheetSync.ServiceAccountJsonPath;
        set
        {
            _settings.SheetSync.ServiceAccountJsonPath = value;
            OnPropertyChanged();
            RefreshAccountEmail();
        }
    }

    public string ServiceAccountEmail
    {
        get => _serviceAccountEmail;
        private set => SetProperty(ref _serviceAccountEmail, value);
    }

    public bool IsKeyWriteMode
    {
        get => _settings.SheetSync.WriteMode.Equals("Key", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (!value)
            {
                return;
            }

            _settings.SheetSync.WriteMode = "Key";
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsScriptWriteMode));
            OnPropertyChanged(nameof(IsLinkWriteMode));
        }
    }

    public bool IsScriptWriteMode
    {
        get => _settings.SheetSync.WriteMode.Equals("Script", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (!value)
            {
                return;
            }

            _settings.SheetSync.WriteMode = "Script";
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsKeyWriteMode));
            OnPropertyChanged(nameof(IsLinkWriteMode));
        }
    }

    public bool IsLinkWriteMode
    {
        get => _settings.SheetSync.WriteMode.Equals("Link", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (!value)
            {
                return;
            }

            _settings.SheetSync.WriteMode = "Link";
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsKeyWriteMode));
            OnPropertyChanged(nameof(IsScriptWriteMode));
        }
    }

    public string DataFolder
    {
        get => _settings.DataFolder;
        set
        {
            _settings.DataFolder = value;
            OnPropertyChanged();
        }
    }

    public string ExportFolder
    {
        get => _settings.ExportFolder;
        set
        {
            _settings.ExportFolder = value;
            OnPropertyChanged();
        }
    }

    public string ScriptWebAppUrl
    {
        get => _settings.SheetSync.ScriptWebAppUrl;
        set
        {
            _settings.SheetSync.ScriptWebAppUrl = value;
            OnPropertyChanged();
        }
    }

    public ICommand SaveCommand { get; }
    public ICommand RegisterTimeCommand { get; }
    public ICommand VersionClickCommand { get; }
    public ICommand BrowseKeyCommand { get; }
    public ICommand BrowseDataFolderCommand { get; }
    public ICommand BrowseExportFolderCommand { get; }
    public ICommand ResetDataFolderCommand { get; }
    public ICommand OpenKeyConsoleCommand { get; }
    public ICommand OpenSheetsApiCommand { get; }
    public ICommand CopyScriptCommand { get; }
    public ICommand TryLinkCommand { get; }
    public ICommand AddSchemeCommand { get; }
    public ICommand DuplicateSchemeCommand { get; }
    public ICommand DeleteSchemeCommand { get; }
    public ICommand AddTierCommand { get; }
    public ICommand RemoveTierCommand { get; }
    public ICommand AddCustomNumberCommand { get; }
    public ICommand ApplyCustomNumbersCommand { get; }
    public ICommand PasteCustomNumbersCommand { get; }
    public ICommand RemoveCustomNumberCommand { get; }
    public ICommand ClearCustomNumbersCommand { get; }
    public ICommand ExportSheetSetupCommand { get; }
    public ICommand ImportSheetSetupCommand { get; }

    public ObservableCollection<CustomNumber> CustomNumbers { get; }

    public string CustomNumberBatchText
    {
        get => _customNumberBatchText;
        set => SetProperty(ref _customNumberBatchText, value);
    }

    public string NewCustomNumberName
    {
        get => _newCustomNumberName;
        set => SetProperty(ref _newCustomNumberName, value);
    }

    public string NewCustomNumberCode
    {
        get => _newCustomNumberCode;
        set => SetProperty(ref _newCustomNumberCode, value);
    }

    public CustomNumber? SelectedCustomNumber
    {
        get => _selectedCustomNumber;
        set => SetProperty(ref _selectedCustomNumber, value);
    }

    private async Task ExportSheetSetupAsync()
    {
        var path = _fileDialogs.ChooseSheetSetupExportPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            PersistScheme();
            await File.WriteAllTextAsync(path, SheetSetupTransfer.Export(_settings.SheetSync)).ConfigureAwait(true);
            StatusMessage = "已导出实时和最终的设置。表格链接没有写进文件。";
        }
        catch (Exception)
        {
            StatusMessage = "设置没有导出成功。";
        }
    }

    private async Task ImportSheetSetupAsync()
    {
        var path = _fileDialogs.ChooseSheetSetupImportPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path).ConfigureAwait(true);
            if (!SheetSetupTransfer.TryImport(_settings.SheetSync, json, out var message))
            {
                StatusMessage = message;
                return;
            }

            ShowImportedSetup();
            if (!await TryRememberSettingsAsync().ConfigureAwait(true))
            {
                return;
            }

            StatusMessage = WithSheetNote("已导入实时和最终的设置。表格链接没有改动。");
        }
        catch (Exception)
        {
            StatusMessage = "设置没有导入成功。";
        }
    }

    private void ShowImportedSetup()
    {
        Schemes.Clear();
        foreach (var scheme in _settings.SheetSync.Schemes)
        {
            Schemes.Add(scheme);
        }

        _selectedScheme = null;
        SelectedScheme = Schemes.FirstOrDefault(scheme =>
            scheme.Name.Equals(_settings.SheetSync.ActiveSchemeName, StringComparison.OrdinalIgnoreCase)) ?? Schemes[0];
        ReplaceCustomNumbers(_settings.SheetSync.CustomNumbers);
    }

    private async Task RegisterTimeAsync()
    {
        PersistScheme();
        await _settingsStore.SaveAsync(_settings).ConfigureAwait(true);
        StatusMessage = "正在登记…";
        StatusMessage = await _sheetSync.RegisterTimeAsync().ConfigureAwait(true);
    }

    private void PersistScheme()
    {
        RememberTiers();
        _settings.SheetSync.Schemes = Schemes.ToList();
        _settings.SheetSync.CustomNumbers = CustomNumbers.ToList();
        _settings.SheetSync.ActiveSchemeName = SelectedScheme?.Name ?? "默认";
        _settings.Normalize(_settings.DatabasePath, _settings.ExportFolder);
        SelectedScheme = Schemes.FirstOrDefault(scheme =>
            scheme.Name.Equals(_settings.SheetSync.ActiveSchemeName, StringComparison.OrdinalIgnoreCase));
        ReplaceCustomNumbers(_settings.SheetSync.CustomNumbers);
    }

    private async Task AddCustomNumberAsync()
    {
        if (CustomNumberDirectory.Key(NewCustomNumberName).Length == 0 || string.IsNullOrWhiteSpace(NewCustomNumberCode))
        {
            StatusMessage = "请填写名字和号码。";
            return;
        }

        var merged = CustomNumberDirectory.Merge(CustomNumbers,
        [
            new CustomNumber { Name = NewCustomNumberName, Code = NewCustomNumberCode }
        ]);
        ReplaceCustomNumbers(merged);
        NewCustomNumberName = string.Empty;
        NewCustomNumberCode = string.Empty;
        if (!await TryRememberSettingsAsync().ConfigureAwait(true))
        {
            return;
        }

        StatusMessage = WithSheetNote("已添加。同名的人会改成新号码。");
    }

    private async Task ApplyCustomNumbersAsync(bool fromClipboard)
    {
        var text = fromClipboard ? ReadClipboard() : CustomNumberBatchText;
        if (fromClipboard && string.IsNullOrWhiteSpace(text))
        {
            StatusMessage = "剪贴板里没有文字。";
            return;
        }

        if (fromClipboard)
        {
            CustomNumberBatchText = text ?? string.Empty;
        }

        var parsed = CustomNumberDirectory.Parse(text);
        if (parsed.Count == 0)
        {
            StatusMessage = "没有读到号码。从表格复制两列，第一列名字，第二列号码。";
            return;
        }

        var merged = CustomNumberDirectory.Merge(CustomNumbers, parsed);
        ReplaceCustomNumbers(merged);
        if (!await TryRememberSettingsAsync().ConfigureAwait(true))
        {
            return;
        }

        StatusMessage = WithSheetNote($"已读入 {parsed.Count} 行，现在共有 {merged.Count} 个自定义号码。");
    }

    private async Task RemoveCustomNumberAsync()
    {
        if (SelectedCustomNumber is not CustomNumber selected)
        {
            StatusMessage = "请先选中一个号码。";
            return;
        }

        CustomNumbers.Remove(selected);
        if (!await TryRememberSettingsAsync().ConfigureAwait(true))
        {
            return;
        }

        StatusMessage = WithSheetNote("已删除这个号码。");
    }

    private async Task ClearCustomNumbersAsync()
    {
        CustomNumbers.Clear();
        if (!await TryRememberSettingsAsync().ConfigureAwait(true))
        {
            return;
        }

        StatusMessage = WithSheetNote("自定义号码已清空。");
    }

    public async Task RememberCustomNumbersAsync()
    {
        try
        {
            var pending = CustomNumbers
                .Where(item => CustomNumberDirectory.Key(item.Name).Length == 0 || string.IsNullOrWhiteSpace(item.Code))
                .ToList();
            RememberTiers();
            _settings.SheetSync.Schemes = Schemes.ToList();
            _settings.SheetSync.CustomNumbers = CustomNumbers.ToList();
            _settings.SheetSync.ActiveSchemeName = SelectedScheme?.Name ?? "默认";
            _settings.Normalize(_settings.DatabasePath, _settings.ExportFolder);
            if (pending.Count == 0)
            {
                SelectedScheme = Schemes.FirstOrDefault(scheme =>
                    scheme.Name.Equals(_settings.SheetSync.ActiveSchemeName, StringComparison.OrdinalIgnoreCase));
                ReplaceCustomNumbers(_settings.SheetSync.CustomNumbers);
            }

            await _settingsStore.SaveAsync(_settings).ConfigureAwait(true);
            CustomNumbersChanged?.Invoke(this, EventArgs.Empty);
            var saved = DescribeSavedNumbers(pending.Count > 0);
            _lastSheetNote = await _sheetSync.RefreshCustomNumbersAsync().ConfigureAwait(true);
            StatusMessage = saved + _lastSheetNote;
        }
        catch (Exception)
        {
            StatusMessage = "自定义号码没有保存成功。";
        }
    }

    private async Task<bool> TryRememberSettingsAsync()
    {
        try
        {
            PersistScheme();
            await _settingsStore.SaveAsync(_settings).ConfigureAwait(true);
            CustomNumbersChanged?.Invoke(this, EventArgs.Empty);
            _lastSheetNote = await _sheetSync.RefreshCustomNumbersAsync().ConfigureAwait(true);
            return true;
        }
        catch (Exception)
        {
            _lastSheetNote = string.Empty;
            StatusMessage = "自定义号码没有保存成功。";
            return false;
        }
    }

    private void ReplaceCustomNumbers(IEnumerable<CustomNumber> numbers)
    {
        CustomNumbers.Clear();
        foreach (var number in numbers)
        {
            CustomNumbers.Add(number);
        }
    }

    private static string? ReadClipboard()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task SaveAsync()
    {
        var previousDatabase = _initialDatabasePath;
        PersistScheme();
        var dataMoved = CopyDatabaseIfNeeded(previousDatabase, _settings.DatabasePath);
        await _settingsStore.SaveAsync(_settings).ConfigureAwait(true);
        StartupRegistrationService.Apply(_settings.StartWithWindows);
        ThemeService.Apply(_settings.UiTheme);
        if (_settings.SheetSync.DetailEnabled && !string.IsNullOrWhiteSpace(_settings.SheetSync.DetailSpreadsheetUrl))
        {
            StatusMessage = "已保存。正在写入历史…";
            var history = await _sheetSync.PushHistoryAsync().ConfigureAwait(true);
            StatusMessage = dataMoved ? $"已保存。{history} 数据位置在重新打开后生效。" : $"已保存。{history}";
            return;
        }

        StatusMessage = dataMoved ? "已保存。数据位置在重新打开后生效。" : "已保存。";
    }

    private static bool CopyDatabaseIfNeeded(string source, string destination)
    {
        if (string.IsNullOrWhiteSpace(source) ||
            string.IsNullOrWhiteSpace(destination) ||
            string.Equals(source, destination, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(source) ||
            File.Exists(destination))
        {
            return !string.Equals(source, destination, StringComparison.OrdinalIgnoreCase);
        }

        var folder = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(folder))
        {
            return true;
        }

        Directory.CreateDirectory(folder);
        File.Copy(source, destination);
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var side = source + suffix;
            if (File.Exists(side))
            {
                File.Copy(side, destination + suffix);
            }
        }

        return true;
    }

    private void HandleVersionClick()
    {
        var now = DateTimeOffset.UtcNow;
        _versionClickCount = now - _lastVersionClick <= TimeSpan.FromSeconds(3)
            ? _versionClickCount + 1
            : 1;
        _lastVersionClick = now;
        if (_versionClickCount < 5)
        {
            return;
        }

        _versionClickCount = 0;
        IsAdvancedDiagnosticsUnlocked = true;
        StatusMessage = "高级诊断已打开。";
        AdvancedDiagnosticsUnlocked?.Invoke(this, EventArgs.Empty);
    }

    private void BrowseDataFolder()
    {
        var folder = _fileDialogs.ChooseFolder("选择数据位置", string.IsNullOrWhiteSpace(DataFolder) ? null : DataFolder);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            DataFolder = folder;
        }
    }

    private void BrowseExportFolder()
    {
        var folder = _fileDialogs.ChooseFolder("选择导出文件夹", string.IsNullOrWhiteSpace(ExportFolder) ? null : ExportFolder);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            ExportFolder = folder;
        }
    }

    private static void OpenSite(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void BrowseKey()
    {
        var path = _fileDialogs.ChooseServiceAccountJsonPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            ServiceAccountJsonPath = path;
        }
    }

    private void CopyScript()
    {
        Clipboard.SetText(ScriptSheetProtocol.AppsScript);
        StatusMessage = "脚本已复制。";
    }

    private async Task TryLinkAsync()
    {
        var detail = _settings.SheetSync.DetailSpreadsheetUrl.Trim();
        var time = _settings.SheetSync.TimeSpreadsheetUrl.Trim();
        if (detail.Length == 0 && time.Length == 0)
        {
            StatusMessage = "请先填写表格链接。";
            return;
        }

        StatusMessage = "正在试链接…";
        var parts = new List<string>();
        if (detail.Length > 0)
        {
            parts.Add("实时：" + await _linkClient.ProbeAsync(detail, _settings.SheetSync.DetailSheetName, CancellationToken.None).ConfigureAwait(true));
        }

        if (time.Length > 0)
        {
            parts.Add("最终：" + await _linkClient.ProbeAsync(time, _settings.SheetSync.TimeSheetName, CancellationToken.None).ConfigureAwait(true));
        }

        StatusMessage = string.Join(" ", parts);
    }

    private void AddScheme()
    {
        var scheme = TimeMarkScheme.CreateDefault();
        scheme.Name = UniqueName("方案");
        Schemes.Add(scheme);
        SelectedScheme = scheme;
    }

    private void DuplicateScheme()
    {
        if (SelectedScheme is null)
        {
            return;
        }

        RememberTiers();
        var copy = SelectedScheme.Copy(UniqueName(SelectedScheme.Name));
        Schemes.Add(copy);
        SelectedScheme = copy;
    }

    private void DeleteScheme()
    {
        if (SelectedScheme is null || Schemes.Count <= 1)
        {
            return;
        }

        var remove = SelectedScheme;
        SelectedScheme = Schemes.First(scheme => !ReferenceEquals(scheme, remove));
        Schemes.Remove(remove);
    }

    private void AddTier()
    {
        SelectedTiers.Add(new TimeMarkTier { MinimumMinutes = 10, Symbol = "●", Color = "#F9A825" });
    }

    private void RemoveLastTier()
    {
        if (SelectedTiers.Count > 0)
        {
            SelectedTiers.RemoveAt(SelectedTiers.Count - 1);
        }
    }

    private void RememberTiers()
    {
        if (_selectedScheme is not null)
        {
            _selectedScheme.Tiers = SelectedTiers.ToList();
        }
    }

    private void LoadTiers(TimeMarkScheme scheme)
    {
        SelectedTiers.Clear();
        foreach (var tier in scheme.Tiers)
        {
            SelectedTiers.Add(tier);
        }
    }

    private string UniqueName(string prefix)
    {
        var name = prefix;
        var index = 2;
        while (Schemes.Any(scheme => scheme.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            name = $"{prefix}{index}";
            index++;
        }

        return name;
    }

    private string DescribeSavedNumbers(bool incomplete)
    {
        var count = _settings.SheetSync.CustomNumbers.Count;
        var text = count == 0
            ? "还没有完整的名字和号码。"
            : $"已按名字对照 {count} 个号码。";
        return incomplete ? text + "还有没填完的一行，这一行还没保存。" : text;
    }

    private string WithSheetNote(string message) =>
        string.IsNullOrEmpty(_lastSheetNote) ? message : message + _lastSheetNote;

    private void RefreshAccountEmail()
    {
        var path = _settings.SheetSync.ServiceAccountJsonPath;
        var email = "未选择密钥";
        try
        {
            if (File.Exists(path))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.TryGetProperty("client_email", out var value))
                {
                    email = value.GetString() ?? email;
                }
            }
        }
        catch (Exception)
        {
            email = "密钥文件无法读取";
        }

        ServiceAccountEmail = email;
    }
}
