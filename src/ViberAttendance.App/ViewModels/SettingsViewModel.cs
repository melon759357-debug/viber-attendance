using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using ViberAttendance.App.Commands;
using ViberAttendance.App.Services;
using ViberAttendance.Core.Configuration;
using ViberAttendance.Core.Contracts;

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
    private bool _isAdvancedDiagnosticsUnlocked;
    private readonly string _versionText = "版本 1.0.0";
    private TimeMarkScheme? _selectedScheme;
    private string _serviceAccountEmail = "未选择密钥";

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
        Schemes = new ObservableCollection<TimeMarkScheme>(settings.SheetSync.Schemes);
        _selectedScheme = Schemes.FirstOrDefault(scheme =>
            scheme.Name.Equals(settings.SheetSync.ActiveSchemeName, StringComparison.OrdinalIgnoreCase)) ?? Schemes[0];
        LoadTiers(_selectedScheme);
        RefreshAccountEmail();
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        VersionClickCommand = new RelayCommand(HandleVersionClick);
        BrowseKeyCommand = new RelayCommand(BrowseKey);
        CopyScriptCommand = new RelayCommand(CopyScript);
        TryLinkCommand = new AsyncRelayCommand(TryLinkAsync);
        AddSchemeCommand = new RelayCommand(AddScheme);
        DuplicateSchemeCommand = new RelayCommand(DuplicateScheme);
        DeleteSchemeCommand = new RelayCommand(DeleteScheme);
        AddTierCommand = new RelayCommand(AddTier);
        RemoveTierCommand = new RelayCommand(RemoveLastTier);
    }

    public event EventHandler? AdvancedDiagnosticsUnlocked;

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
    public ICommand VersionClickCommand { get; }
    public ICommand BrowseKeyCommand { get; }
    public ICommand CopyScriptCommand { get; }
    public ICommand TryLinkCommand { get; }
    public ICommand AddSchemeCommand { get; }
    public ICommand DuplicateSchemeCommand { get; }
    public ICommand DeleteSchemeCommand { get; }
    public ICommand AddTierCommand { get; }
    public ICommand RemoveTierCommand { get; }

    private async Task SaveAsync()
    {
        RememberTiers();
        _settings.SheetSync.Schemes = Schemes.ToList();
        _settings.SheetSync.ActiveSchemeName = SelectedScheme?.Name ?? "默认";
        _settings.Normalize(_settings.DatabasePath, _settings.ExportFolder);
        SelectedScheme = Schemes.FirstOrDefault(scheme =>
            scheme.Name.Equals(_settings.SheetSync.ActiveSchemeName, StringComparison.OrdinalIgnoreCase));
        await _settingsStore.SaveAsync(_settings).ConfigureAwait(true);
        StartupRegistrationService.Apply(_settings.StartWithWindows);
        ThemeService.Apply(_settings.UiTheme);
        if (_settings.SheetSync.DetailEnabled && !string.IsNullOrWhiteSpace(_settings.SheetSync.DetailSpreadsheetUrl))
        {
            StatusMessage = "已保存。正在写入历史…";
            var history = await _sheetSync.PushHistoryAsync().ConfigureAwait(true);
            StatusMessage = $"已保存。{history}";
            return;
        }

        StatusMessage = "已保存。";
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
