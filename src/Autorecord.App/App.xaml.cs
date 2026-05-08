using System.IO;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using Autorecord.App.Transcription;
using Autorecord.App.Dialogs;
using Autorecord.App.Notifications;
using Autorecord.App.Tray;
using Autorecord.Core.Audio;
using Autorecord.Core.Calendar;
using Autorecord.Core.Recording;
using Autorecord.Core.Scheduling;
using Autorecord.Core.Settings;
using Autorecord.Core.Startup;
using Autorecord.Core.Transcription;
using Autorecord.Core.Transcription.Diarization;
using Autorecord.Core.Transcription.Engines;
using Autorecord.Core.Transcription.Jobs;
using Autorecord.Core.Transcription.Models;
using Autorecord.Core.Transcription.Pipeline;
using Autorecord.Core.Transcription.Results;

namespace Autorecord.App;

public partial class App : System.Windows.Application
{
    private readonly object _eventsGate = new();
    private readonly object _settingsGate = new();
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
    private TranscriptionNotificationService? _transcriptionNotificationService;
    private MainWindow? _mainWindow;
    private ModelCatalog? _modelCatalog;
    private ModelManager? _modelManager;
    private ModelDownloadService? _modelDownloadService;
    private ModelInstallService? _modelInstallService;
    private TranscriptionJobRepository? _transcriptionJobRepository;
    private TranscriptionQueue? _transcriptionQueue;
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
        _recordingCoordinator.RecordingSaveFailed += RecordingCoordinator_RecordingSaveFailed;
        _recordingCoordinator.StopPromptRequired += RecordingCoordinator_StopPromptRequired;

        _mainWindow = new MainWindow();
        _trayIconHost = new TrayIconHost(_mainWindow);
        _notificationService = new WpfNotificationService(_trayIconHost);
        _transcriptionNotificationService = new TranscriptionNotificationService(_notificationService);
        _mainWindow.RefreshCalendarRequested += MainWindow_RefreshCalendarRequested;
        _mainWindow.SettingsSaved += MainWindow_SettingsSaved;
        _mainWindow.ManualRecordingStartRequested += MainWindow_ManualRecordingStartRequested;
        _mainWindow.ManualRecordingStopRequested += MainWindow_ManualRecordingStopRequested;
        _mainWindow.DownloadSelectedModelRequested += MainWindow_DownloadSelectedModelRequested;
        _mainWindow.DeleteSelectedModelRequested += MainWindow_DeleteSelectedModelRequested;
        _mainWindow.ValidateSelectedModelRequested += MainWindow_ValidateSelectedModelRequested;
        _mainWindow.OpenModelsFolderRequested += MainWindow_OpenModelsFolderRequested;
        _mainWindow.PickFileForTranscriptionRequested += MainWindow_PickFileForTranscriptionRequested;

        try
        {
            _settings = await _settingsStore.LoadAsync(_shutdown.Token);
            _mainWindow.SetSettings(_settings);
            _mainWindow.SetStatus("Настройки загружены.");
            _ = ApplyRecorderReadinessAsync(_settings, _shutdown.Token);
            _ = RecoverPendingRecordingsAsync(_settings.OutputFolder, _shutdown.Token);
        }
        catch (Exception ex)
        {
            _mainWindow.SetStatus($"Не удалось загрузить настройки: {ex.Message}");
        }

        try
        {
            await InitializeTranscriptionAsync(_settings.Transcription, _shutdown.Token);
        }
        catch (Exception ex)
        {
            _mainWindow.SetStatus($"Не удалось инициализировать транскрибацию: {ex.Message}");
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
        DisposeRecordingCoordinatorOnExit();
        _trayIconHost?.Dispose();
        _httpClient?.Dispose();
        _shutdown.Dispose();
        base.OnExit(e);
    }

    private void MainWindow_RefreshCalendarRequested(object? sender, AppSettings settings)
    {
        SetCurrentSettings(settings);
        _ = RefreshCalendarAsync(_shutdown.Token);
    }

    private void MainWindow_SettingsSaved(object? sender, AppSettings settings)
    {
        _ = SaveSettingsAsync(settings, _shutdown.Token);
    }

    private void MainWindow_ManualRecordingStartRequested(object? sender, AppSettings settings)
    {
        _ = StartManualRecordingAsync(settings, _shutdown.Token);
    }

    private void MainWindow_ManualRecordingStopRequested(object? sender, EventArgs e)
    {
        _ = StopManualRecordingAsync(_shutdown.Token);
    }

    private void MainWindow_DownloadSelectedModelRequested(object? sender, EventArgs e)
    {
        _ = DownloadSelectedModelAsync(_shutdown.Token);
    }

    private void MainWindow_DeleteSelectedModelRequested(object? sender, EventArgs e)
    {
        _ = DeleteSelectedModelAsync(_shutdown.Token);
    }

    private void MainWindow_ValidateSelectedModelRequested(object? sender, EventArgs e)
    {
        _ = ValidateSelectedModelAsync(_shutdown.Token);
    }

    private void MainWindow_OpenModelsFolderRequested(object? sender, EventArgs e)
    {
        OpenModelsFolder();
    }

    private void MainWindow_PickFileForTranscriptionRequested(object? sender, EventArgs e)
    {
        _ = PickFileForTranscriptionAsync(_shutdown.Token);
    }

    private async Task StartManualRecordingAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        if (_recordingCoordinator is null)
        {
            return;
        }

        if (_recordingCoordinator.IsRecording)
        {
            SetRecordingState(true, _recordingCoordinator.CurrentSession?.CalendarEvent.Title);
            SetStatus("Запись уже идёт.");
            return;
        }

        try
        {
            SetCurrentSettings(settings);
            var startsAt = DateTimeOffset.Now;
            var manualEvent = new CalendarEvent("Ручная запись", startsAt, startsAt);
            await _recordingCoordinator.StartAsync(manualEvent, settings, cancellationToken);
        }
        catch (Exception ex)
        {
            SetRecordingState(false);
            SetStatus($"Не удалось начать ручную запись: {ex.Message}");
        }
    }

    private async Task StopManualRecordingAsync(CancellationToken cancellationToken)
    {
        if (_recordingCoordinator?.IsRecording != true)
        {
            SetRecordingState(false);
            SetStatus("Запись не идёт.");
            return;
        }

        try
        {
            await _recordingCoordinator.ConfirmStopAsync(cancellationToken);
            SetRecordingState(false, "Запись остановлена. MP3 сохраняется...");
            SetStatus("Запись остановлена. MP3 сохраняется...");
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось остановить запись: {ex.Message}");
        }
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
            SetCurrentSettings(settings);
            _ = ApplyRecorderReadinessAsync(settings, cancellationToken);
            _ = RecoverPendingRecordingsAsync(settings.OutputFolder, cancellationToken);
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
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
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
        SetRecordingState(true, session.CalendarEvent.Title);
        SetStatus($"Запись началась: {session.CalendarEvent.Title}");
    }

    private void RecordingCoordinator_RecordingSaved(object? sender, RecordingSession session)
    {
        _notificationService?.ShowInfo("Запись сохранена", session.OutputPath);
        SetRecordingState(false, $"Запись сохранена: {session.OutputPath}");
        SetStatus($"Запись сохранена: {session.OutputPath}");
        _ = EnqueueRecordingForTranscriptionAsync(session, _shutdown.Token);
    }

    private void RecordingCoordinator_RecordingSaveFailed(object? sender, RecordingSaveFailedEventArgs args)
    {
        var message = $"MP3 не сохранён. Резервный WAV оставлен: {args.TemporaryWavPath}. Ошибка: {args.Error.Message}";
        _notificationService?.ShowInfo("Ошибка сохранения записи", message);
        SetRecordingState(false, message);
        SetStatus(message);
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

    private void DisposeRecordingCoordinatorOnExit()
    {
        try
        {
            _recordingCoordinator?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось освободить аудиоустройства: {ex.Message}");
        }
    }

    private async Task ApplyRecorderReadinessAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        if (_recordingCoordinator is null)
        {
            return;
        }

        try
        {
            await _recordingCoordinator.ApplySettingsAsync(settings, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось подготовить микрофон: {ex.Message}");
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
                SetRecordingState(false, "Запись остановлена. MP3 сохраняется...");
                SetStatus("Запись остановлена. MP3 сохраняется...");
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

    private void SetRecordingState(bool isRecording, string? details = null)
    {
        if (_mainWindow is null)
        {
            return;
        }

        if (Dispatcher.CheckAccess())
        {
            _mainWindow.SetRecordingState(isRecording, details);
            return;
        }

        Dispatcher.BeginInvoke(() => _mainWindow.SetRecordingState(isRecording, details));
    }

    private async Task RecoverPendingRecordingsAsync(string outputFolder, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            return;
        }

        try
        {
            await NaudioWavRecorder.RecoverPendingConversionsAsync(
                outputFolder,
                args =>
                {
                    _notificationService?.ShowInfo("Запись сохранена", args.SavedOutputPath);
                    SetStatus($"Восстановленная запись сохранена: {args.SavedOutputPath}");
                },
                args =>
                {
                    var message = $"Не удалось восстановить MP3. WAV оставлен: {args.TemporaryWavPath}. Ошибка: {args.Error.Message}";
                    _notificationService?.ShowInfo("Ошибка восстановления записи", message);
                    SetStatus(message);
                },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось проверить незавершённые записи: {ex.Message}");
        }
    }

    private async Task InitializeTranscriptionAsync(TranscriptionSettings settings, CancellationToken cancellationToken)
    {
        if (_httpClient is null || _mainWindow is null)
        {
            return;
        }

        var catalogPath = ResolveModelCatalogPath();
        _modelCatalog = await ModelCatalog.LoadAsync(catalogPath, cancellationToken);
        _modelManager = new ModelManager(GetAppDataPath("Models"));
        _modelDownloadService = new ModelDownloadService(_httpClient, GetAppDataPath("Temp", "Downloads"));
        _modelInstallService = new ModelInstallService(_modelManager);
        _transcriptionJobRepository = new TranscriptionJobRepository(GetAppDataPath("transcription-jobs.json"));

        var pipeline = new CurrentSettingsTranscriptionPipeline(
            GetCurrentTranscriptionSettings,
            CreateTranscriptionPipeline);
        _transcriptionQueue = new TranscriptionQueue(_transcriptionJobRepository, pipeline, () => DateTimeOffset.Now);
        _transcriptionQueue.JobsChanged += TranscriptionQueue_JobsChanged;
        await _transcriptionQueue.InitializeAsync(cancellationToken);
        await RefreshModelListAsync(cancellationToken);
        RefreshTranscriptionJobs();
    }

    private void TranscriptionQueue_JobsChanged(object? sender, EventArgs e)
    {
        RefreshTranscriptionJobs();
    }

    private async Task RefreshModelListAsync(CancellationToken cancellationToken)
    {
        if (_modelCatalog is null || _modelManager is null || _mainWindow is null)
        {
            return;
        }

        var models = new List<ModelListItemViewModel>();
        foreach (var model in _modelCatalog.GetByType("asr"))
        {
            var status = await _modelManager.GetStatusAsync(model, cancellationToken);
            models.Add(new ModelListItemViewModel(model.Id, model.DisplayName, model.Type, status.ToString()));
        }

        _ = Dispatcher.BeginInvoke(() => _mainWindow.SetModels(models));
    }

    private async Task DownloadSelectedModelAsync(CancellationToken cancellationToken)
    {
        if (!TryGetSelectedModel(out var model)
            || _modelCatalog is null
            || _modelDownloadService is null
            || _modelInstallService is null
            || _modelManager is null)
        {
            return;
        }

        var tempPaths = new List<string>();
        try
        {
            SetModelDownloadProgress(0);
            var progress = new Progress<ModelDownloadProgress>(value => SetModelDownloadProgress(value.Percent));
            var installedModels = new List<string>();
            await DownloadAndInstallModelAsync(model, progress, tempPaths, cancellationToken);
            installedModels.Add(model.DisplayName);

            if (_settings.Transcription.EnableDiarization
                && !string.IsNullOrWhiteSpace(_settings.Transcription.SelectedDiarizationModelId))
            {
                var diarizationModel = _modelCatalog.GetRequired(_settings.Transcription.SelectedDiarizationModelId);
                var diarizationStatus = await _modelManager.GetStatusAsync(diarizationModel, cancellationToken);
                if (diarizationStatus != ModelInstallStatus.Installed)
                {
                    SetModelDownloadProgress(0);
                    await DownloadAndInstallModelAsync(diarizationModel, progress, tempPaths, cancellationToken);
                    installedModels.Add(diarizationModel.DisplayName);
                }
            }

            SetModelDownloadProgress(100);
            SetStatus(installedModels.Count == 1
                ? $"Модель установлена и готова: {installedModels[0]}."
                : $"Модели установлены и готовы: {string.Join(", ", installedModels)}.");
            await RefreshModelListAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось скачать или установить модель: {ex.Message}");
        }
        finally
        {
            foreach (var tempPath in tempPaths)
            {
                DeleteTempFileQuietly(tempPath);
            }
        }
    }

    private async Task DownloadAndInstallModelAsync(
        ModelCatalogEntry model,
        IProgress<ModelDownloadProgress> progress,
        List<string> tempPaths,
        CancellationToken cancellationToken)
    {
        SetStatus($"Скачивание модели: {model.DisplayName}");
        var artifacts = await DownloadModelArtifactsAsync(model, progress, tempPaths, cancellationToken);
        SetStatus($"Установка модели: {model.DisplayName}");
        await _modelInstallService!.InstallAsync(model, artifacts, cancellationToken);
        var status = await _modelManager!.GetStatusAsync(model, cancellationToken);
        if (status != ModelInstallStatus.Installed)
        {
            throw new InvalidOperationException($"Model validation returned status '{status}'.");
        }
    }

    private async Task<IReadOnlyList<ModelInstallArtifact>> DownloadModelArtifactsAsync(
        ModelCatalogEntry model,
        IProgress<ModelDownloadProgress> progress,
        List<string> tempPaths,
        CancellationToken cancellationToken)
    {
        var artifacts = new List<ModelInstallArtifact>();

        if (!string.IsNullOrWhiteSpace(model.Download.Url))
        {
            var tempPath = await _modelDownloadService!.DownloadAsync(model, progress, cancellationToken);
            tempPaths.Add(tempPath);
            artifacts.Add(new ModelInstallArtifact
            {
                Path = tempPath,
                ArchiveType = model.Download.ArchiveType,
                Sha256 = model.Download.Sha256
            });
            return artifacts;
        }

        if (!string.IsNullOrWhiteSpace(model.Download.SegmentationUrl))
        {
            SetStatus($"Скачивание segmentation-модели: {model.DisplayName}");
            var url = model.Download.SegmentationUrl;
            var tempPath = await _modelDownloadService!.DownloadFileAsync(
                url,
                $"{model.Id}-segmentation",
                progress,
                cancellationToken);
            tempPaths.Add(tempPath);
            artifacts.Add(new ModelInstallArtifact
            {
                Path = tempPath,
                ArchiveType = InferArchiveType(model.Download.ArchiveType, url),
                Sha256 = model.Download.Sha256
            });
        }

        if (!string.IsNullOrWhiteSpace(model.Download.EmbeddingUrl))
        {
            SetStatus($"Скачивание embedding-модели: {model.DisplayName}");
            var url = model.Download.EmbeddingUrl;
            var targetFileName = GetFileNameFromUrl(url, $"{model.Id}-embedding.onnx");
            var tempPath = await _modelDownloadService!.DownloadFileAsync(
                url,
                targetFileName,
                progress,
                cancellationToken);
            tempPaths.Add(tempPath);
            artifacts.Add(new ModelInstallArtifact
            {
                Path = tempPath,
                TargetFileName = targetFileName
            });
        }

        if (artifacts.Count == 0)
        {
            throw new InvalidOperationException($"No download URL is available for model '{model.Id}'.");
        }

        return artifacts;
    }

    private static string? InferArchiveType(string? declaredArchiveType, string url)
    {
        if (!string.IsNullOrWhiteSpace(declaredArchiveType))
        {
            return declaredArchiveType;
        }

        var path = Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.LocalPath
            : url;

        if (path.EndsWith(".tar.bz2", StringComparison.OrdinalIgnoreCase))
        {
            return "tar.bz2";
        }

        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return "zip";
        }

        return null;
    }

    private static string GetFileNameFromUrl(string url, string fallback)
    {
        var path = Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.LocalPath
            : url;
        var fileName = Path.GetFileName(path);
        return string.IsNullOrWhiteSpace(fileName) ? fallback : fileName;
    }

    private static void DeleteTempFileQuietly(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private async Task DeleteSelectedModelAsync(CancellationToken cancellationToken)
    {
        if (!TryGetSelectedModel(out var model) || _modelManager is null)
        {
            return;
        }

        try
        {
            await Task.Run(
                () => _modelManager.DeleteAsync(model, cancellationToken).GetAwaiter().GetResult(),
                cancellationToken);
            var status = await _modelManager.GetStatusAsync(model, cancellationToken);
            SetStatus($"Модель удалена. Статус: {status}.");
            await RefreshModelListAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось удалить модель: {ex.Message}");
        }
    }

    private async Task ValidateSelectedModelAsync(CancellationToken cancellationToken)
    {
        if (!TryGetSelectedModel(out var model) || _modelManager is null)
        {
            return;
        }

        try
        {
            var status = await _modelManager.GetStatusAsync(model, cancellationToken);
            SetSelectedModelStatus($"Статус модели: {status}");
            SetStatus($"Проверка модели завершена: {status}.");
            await RefreshModelListAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось проверить модель: {ex.Message}");
        }
    }

    private void OpenModelsFolder()
    {
        if (_modelManager is null)
        {
            SetStatus("Папка моделей недоступна: сервис моделей не инициализирован.");
            return;
        }

        try
        {
            Directory.CreateDirectory(_modelManager.ModelsRoot);
            Process.Start(new ProcessStartInfo(_modelManager.ModelsRoot) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось открыть папку моделей: {ex.Message}");
        }
    }

    private async Task PickFileForTranscriptionAsync(CancellationToken cancellationToken)
    {
        if (_mainWindow is null)
        {
            return;
        }

        if (!_mainWindow.TryGetCurrentSettings(out var settings))
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Audio and video|*.wav;*.mp3;*.m4a;*.flac;*.ogg;*.mp4;*.mkv|All files|*.*"
        };

        if (dialog.ShowDialog(_mainWindow) != true)
        {
            return;
        }

        try
        {
            SetCurrentSettings(settings);
            var outputDirectory = ResolveTranscriptionOutputDirectory(dialog.FileName, settings.Transcription);
            var diarizationModelId = settings.Transcription.EnableDiarization
                ? settings.Transcription.SelectedDiarizationModelId
                : null;

            if (_transcriptionQueue is null)
            {
                SetStatus("Очередь транскрибации не инициализирована.");
                return;
            }

            await _transcriptionQueue.EnqueueAsync(
                dialog.FileName,
                outputDirectory,
                settings.Transcription.SelectedAsrModelId,
                diarizationModelId,
                cancellationToken);
            RefreshTranscriptionJobs();
            SetStatus("Файл добавлен в очередь транскрибации.");
            _ = RunNextTranscriptionJobAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось поставить файл в очередь: {ex.Message}");
        }
    }

    private async Task EnqueueRecordingForTranscriptionAsync(
        RecordingSession session,
        CancellationToken cancellationToken)
    {
        var transcriptionSettings = GetCurrentTranscriptionSettings();
        if (!transcriptionSettings.AutoTranscribeAfterRecording)
        {
            return;
        }

        if (_transcriptionQueue is null)
        {
            SetStatus("Очередь транскрибации не инициализирована.");
            return;
        }

        try
        {
            var enqueued = await RecordingTranscriptionEnqueuer.EnqueueAsync(
                session,
                transcriptionSettings,
                ResolveTranscriptionOutputDirectory,
                _transcriptionQueue.EnqueueAsync,
                cancellationToken);
            if (!enqueued)
            {
                return;
            }

            RefreshTranscriptionJobs();
            _ = RunNextTranscriptionJobAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось поставить запись в очередь транскрибации: {ex.Message}");
        }
    }

    private async Task RunNextTranscriptionJobAsync(CancellationToken cancellationToken)
    {
        if (_transcriptionQueue is null)
        {
            return;
        }

        try
        {
            await _transcriptionQueue.RunNextAsync(cancellationToken);
            RefreshTranscriptionJobs();
            var lastFinishedJob = _transcriptionQueue.Jobs
                .Where(job => job.FinishedAt is not null)
                .OrderByDescending(job => job.FinishedAt)
                .FirstOrDefault();
            if (lastFinishedJob is not null)
            {
                _transcriptionNotificationService?.ShowFinished(lastFinishedJob);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetStatus($"Не удалось выполнить задачу транскрибации: {ex.Message}");
            RefreshTranscriptionJobs();
        }
    }

    private bool TryGetSelectedModel(out ModelCatalogEntry model)
    {
        model = new ModelCatalogEntry();
        if (_modelCatalog is null || _mainWindow is null)
        {
            SetStatus("Каталог моделей не загружен.");
            return false;
        }

        var modelId = _mainWindow.SelectedModelId;
        if (string.IsNullOrWhiteSpace(modelId))
        {
            SetStatus("Выберите модель.");
            return false;
        }

        try
        {
            model = _modelCatalog.GetRequired(modelId);
            return true;
        }
        catch (Exception ex)
        {
            SetStatus($"Модель не найдена: {ex.Message}");
            return false;
        }
    }

    private void RefreshTranscriptionJobs()
    {
        if (_mainWindow is null || _transcriptionQueue is null)
        {
            return;
        }

        Dispatcher.BeginInvoke(() => _mainWindow.SetTranscriptionJobs(_transcriptionQueue.Jobs));
    }

    private TranscriptionPipeline CreateTranscriptionPipeline(TranscriptionSettings settings)
    {
        if (_modelCatalog is null || _modelManager is null)
        {
            throw new InvalidOperationException("Transcription services are not initialized.");
        }

        var gigaAmWorkerPath = GetAppDataPath("Workers", "GigaAM", "worker.exe");
        var engines = new Dictionary<string, ITranscriptionEngine>(StringComparer.OrdinalIgnoreCase)
        {
            ["sherpa-onnx"] = new SherpaOnnxTranscriptionEngine(),
            ["gigaam-v3"] = new GigaAmV3TranscriptionEngine(gigaAmWorkerPath, new GigaAmWorkerClient())
        };

        return new TranscriptionPipeline(
            _modelCatalog,
            _modelManager,
            new AudioNormalizer(GetAppDataPath("Temp")),
            engines,
            new DiarizationEngine(),
            new TranscriptExporter(),
            settings);
    }

    private TranscriptionSettings GetCurrentTranscriptionSettings()
    {
        lock (_settingsGate)
        {
            return _settings.Transcription;
        }
    }

    private void SetCurrentSettings(AppSettings settings)
    {
        lock (_settingsGate)
        {
            _settings = settings;
        }
    }

    private void SetSelectedModelStatus(string status)
    {
        if (_mainWindow is null)
        {
            return;
        }

        Dispatcher.BeginInvoke(() => _mainWindow.SetSelectedModelStatus(status));
    }

    private void SetModelDownloadProgress(int percent)
    {
        if (_mainWindow is null)
        {
            return;
        }

        Dispatcher.BeginInvoke(() => _mainWindow.SetModelDownloadProgress(percent));
    }

    private static string ResolveModelCatalogPath()
    {
        var baseDirectoryPath = Path.Combine(AppContext.BaseDirectory, "models", "catalog.json");
        if (File.Exists(baseDirectoryPath))
        {
            return baseDirectoryPath;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "models", "catalog.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return baseDirectoryPath;
    }

    private static string ResolveTranscriptionOutputDirectory(string inputFilePath, TranscriptionSettings settings)
    {
        return settings.OutputFolderMode switch
        {
            TranscriptOutputFolderMode.SameAsRecording => Path.GetDirectoryName(inputFilePath)
                ?? throw new InvalidOperationException("Не удалось определить папку исходного файла."),
            TranscriptOutputFolderMode.CustomFolder => string.IsNullOrWhiteSpace(settings.CustomOutputFolder)
                ? throw new InvalidOperationException("Укажите папку для сохранения транскриптов.")
                : settings.CustomOutputFolder,
            _ => throw new InvalidOperationException($"Неизвестный режим папки транскриптов: {settings.OutputFolderMode}.")
        };
    }

    private static string GetSettingsPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Autorecord",
            "settings.json");
    }

    private static string GetAppDataPath(params string[] parts)
    {
        return Path.Combine(
            [Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Autorecord", .. parts]);
    }

    private sealed class CurrentSettingsTranscriptionPipeline(
        Func<TranscriptionSettings> getSettings,
        Func<TranscriptionSettings, ITranscriptionPipeline> createPipeline)
        : ITranscriptionPipeline
    {
        public Task<TranscriptionPipelineResult> RunAsync(
            TranscriptionJob job,
            IProgress<int> progress,
            CancellationToken cancellationToken)
        {
            var pipeline = createPipeline(getSettings());
            return pipeline.RunAsync(job, progress, cancellationToken);
        }
    }
}
