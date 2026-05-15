# Проект

- Название: autorecord.
- Цель: Windows-приложение для автоматической записи микрофона и системного звука во время встреч из календаря, который отдает события по iCal/.ics-ссылке.
- Стек: .NET WPF, NAudio, Ical.Net, iCal/.ics-ссылка календаря, Windows Task Scheduler с fallback через HKCU Run.
- Текущая структура:
  - `README.md` — описание проекта для GitHub.
  - `context/` — проектный контекст.
  - `docs/superpowers/specs/2026-05-06-autorecord-design.md` — утвержденная дизайн-спека MVP.
  - `docs/huggingface-token-pyannote.md` — пользовательская инструкция с картинками по получению Hugging Face token для Pyannote Community-1.
  - `.worktrees/mvp/docs/superpowers/specs/2026-05-07-autorecord-v2-transcription-design.md` — утвержденная дизайн-спека v2 локальной транскрибации.
  - `.worktrees/mvp/docs/superpowers/specs/2026-05-08-model-install-flow-design.md` — design addendum по установке моделей из GUI.
  - `.worktrees/mvp/docs/superpowers/plans/2026-05-07-autorecord-v2-transcription.md` — implementation plan v2 локальной транскрибации по фазам A-G.
  - `docs/superpowers/plans/2026-05-06-autorecord-mvp.md` — план реализации MVP.
  - `.worktrees/mvp/` — рабочее дерево реализации на ветке `feature/mvp`.
  - `.worktrees/mvp/src/Autorecord.Core/` — core-логика календаря, расписания, записи, настроек и автозапуска.
  - `.worktrees/mvp/src/Autorecord.App/` — WPF GUI, tray, уведомления и app host.
  - `.worktrees/mvp/tests/Autorecord.Core.Tests/` — автоматические тесты core-логики.
  - `.worktrees/mvp/scripts/publish.ps1` — публикация приложения в папку с запускаемым `.exe`.
  - `.worktrees/mvp/tools/gigaam-worker/` — исходник, зависимости и build-script для внешнего локального GigaAM worker artifact; artifact собирается в `.worktrees/mvp/artifacts/vendor/gigaam-worker`.
  - `.worktrees/mvp/tools/pyannote-community-worker/` — исходник, зависимости и build-script для внешнего локального Pyannote Community-1 worker artifact; worker читает normalized WAV через Python `wave`, artifact собирается в `.worktrees/mvp/artifacts/vendor/pyannote-community-worker`.
  - `.worktrees/mvp/artifacts/publish/Autorecord/` — актуальная локальная publish-папка с `Autorecord.App.exe` для запуска двойным кликом.
  - `.worktrees/mvp/artifacts/publish/Autorecord-pyannote-audio-fix/` — предыдущая publish-папка с исправленным Pyannote Community-1 worker.
  - `Autorecord.lnk` — ярлык в корне проекта на актуальную publish-сборку `.worktrees/mvp/artifacts/publish/Autorecord/Autorecord.App.exe`.
  - `.worktrees/mvp/artifacts/publish/Autorecord-preroll/` — последняя тестовая publish-сборка с pre-roll и настройкой готовности микрофона.
  - `.worktrees/mvp/artifacts/publish/Autorecord-mp3/` — последняя тестовая publish-сборка с MP3-выходом.
  - `.worktrees/mvp/artifacts/publish/Autorecord-mp3-no-preroll/` — последняя тестовая publish-сборка с MP3-выходом без звука до старта записи.
  - `.worktrees/mvp/artifacts/publish/Autorecord-mp3-safe-save/` — последняя publish-сборка с фоновым надежным MP3-сохранением.
- Важные компоненты:
  - `IAudioRecorder.PrepareAsync` — подготовка аудиоустройств до начала записи.
  - `IAudioRecorder.FileSaved` / `FileSaveFailed` — события завершения фонового сохранения или ошибки MP3-конвертации.
  - `NaudioWavRecorder` — WASAPI-захват микрофона и всех активных render loopback devices, подготовка аудиоустройств, wall-clock aligned микширование во временный WAV, фоновая MP3-конвертация и восстановление незавершенных WAV.
  - `RecordingCoordinator.ApplySettingsAsync` — применяет настройку готовности микрофона и управляет жизненным циклом подготовленного recorder-а.
  - `RecordingCoordinator.RecordingSaved` / `RecordingSaveFailed` — события финального сохранения или ошибки, которые WPF-слой показывает в статусе и уведомлениях.
  - `StopRecordingDialog` / `StopRecordingPrompt` — диалог остановки после молчания; если пользователь не выбирает `Да/Нет` 2 минуты, timeout останавливает запись через обычный save path.
  - `RecordingTranscriptionEnqueuer` — ставит сохраненную запись в очередь транскрибации при включенной автотранскрибации, с учетом отдельной папки транскриптов и optional diarization.
  - `RecordingFileNamer` — формирует имена итоговых `.mp3` файлов в формате `дд.мм.гггг чч.мм` и учитывает временные файлы, чтобы не перезаписать незавершенную запись.
  - `StartupManager` — управляет автозапуском: сначала пробует Task Scheduler, при `Access denied` включает fallback в `HKCU\...\Run`, при отключении чистит оба механизма.
- Планируемые компоненты v2:
  - `TranscriptionSettings` — настройки автотранскрибации, моделей, диаризации, папки транскриптов и форматов вывода. Базовые типы и валидация реализованы.
  - `ModelCatalog` / `ModelManager` / `ModelDownloadService` / `ModelInstallService` — расширяемый каталог моделей, скачивание только по действию пользователя в GUI, progress/speed/cancel UI, disk-space precheck, Hugging Face snapshot download для gated моделей через token-диалог, локальная установка file/directory artifacts, `requiredFiles` validation и `manifest.json`. Реализованы.
  - `TranscriptionQueue` / `TranscriptionJobRepository` / `TranscriptionJobLogWriter` — локальная очередь, история заданий с восстановлением после перезапуска и локальные best-effort job-логи. Repository, долгоживущая очередь, `JobsChanged`, real pipeline wiring, действия `retry/cancel/delete`, `WaitingForModel` для отсутствующих моделей, автоподхват restored `Pending` jobs и `%LOCALAPPDATA%\Autorecord\Logs\transcription-job-{id}.log` реализованы.
  - `AudioNormalizer` / `TranscriptionPipeline` / `LocalNetworkGuard` — нормализация app-created `.mp3`/`.wav` в 16 kHz mono PCM WAV, проверка моделей, защита runtime от сетевых API, запуск ASR/диаризации, сборка и экспорт результата. Реализованы.
  - `ITranscriptionEngine`, `ISegmentedTranscriptionEngine`, `SherpaOnnxTranscriptionEngine`, `GigaAmV3TranscriptionEngine` — локальные ASR-движки без облачных API. При включенной диаризации pipeline передает ASR интервалы speaker turns; Sherpa транскрибирует заданные интервалы локально, GigaAM получает их через `--chunks-json` в worker. Без диаризации остается обычная full-file chunked ASR обработка.
  - `DiarizationEngine` / `PyannoteCommunityDiarizationEngine` / `TranscriptAssembler` / `TranscriptExporter` — разделение по спикерам, сборка сегментов и экспорт `.txt`, `.md`, `.srt`, `.json`. Sherpa `DiarizationEngine` читает normalized WAV chunks по 30 минут с 5 сек padding и маппит speaker ids между chunks по реальному overlap; Pyannote Community-1 подключен через отдельный локальный worker, выбирается по `engine` из catalog и читает normalized WAV без `torchaudio.load`; `TranscriptAssembler` удаляет точный ASR boundary-overlap при merge соседних сегментов одного speaker; пустой результат ASR экспортируется как no-speech transcript (`Речь не обнаружена.`) без failed job.
  - WPF-вкладка `Транскрибация` — подключена к catalog/model actions/manual file enqueue; автопостановка после `RecordingSaved` реализована и включена по умолчанию для старых настроек без секции `Transcription`. GUI-настройки включают ASR/diarization выбор из catalog, speaker count, отдельную папку транскриптов и форматы `TXT/Markdown/SRT/JSON`; model download показывает progress/speed/downloaded/total и поддерживает отмену; `Удалить выбранные`/`Проверить выбранные` работают по ASR + включенной diarization-модели; история заданий поддерживает open transcript/open folder/retry/cancel/delete и показывает отдельный столбец `Модель диаризации`.
  - `ModelDownloadPlan` — планирует скачивание только отсутствующих выбранных моделей: уже установленная ASR не скачивается повторно, если пользователю нужна только missing diarization-модель.
- Последняя GUI end-to-end проверка v2:
  - Опубликованное WPF-приложение записало MP3, автоматически поставило его в очередь после сохранения и создало `.txt`, `.md`, `.srt`, `.json` в отдельной папке транскриптов.
  - Проверенные модели: `gigaam-v3-ru-quality` + `sherpa-diarization-pyannote-fast`.
  - Проверенный single-source job: `a2f1bd48-3aed-4f5e-a778-96c361e97d66`, status `Completed`.
  - Проверенный synthetic multi-speaker job: `68ce8407-bc22-4a54-8820-5e37b7da58e4`, status `Completed`, JSON содержит `Speaker 1` и `Speaker 2`.
- Текущий checkpoint:
  - Ветка `.worktrees/mvp`: `feature/mvp`.
  - Последний commit: `d9957ee fix: keep recorder aligned with wall clock`.
  - Worktree `.worktrees/mvp` сейчас содержит незакоммиченные изменения Pyannote Community-1 / HF-download / GUI-столбца / turn-aware ASR pipeline.
  - Последние проверки: regression test на turn-aware ASR сначала падал на full-file ASR call; затем `dotnet test Autorecord.sln --no-restore` — 353/353 passed; `dotnet build Autorecord.sln -c Release --no-restore` — 0 warnings, 0 errors; `scripts/publish.ps1` прошел; publish GigaAM worker показывает `--chunks-json`.
  - Актуальная publish-папка: `.worktrees/mvp/artifacts/publish/Autorecord`.

## Sync 2026-05-14

- Архитектура записи в `.worktrees/mvp`:
  - основной файл записи: combined MP3 `*.mp3`;
  - техническая дорожка микрофона: `*.mic.wav`;
  - техническая дорожка системного звука: `*.system.wav`;
  - автотранскрибация пока использует основной combined MP3 и текущую выбранную ASR-модель.

## Sync 2026-05-12

- Актуальная релизная конфигурация v2:
  - Единственная ASR-модель в GUI/catalog: `gigaam-v3-ru-quality`.
  - Единственная diarization-модель в GUI/catalog: `pyannote-community-1`.
  - Выбор моделей пользователем убран; `TranscriptionSettings` нормализуются к фиксированной паре моделей и включенной диаризации.
  - Default output folder для новых настроек: `Documents\Autorecord`.
  - `SpeakerCountDialog` добавлен в WPF: выбор `Auto/1..6` при остановке записи; timeout 2 минуты продолжает с `Auto`.
  - Вкладка `Транскрибация` показывает текущую задачу с progress/actions вместо history grid; status/download actions для фиксированных моделей скрываются, когда обе модели установлены.
  - Очередь транскрибации продолжает использовать `TranscriptionQueue`, но UI/cleanup ориентированы на актуальную задачу, а не на хранение истории.
- Актуальный checkpoint:
  - Worktree `.worktrees/mvp` содержит незакоммиченные изменения Pyannote Community-1 / HF-download / turn-aware ASR / release UX.
  - Последние проверки: `dotnet test Autorecord.sln --no-restore` — 359/359 passed; `dotnet build Autorecord.sln -c Release --no-restore` — 0 warnings, 0 errors; `scripts/publish.ps1` прошел.
  - `Autorecord.lnk` указывает на `.worktrees/mvp/artifacts/publish/Autorecord/Autorecord.App.exe`.

## Sync 2026-05-14

- Корневой репозиторий `C:\Projects\autorecord` находится на ветке `master`.
- Текущий корневой checkpoint: `a97cd2c initial stable version`.
- `.gitignore` настроен для локальных worktrees, .NET/Python build artifacts, publish/temp output, IDE/user state, секретов и `*.lnk`.
- `Autorecord.lnk` и `.worktrees/` остаются локальными игнорируемыми артефактами; документация в `docs/` отслеживается git.

## Sync 2026-05-14

- Актуальные локальные артефакты `.worktrees/mvp`:
  - publish exe: `C:\Projects\autorecord\.worktrees\mvp\artifacts\publish\Autorecord\Autorecord.App.exe`;
  - setup exe: `C:\Projects\autorecord\.worktrees\mvp\artifacts\installer\Autorecord-Setup-WithModels.exe`;
  - installer payload пересобран после добавления технических дорожек и проверен по marker/required entries.
