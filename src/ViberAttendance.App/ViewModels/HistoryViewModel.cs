using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using ViberAttendance.App.Commands;
using ViberAttendance.App.Services;
using ViberAttendance.Core.Attendance;
using ViberAttendance.Core.Configuration;
using ViberAttendance.Core.Contracts;
using ViberAttendance.Core.Models;
using ViberAttendance.Core.Sheets;

namespace ViberAttendance.App.ViewModels;

public sealed record CallHistoryRowViewModel(
    Guid CallId,
    string CallDate,
    string StartedAt,
    string EndedAt,
    int ParticipantCount,
    string TotalDuration,
    string StatusText);

public sealed record HistoryIntervalRowViewModel(
    Guid IntervalId,
    Guid CallId,
    string DisplayName,
    string StableUiIdentity,
    string OnlineAt,
    string OfflineAt,
    string Duration,
    string StatusText,
    string CustomNumber);

public sealed class HistoryViewModel : ObservableObject
{
    private readonly IAttendanceRepository _repository;
    private readonly MonitoringCoordinator _coordinator;
    private readonly IConfirmationDialogService _confirmationDialogService;
    private readonly IFileDialogService _fileDialogs;
    private readonly ISheetAttendanceSync _sheetSync;
    private readonly AppSettings _settings;
    private static readonly JsonSerializerOptions ArchiveJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    private readonly Dictionary<Guid, string> _participantSearchIndex = [];
    private readonly Dictionary<Guid, string[]> _participantNames = [];
    private CallHistoryRowViewModel? _selectedRecord;
    private HistoryIntervalRowViewModel? _selectedInterval;
    private string _statusMessage = "选择一场通话。";
    private string _searchText = string.Empty;
    private string _selectedDateFilter = "全部日期";
    private bool _hasRecords;
    private bool _hasDetails;
    private bool _suppressDetailLoad;
    private bool _forceDetailLoad;
    private int _detailVersion;
    private Task _detailLoad = Task.CompletedTask;

    public HistoryViewModel(
        IAttendanceRepository repository,
        MonitoringCoordinator coordinator,
        IConfirmationDialogService confirmationDialogService,
        IFileDialogService fileDialogs,
        ISheetAttendanceSync sheetSync,
        AppSettings settings)
    {
        _repository = repository;
        _coordinator = coordinator;
        _confirmationDialogService = confirmationDialogService;
        _fileDialogs = fileDialogs;
        _sheetSync = sheetSync;
        _settings = settings;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ViewDetailsCommand = new AsyncRelayCommand(ViewDetailsAsync);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync);
        DeleteIntervalCommand = new AsyncRelayCommand(DeleteIntervalAsync);
        ExportCommand = new AsyncRelayCommand(ExportAsync);
        ExportArchiveCommand = new AsyncRelayCommand(ExportArchiveAsync);
        ImportArchiveCommand = new AsyncRelayCommand(ImportArchiveAsync);
        ClearCommand = new AsyncRelayCommand(ClearAsync);
        WriteSheetCommand = new AsyncRelayCommand(WriteSheetAsync);
    }

    public ObservableCollection<CallHistoryRowViewModel> Records { get; } = [];
    public ObservableCollection<CallHistoryRowViewModel> FilteredRecords { get; } = [];
    public ObservableCollection<HistoryIntervalRowViewModel> Details { get; } = [];
    public IReadOnlyList<string> DateFilterOptions { get; } = ["全部日期", "今天", "最近 7 天", "最近 30 天"];

    public bool HasRecords
    {
        get => _hasRecords;
        private set => SetProperty(ref _hasRecords, value);
    }

    public bool HasDetails
    {
        get => _hasDetails;
        private set => SetProperty(ref _hasDetails, value);
    }

    public CallHistoryRowViewModel? SelectedRecord
    {
        get => _selectedRecord;
        set
        {
            if (!SetProperty(ref _selectedRecord, value) || _suppressDetailLoad)
            {
                return;
            }

            ScheduleDetailLoad();
        }
    }

    public HistoryIntervalRowViewModel? SelectedInterval
    {
        get => _selectedInterval;
        set => SetProperty(ref _selectedInterval, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public string SelectedDateFilter
    {
        get => _selectedDateFilter;
        set
        {
            if (SetProperty(ref _selectedDateFilter, value))
            {
                ApplyFilter();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand ViewDetailsCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand DeleteIntervalCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ExportArchiveCommand { get; }
    public ICommand ImportArchiveCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand WriteSheetCommand { get; }

    internal async Task RefreshAsync()
    {
        var records = await _repository.GetCallsAsync().ConfigureAwait(true);
        Records.Clear();
        _participantSearchIndex.Clear();
        _participantNames.Clear();
        foreach (var record in records)
        {
            var row = new CallHistoryRowViewModel(
                record.CallId,
                record.StartedAtUtc.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.CurrentCulture),
                record.StartedAtUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture),
                record.EndedAtUtc?.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture) ?? "—",
                record.ParticipantCount,
                FormatDuration(record.TotalDuration),
                record.Status == UserFacingStatuses.InterruptedStatus
                    ? UserFacingStatuses.Interrupted
                    : record.EndedAtUtc is null ? "记录中" : "已保存");
            Records.Add(row);

            var intervals = await _repository.GetCallIntervalsAsync(record.CallId).ConfigureAwait(true);
            var names = intervals
                .SelectMany(interval => new[] { interval.DisplayName, interval.StableUiIdentity })
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            _participantNames[record.CallId] = names;
            _participantSearchIndex[record.CallId] = SearchTextFor(names);
        }

        _forceDetailLoad = true;
        ApplyFilter();
        _forceDetailLoad = false;
        await _detailLoad.ConfigureAwait(true);
        if (Records.Count == 0)
        {
            StatusMessage = "暂无历史记录。";
        }
    }

    private void ApplyFilter()
    {
        var today = DateTime.Today;
        var search = SearchText.Trim();
        var selectedCallId = SelectedRecord?.CallId;
        var previousCallId = selectedCallId;
        var rows = Records.Where(row =>
        {
            var matchesSearch = search.Length == 0 ||
                row.CallDate.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                row.StatusText.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                (_participantSearchIndex.TryGetValue(row.CallId, out var names) &&
                 names.Contains(search, StringComparison.CurrentCultureIgnoreCase));
            if (!matchesSearch)
            {
                return false;
            }

            if (!DateTime.TryParseExact(
                    row.CallDate,
                    "yyyy-MM-dd",
                    CultureInfo.CurrentCulture,
                    DateTimeStyles.None,
                    out var date))
            {
                return true;
            }

            return SelectedDateFilter switch
            {
                "今天" => date.Date == today,
                "最近 7 天" => date.Date >= today.AddDays(-6),
                "最近 30 天" => date.Date >= today.AddDays(-29),
                _ => true
            };
        }).ToArray();

        FilteredRecords.Clear();
        foreach (var row in rows)
        {
            FilteredRecords.Add(row);
        }

        HasRecords = FilteredRecords.Count > 0;
        _suppressDetailLoad = true;
        SelectedRecord = selectedCallId is Guid callId
            ? FilteredRecords.FirstOrDefault(row => row.CallId == callId)
            : null;
        if (SelectedRecord is null && FilteredRecords.Count > 0)
        {
            SelectedRecord = FilteredRecords[0];
        }

        _suppressDetailLoad = false;
        if (_forceDetailLoad || SelectedRecord?.CallId != previousCallId)
        {
            ScheduleDetailLoad();
        }
        else if (SelectedRecord is null)
        {
            Details.Clear();
            SelectedInterval = null;
            HasDetails = false;
            _detailLoad = Task.CompletedTask;
        }
    }

    internal void RefreshMatchedNumbers()
    {
        foreach (var pair in _participantNames)
        {
            _participantSearchIndex[pair.Key] = SearchTextFor(pair.Value);
        }

        ApplyFilter();
        ScheduleDetailLoad();
    }

    private string SearchTextFor(IEnumerable<string> names)
    {
        var codes = CustomNumberDirectory.Index(_settings.SheetSync.CustomNumbers);
        return string.Join(
            ' ',
            names.SelectMany(name => new[] { name, CustomNumberDirectory.Find(codes, name) })
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.CurrentCultureIgnoreCase));
    }

    private void ScheduleDetailLoad() => _detailLoad = LoadSelectedDetailsAsync();

    private async Task LoadSelectedDetailsAsync()
    {
        var version = ++_detailVersion;
        var record = SelectedRecord;
        if (record is null)
        {
            Details.Clear();
            SelectedInterval = null;
            HasDetails = false;
            StatusMessage = Records.Count == 0 ? "暂无历史记录。" : "选择一场通话。";
            return;
        }

        var details = await _repository.GetCallIntervalsAsync(record.CallId).ConfigureAwait(true);
        if (version != _detailVersion)
        {
            return;
        }

        Details.Clear();
        SelectedInterval = null;
        var codes = CustomNumberDirectory.Index(_settings.SheetSync.CustomNumbers);
        foreach (var row in details)
        {
            Details.Add(new HistoryIntervalRowViewModel(
                row.IntervalId,
                row.CallId,
                row.DisplayName,
                string.IsNullOrWhiteSpace(row.StableUiIdentity) ? "—" : row.StableUiIdentity,
                row.OnlineAtUtc?.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture) ?? "—",
                row.OfflineAtUtc?.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture) ?? "—",
                FormatDuration(row.Duration),
                UserFacingStatuses.DescribeIntervalStatus(row.Status, row.OfflineAtUtc is not null),
                CustomNumberDirectory.Find(codes, row.DisplayName)));
        }

        HasDetails = Details.Count > 0;
        var people = details.Select(row => row.MemberId).Distinct().Count();
        StatusMessage = $"{record.CallDate} {record.StartedAt}–{record.EndedAt}，{people} 人，{Details.Count} 段";
    }

    private Task ViewDetailsAsync() => LoadSelectedDetailsAsync();

    private async Task DeleteAsync()
    {
        if (SelectedRecord is null)
        {
            StatusMessage = "请先选择要删除的记录。";
            return;
        }

        var confirmed = await _confirmationDialogService.ConfirmAsync(
            "删除这场记录？",
            $"{SelectedRecord.CallDate} {SelectedRecord.StartedAt} 的记录将被永久删除。",
            "确认删除").ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        if (SelectedRecord.CallId == _coordinator.CurrentCallId)
        {
            StatusMessage = "这场还在记录，请先停止监控。";
            return;
        }

        await _repository.DeleteCallAsync(SelectedRecord.CallId).ConfigureAwait(true);
        Details.Clear();
        SelectedRecord = null;
        await RefreshAsync().ConfigureAwait(true);
        StatusMessage = "历史记录已删除。";
    }

    private async Task ExportAsync()
    {
        if (SelectedRecord is null)
        {
            StatusMessage = "请先选择要导出的记录。";
            return;
        }

        var path = await _coordinator.ExportCallAsync(SelectedRecord.CallId).ConfigureAwait(true);
        StatusMessage = $"这场记录已导出：{path}";
    }

    private async Task ExportArchiveAsync()
    {
        var path = _fileDialogs.ChooseHistoryExportPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var archive = await _repository.ExportHistoryAsync().ConfigureAwait(true);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(archive, ArchiveJson)).ConfigureAwait(true);
        StatusMessage = $"已导出 {archive.Calls.Count} 场记录。";
    }

    private async Task ImportArchiveAsync()
    {
        var path = _fileDialogs.ChooseHistoryImportPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        HistoryArchive? archive;
        try
        {
            archive = JsonSerializer.Deserialize<HistoryArchive>(await File.ReadAllTextAsync(path).ConfigureAwait(true), ArchiveJson);
        }
        catch (JsonException)
        {
            StatusMessage = "这个文件不是历史记录。";
            return;
        }

        if (archive is null)
        {
            StatusMessage = "这个文件不是历史记录。";
            return;
        }

        try
        {
            var result = await _repository.ImportHistoryAsync(archive).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
            var sheet = await _sheetSync.PushHistoryAsync().ConfigureAwait(true);
            StatusMessage = sheet.StartsWith("已写入", StringComparison.Ordinal)
                ? $"已导入 {result.ImportedCalls} 场，跳过已存在的 {result.SkippedCalls} 场。{sheet}"
                : $"已导入 {result.ImportedCalls} 场，跳过已存在的 {result.SkippedCalls} 场。";
        }
        catch (InvalidOperationException exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private async Task WriteSheetAsync()
    {
        StatusMessage = "正在写入表格…";
        StatusMessage = await _sheetSync.PushHistoryAsync().ConfigureAwait(true);
    }

    private async Task ClearAsync()
    {
        var confirmed = await _confirmationDialogService.ConfirmAsync(
            "清空历史记录？",
            "已保存的通话记录会被永久删除。正在记录的这场会保留。",
            "确认清空").ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        var deleted = await _repository.DeleteAllHistoryAsync(_coordinator.CurrentCallId).ConfigureAwait(true);
        Details.Clear();
        SelectedRecord = null;
        SelectedInterval = null;
        await RefreshAsync().ConfigureAwait(true);
        StatusMessage = deleted == 0 ? "没有可清空的历史记录。" : $"已清空 {deleted} 场记录。";
    }

    private async Task DeleteIntervalAsync()
    {
        if (SelectedInterval is null)
        {
            StatusMessage = "请先在成员明细里选择一段记录。";
            return;
        }

        if (SelectedInterval.CallId == _coordinator.CurrentCallId)
        {
            StatusMessage = "这场还在记录，请先停止监控。";
            return;
        }

        var confirmed = await _confirmationDialogService.ConfirmAsync(
            "删除这段记录？",
            $"{SelectedInterval.DisplayName} 的这段在线记录将被永久删除。",
            "确认删除").ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        await _repository.DeleteIntervalAsync(SelectedInterval.IntervalId).ConfigureAwait(true);
        SelectedInterval = null;
        await RefreshAsync().ConfigureAwait(true);
        StatusMessage = "这段记录已删除。";
    }

    private static string FormatDuration(TimeSpan value) => DurationText.Format(value);
}
