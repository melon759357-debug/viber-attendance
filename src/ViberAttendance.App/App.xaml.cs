using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ViberAttendance.App.Services;
using ViberAttendance.App.ViewModels;
using ViberAttendance.Core.Configuration;
using ViberAttendance.Core.Contracts;
using ViberAttendance.Infrastructure;
using ViberAttendance.Persistence;
using ViberAttendance.UIAutomation;

namespace ViberAttendance.App;

public partial class App : Application
{
    private const string SingleInstanceName = @"Local\ViberAttendance.SingleInstance";

    private ServiceProvider? _services;
    private AppSettings? _settings;
    private Mutex? _singleInstance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var handoff = AppUpdater.HandoffToInstalledCopy(processPath);
            if (handoff == StartupHandoff.NeedsClose)
            {
                MessageBox.Show(
                    "请先退出正在运行的 Viber 在线记录，再打开这个新版本。打开后会替换已安装的程序并重新启动。",
                    "请先退出旧版本",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown(0);
                return;
            }

            if (handoff == StartupHandoff.Relaunched)
            {
                Shutdown(0);
                return;
            }
        }

        _singleInstance = new Mutex(true, SingleInstanceName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            MessageBox.Show(
                "Viber 在线记录已在运行，请在任务栏中找到已打开的窗口。",
                "已在运行",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(processPath) &&
                await AppUpdater.TryUpdateFromGitHubAsync(processPath).ConfigureAwait(true))
            {
                Shutdown(0);
                return;
            }

            var defaultPaths = ApplicationPaths.CreateDefault();
            defaultPaths.EnsureDirectories();
            var settingsStore = new JsonSettingsStore(defaultPaths);
            var settings = await settingsStore.LoadAsync().ConfigureAwait(true);
            _settings = settings;
            var paths = defaultPaths.WithDataFolder(settings.DataFolder);
            try
            {
                paths.EnsureDirectories();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                paths = defaultPaths;
            }

            _ = StartMenuRegistrationService.EnsureShortcut();

            var services = new ServiceCollection();
            services.AddSingleton(paths);
            services.AddSingleton(settings);
            services.AddSingleton<ISettingsStore>(settingsStore);
            services.AddSingleton<IClock, SystemClock>();
            services.AddSingleton<IDiagnosticLogger, PrivacyFileLogger>();
            services.AddSingleton<IDiagnosticLogMaintenance, DiagnosticLogMaintenance>();
            services.AddSingleton<IDatabaseInitializer, SqliteDatabaseInitializer>();
            services.AddSingleton<IAttendanceRepository, AttendanceRepository>();
            services.AddSingleton<IAppSessionService, AppSessionService>();
            services.AddSingleton<IViberProcessLocator, ViberProcessLocator>();
            services.AddSingleton<IViberWindowLocator, ViberWindowLocator>();
            services.AddSingleton<IUiAutomationParticipantDetector, UiAutomationParticipantDetector>();
            services.AddSingleton<IUiAutomationTreeInspector, UiAutomationTreeInspector>();
            services.AddSingleton<IUiAutomationTreeExporter, UiAutomationTreeExporter>();
            services.AddSingleton<IUiAutomationEventMonitor, UiAutomationEventMonitor>();
            services.AddSingleton<IFullDiagnosticRunner, FullDiagnosticRunner>();
            services.AddSingleton<GoogleSheetGridClient>();
            services.AddSingleton<ScriptSheetGridClient>();
            services.AddSingleton<LinkSheetGridClient>();
            services.AddSingleton<ISheetGridClient, SheetGridClientRouter>();
            services.AddSingleton<ISheetAttendanceSync, SheetAttendanceSync>();
            services.AddSingleton<IFileDialogService, WpfFileDialogService>();
            services.AddSingleton<IConfirmationDialogService, WpfConfirmationDialogService>();
            services.AddSingleton<MonitoringCoordinator>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<HistoryViewModel>();
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<AdvancedDiagnosticsViewModel>();
            services.AddSingleton<MainWindow>();
            _services = services.BuildServiceProvider(validateScopes: true);

            await _services.GetRequiredService<IDatabaseInitializer>().InitializeAsync().ConfigureAwait(true);
            await _services.GetRequiredService<IAttendanceRepository>().CloseInterruptedCallsAsync().ConfigureAwait(true);
            var sheetSync = _services.GetRequiredService<ISheetAttendanceSync>();
            await sheetSync.PushInterruptedAsync().ConfigureAwait(true);
            await sheetSync.PushHistoryAsync().ConfigureAwait(true);
            var sessionService = _services.GetRequiredService<IAppSessionService>();
            await sessionService.StartAsync().ConfigureAwait(true);

            var viewModel = _services.GetRequiredService<MainViewModel>();
            ThemeService.Apply(settings.UiTheme);
            await viewModel.InitializeAsync(settings.AutoMonitorAfterViberStarts).ConfigureAwait(true);

            var window = _services.GetRequiredService<MainWindow>();
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"ViberAttendance 启动失败。\n\n{exception.GetType().Name}: {exception.Message}",
                "启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_services is not null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            _services.GetService<MainViewModel>()?.Dispose();
            try
            {
                _services.GetRequiredService<MonitoringCoordinator>()
                    .StopAsync("ApplicationExit", timeout.Token).GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"监控停止失败: {exception}");
            }

            try
            {
                _services.GetRequiredService<IUiAutomationEventMonitor>()
                    .StopAsync(timeout.Token).GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"UIA 事件停止失败: {exception}");
            }

            try
            {
                _services.GetRequiredService<IAppSessionService>()
                    .StopAsync("Normal", timeout.Token).GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"应用会话停止失败: {exception}");
            }

            try
            {
                if (_settings is not null)
                {
                    _services.GetRequiredService<ISettingsStore>()
                        .SaveAsync(_settings, timeout.Token).GetAwaiter().GetResult();
                }
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"退出时保存设置失败: {exception}");
            }

            try
            {
                _services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"依赖容器释放失败: {exception}");
            }
        }

        if (_singleInstance is not null)
        {
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
            _singleInstance = null;
        }

        base.OnExit(e);
    }
}
