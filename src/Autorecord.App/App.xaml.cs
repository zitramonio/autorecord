using System.IO;
using System.Net.Http;
using System.Windows;
using Autorecord.App.Dialogs;
using Autorecord.App.Notifications;
using Autorecord.App.Tray;
using Autorecord.Core.Audio;
using Autorecord.Core.Calendar;
using Autorecord.Core.Recording;
using Autorecord.Core.Scheduling;
using Autorecord.Core.Settings;
using Autorecord.Core.Startup;

namespace Autorecord.App;

public partial class App : System.Windows.Application
{
    private readonly object _eventsGate = new();
    private readonly HashSet<DateTimeOffset> _handledStartsAt = [];
    private readonly SemaphoreSlim _calendarRefreshGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private DateTimeOffset _appStartedAt;
    private SettingsStore? _settingsStore;
    private CalendarSyncService? _calendarSyncService;
    private RecordingCoordinator? _recordingCoordinator;
    private StartupManager? _startupManager;
    private HttpClient? _httpClient;
    private TrayIconHost? _trayIconHost;
    private WpfNotificationService? _notificationService;
    private MainWindow? _mainWindow;
    private AppSettings _settings = new();
    private IReadOnlyList<CalendarEvent> _events = [];
    private Task? _calendarLoop;
    private Task? _scheduleLoop;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _appStartedAt = DateTimeOffset.Now;
        _settingsStore = new SettingsStore(GetSettingsPath());
        _httpClient = new HttpClient();
        _calendarSyncService = new CalendarSyncService(_httpClient);
        _startupManager = new StartupManager();
        _recordingCoordinator = new RecordingCoordinator(() => new NaudioWavRecorder(), () => DateTimeOffset.Now);
        _recordingCoordinator.RecordingStarted += RecordingCoordinator_RecordingStarted;
        _recordingCoordinator.RecordingSaved += RecordingCoordinator_RecordingSaved;
        _recordingCoordinator.StopPromptRequired += RecordingCoordinator_StopPromptRequired;

        _mainWindow = new MainWindow();
        _trayIconHost = new TrayIconHost(_mainWindow);
        _notificationService = new WpfNotificationService(_trayIconHost);
        _mainWindow.RefreshCalendarRequested += MainWindow_RefreshCalendarRequested;
        _mainWindow.SettingsSaved += MainWindow_SettingsSaved;

        try
        {
            _settings = await _settingsStore.LoadAsync(_shutdown.Token);
            _mainWindow.SetSettings(_settings);
            _mainWindow.SetStatus("Настройки загружены.");
        }
        catch (Exception ex)
        {
            _mainWindow.SetStatus($"Не удалось загрузить настройки: {ex.Message}");
        }

        if (e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase))
        {
            _mainWindow.Hide();
        }
        else
        {
            _mainWindow.Show();
        }

        _calendarLoop = RunCalendarRefreshLoopAsync(_shutdown.Token);
        _scheduleLoop = RunSchedulePollingLoopAsync(_shutdown.Token);

        if (!string.IsNullOrWhiteSpace(_settings.CalendarUrl))
        {
            _ = RefreshCalendarAsync(_shutdown.Token);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shutdown.Cancel();
        StopRecordingOnExit();
        _trayIconHost?.Dispose();
        _httpClient?.Dispose();
        _shutdown.Dispose();
        base.OnExit(e);
    }

    private void MainWindow_RefreshCalendarRequested(object? sender, AppSettings settings)
    {
        _settings = settings;
        _ = RefreshCalendarAsync(_shutdown.Token);
    }

    private void MainWindow_SettingsSaved(object? sender, AppSettings settings)
    {
        _ = SaveSettingsAsync(settings, _shutdown.Token);
    }

    private async Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        if (_settingsStore is null || _startupManager is null)
        {
            return;
        }

        try
        {
            if (settings.StartWithWindows)
            {
                var executablePath = Environment.ProcessPath ?? "";
                if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                {
                    SetStatus("Автозапуск не сохранён: не удалось определить путь к приложению.");
                    return;
                }

                _startupManager.SetEnabled(true, executablePath);
            }
            else
            {
                _startupManager.SetEnabled(false, "");
            }

            await _settingsStore.SaveAsync(settings, cancellationToken);
            _settings = settings;
            SetStatus("Настройки сохранены.");
            if (!string.IsNullOrWhiteSpace(settings.CalendarUrl))
            {
                _ = RefreshCalendarAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось сохранить настройки: {ex.Message}");
        }
    }

    private async Task RefreshCalendarAsync(CancellationToken cancellationToken)
    {
        if (_calendarSyncService is null)
        {
            return;
        }

        if (!_calendarRefreshGate.Wait(0))
        {
            SetStatus("Обновление календаря уже выполняется.");
            return;
        }

        try
        {
            var events = await _calendarSyncService.DownloadAsync(_settings, cancellationToken);
            lock (_eventsGate)
            {
                _events = events;
            }

            SetStatus($"Календарь обновлён. Событий: {events.Count}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка календаря: {ex.Message}");
        }
        finally
        {
            _calendarRefreshGate.Release();
        }
    }

    private async Task RunCalendarRefreshLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RefreshCalendarAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetStatus($"Цикл календаря остановлен: {ex.Message}");
        }
    }

    private async Task RunSchedulePollingLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await CheckScheduleAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetStatus($"Цикл расписания остановлен: {ex.Message}");
        }
    }

    private async Task CheckScheduleAsync(CancellationToken cancellationToken)
    {
        if (_recordingCoordinator is null)
        {
            return;
        }

        IReadOnlyList<CalendarEvent> events;
        lock (_eventsGate)
        {
            events = _events;
        }

        var dueEvent = ScheduleMonitor.FindDueEvent(
            events,
            DateTimeOffset.Now,
            _recordingCoordinator.IsRecording,
            _appStartedAt,
            _handledStartsAt);

        if (dueEvent is null)
        {
            return;
        }

        try
        {
            await _recordingCoordinator.StartAsync(dueEvent, _settings, cancellationToken);
            _handledStartsAt.Add(dueEvent.StartsAt);
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось начать запись: {ex.Message}");
        }
    }

    private void RecordingCoordinator_RecordingStarted(object? sender, RecordingSession session)
    {
        _notificationService?.ShowInfo("Запись началась", session.CalendarEvent.Title);
        SetStatus($"Запись началась: {session.CalendarEvent.Title}");
    }

    private void RecordingCoordinator_RecordingSaved(object? sender, RecordingSession session)
    {
        _notificationService?.ShowInfo("Запись сохранена", session.OutputPath);
        SetStatus($"Запись сохранена: {session.OutputPath}");
    }

    private void RecordingCoordinator_StopPromptRequired(object? sender, RecordingSession session)
    {
        Dispatcher.BeginInvoke(() => _ = ShowStopPromptAsync(_shutdown.Token));
    }

    private void StopRecordingOnExit()
    {
        var recordingCoordinator = _recordingCoordinator;
        if (recordingCoordinator?.IsRecording != true)
        {
            return;
        }

        try
        {
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Task.Run(() => recordingCoordinator.ConfirmStopAsync(stopTimeout.Token))
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось завершить запись при выходе: {ex.Message}");
        }
    }

    private async Task ShowStopPromptAsync(CancellationToken cancellationToken)
    {
        if (_recordingCoordinator is null)
        {
            return;
        }

        var dialog = new StopRecordingDialog
        {
            Owner = _mainWindow
        };

        dialog.ShowDialog();
        try
        {
            if (dialog.Response == StopRecordingDialogResponse.Yes)
            {
                await _recordingCoordinator.ConfirmStopAsync(cancellationToken);
            }
            else if (dialog.Response == StopRecordingDialogResponse.No)
            {
                _recordingCoordinator.DeclineStop();
            }
            else
            {
                _recordingCoordinator.IgnoreStopPrompt();
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось обработать остановку записи: {ex.Message}");
        }
    }

    private void SetStatus(string status)
    {
        if (_mainWindow is null)
        {
            return;
        }

        if (Dispatcher.CheckAccess())
        {
            _mainWindow.SetStatus(status);
            return;
        }

        Dispatcher.BeginInvoke(() => _mainWindow.SetStatus(status));
    }

    private static string GetSettingsPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Autorecord",
            "settings.json");
    }
}
