# Сессия

- Текущее состояние: v2 находится в релизном hardening на worktree `C:\Projects\autorecord\.worktrees\mvp`.
- Что уже есть:
  - WPF-приложение записи встреч, автозапуска/календаря и локальной транскрибации.
  - Запись сохраняется по умолчанию в `Documents\Autorecord`.
  - Публичная транскрибация использует ASR `gigaam-v3-ru-quality` и диаризацию `pyannote-community-1`.
  - `pyannote-community-1` не входит в installer и скачивается пользователем с Hugging Face через token.
  - После остановки записи пользователь выбирает количество спикеров (`Auto`, `1..6`); через 2 минуты без выбора используется `Auto`.
  - Можно вручную выбрать аудиофайл и запустить транскрибацию/диаризацию.
  - История транскрибаций скрыта/не нужна; UI показывает текущий прогресс.
- Последнее изменение:
  - Installer переделан в GUI wizard без чёрного console window:
    - лицензионное соглашение в виде документа;
    - выбор папки установки;
    - экран установки с progress bar;
    - финальный экран с чекбоксом `Открыть Autorecord`.
  - Вкладка `Транскрибация` упрощена:
    - скрыт выбор ASR, вместо него показывается `Модель транскрибации: GigaAM v3`;
    - скрыты строки диаризации/status моделей;
    - скрыты служебные кнопки удаления/проверки/папки моделей;
    - прогресс/статус скачивания показываются только во время активного скачивания;
    - по умолчанию включён только формат `TXT`.
  - Вкладка `О программе` получила кликабельные ссылки.
  - Диалог Hugging Face token обновлён по реальным PNG `hf1.png`–`hf8.png` и кликабельным ссылкам.
  - Publish и public installer пересобраны.
- Предыдущее изменение:
  - Собран самораспаковывающийся installer с релизными моделями: `C:\Projects\autorecord\.worktrees\mvp\artifacts\installer\Autorecord-Setup-WithModels.exe`.
  - Размер installer: `857.4 MiB`.
  - В installer входят приложение из текущего publish и модель `gigaam-v3-ru-quality`; `pyannote-community-1` скачивается отдельно пользователем.
  - Public shortcut: `C:\Projects\autorecord\Autorecord-Public-Setup.lnk`.
  - Добавлена повторяемая сборка installer через `scripts\package-installer.ps1` и `tools\installer\AutorecordInstaller.cs`.
- Более раннее изменение:
  - Из карточки текущей транскрибации удалены progress bar и строка `Прогресс: N%`.
  - Остался только список этапов: чтение файла, диаризация, транскрибация, сохранение транскрипта.
  - Этапы показывают состояния `ожидает`, `выполняется`, `готово`, `ошибка`, `отменено`.
  - Publish-версия пересобрана: `C:\Projects\autorecord\.worktrees\mvp\artifacts\publish\Autorecord\Autorecord.App.exe`.
  - Корневой ярлык `C:\Projects\autorecord\Autorecord.lnk` ведёт на свежий publish exe.
- Проверка:
  - `C:\Users\User\.dotnet\dotnet.exe test Autorecord.sln --no-restore` — 391 tests passed.
  - `C:\Users\User\.dotnet\dotnet.exe build Autorecord.sln -c Release --no-restore` — без предупреждений и ошибок.
  - `scripts\publish.ps1` успешно пересобрал self-contained win-x64 build.
  - `scripts\package-installer.ps1 -SkipPublish` успешно пересобрал installer.
  - Installer stub проверен как `Windows GUI` subsystem.
  - Installer payload проверен: внутри есть `Autorecord.App.exe` и bundled модель `gigaam-v3-ru-quality`; Parakeet и `pyannote-community-1` отсутствуют.
  - `C:\Projects\autorecord\Autorecord-Public-Setup.lnk` указывает на актуальный installer.
- С чего продолжать:
  - Проверить новый GUI installer на чистой/тестовой установке.
  - Проверить скачивание `pyannote-community-1` через Hugging Face token.
  - Запустить через `C:\Projects\autorecord\Autorecord.lnk` и проверить, что карточка показывает только этапы без линии прогресса.
  - Также проверить retry аварийного `.recording.wav`, если эта задача ещё не обработана.

## Sync 2026-05-14

- Последнее изменение:
  - Recorder теперь сохраняет отдельные технические WAV-дорожки рядом с итоговым MP3:
    - `*.mic.wav` — микрофонные input sources;
    - `*.system.wav` — системные render loopback sources;
    - итоговый `*.mp3` остается combined mix.
  - Во время записи используются временные файлы `*.recording.wav`, `*.mic.recording.wav`, `*.system.recording.wav`, затем они финализируются рядом с MP3.
  - `AudioFileSavedEventArgs` передает optional пути `MicrophoneTrackPath` и `SystemTrackPath`.
  - Транскрипционный pipeline пока не переключался на эти дорожки; автотранскрибация продолжает брать saved combined MP3 и `GigaAM v3`.
- Проверка:
  - `C:\Users\User\.dotnet\dotnet.exe test Autorecord.sln --no-restore` — 381 tests passed.
  - Targeted `NaudioWavRecorderTests` — 21 tests passed.
- Ограничение:
  - `scripts\publish.ps1` не завершился, потому что запущенный `Autorecord.App` из publish-папки блокирует DLL:
    - PID `21728`;
    - `C:\Projects\autorecord\.worktrees\mvp\artifacts\publish\Autorecord\Autorecord.App.exe`.
  - Принудительно процесс не закрывался, чтобы не оборвать возможную запись.
- С чего продолжать:
  - Закрыть запущенный Autorecord через tray/окно.
  - Повторить `powershell -ExecutionPolicy Bypass -File scripts\publish.ps1`.
  - Сделать короткую тестовую запись и проверить наличие `*.mp3`, `*.mic.wav`, `*.system.wav`.

## Sync 2026-05-14

- Текущее состояние после закрытия приложения:
  - `scripts\publish.ps1` успешно пересобрал publish:
    - `C:\Projects\autorecord\.worktrees\mvp\artifacts\publish\Autorecord\Autorecord.App.exe`;
    - GigaAM worker и Pyannote Community worker скопированы в publish.
  - `C:\Projects\autorecord\Autorecord.lnk` указывает на актуальный publish exe.
  - Publish exe smoke-запущен с `--minimized`; процесс стартовал, затем был завершен после проверки.
  - Installer пересобран на свежем publish, затем заменен public release сборкой без `pyannote-community-1`:
    - `C:\Projects\autorecord\.worktrees\mvp\artifacts\installer\Autorecord-Setup-WithModels.exe`;
    - актуальный размер `857.4 MiB`;
    - payload marker `AUTORECORD_PAYLOAD_V1` корректный;
    - payload содержит app, workers и bundled модель `gigaam-v3-ru-quality`.
  - `C:\Projects\autorecord\Autorecord-Public-Setup.lnk` указывает на актуальный setup exe.
- Проверка:
  - Targeted `NaudioWavRecorderTests` — 21/21 passed.
  - Реальная запись из GUI не запускалась автоматически, чтобы не создавать пользовательскую аудиозапись без контроля.
- С чего продолжать:
  - Запустить `C:\Projects\autorecord\Autorecord.lnk`.
  - Сделать короткую запись и проверить рядом с MP3 файлы `*.mic.wav` и `*.system.wav`.
  - Проверить скачивание `pyannote-community-1` через Hugging Face token и автотранскрибацию с GigaAM.

## Sync 2026-05-15

- Последнее изменение:
  - Вкладка `Запись` перестроена в framed-блоки:
    - календарный блок: checkbox автостарта по событию, iCal-ссылка, обновление календаря, фильтр по метке;
    - блок статуса записи: кнопки старта/остановки заменены XAML-иконками из `Resources/Buttons`;
    - блок автоостановки: checkbox включения автоостановки, настройки тишины, повтора после `Нет` и таймаута бездействия;
    - добавлен checkbox отключения всплывающих уведомлений.
  - Добавлены настройки:
    - `AutoStartRecordingFromCalendar`;
    - `AutoStopRecordingOnSilence`;
    - `NoAnswerStopPromptMinutes`;
    - `NotificationsEnabled`.
  - Календарный автостарт теперь не запускает запись, если выключен пользователем.
  - Автоостановка по тишине не создаёт stop policy, если выключена пользователем.
  - Диалог остановки использует настраиваемый таймаут бездействия вместо фиксированных 2 минут.
  - Tray/desktop notifications не показываются, если пользователь отключил уведомления.
- Проверка:
  - Targeted tests:
    - `SettingsStoreTests|RecordingCoordinatorTests|ScheduleMonitorTests|StopRecordingPromptTests|MainWindowTranscriptionSettingsTests` — 68/68 passed.
  - `C:\Users\User\.dotnet\dotnet.exe build Autorecord.sln --no-restore` — без предупреждений и ошибок.
  - `C:\Users\User\.dotnet\dotnet.exe test Autorecord.sln --no-restore` — 395/395 passed.
- С чего продолжать:
  - Визуально проверить главный экран в запущенном приложении.
  - Проверить вручную:
    - выключенный автостарт календаря не стартует запись;
    - выключенная автоостановка не показывает запрос после тишины;
    - выключенные уведомления не показывают всплывающие сообщения.

## Sync 2026-05-15

- Последнее изменение:
  - Исправлен экран лицензии public installer wizard:
    - страница wizard теперь получает реальный начальный размер до добавления anchored-контролов;
    - RichTextBox с лицензией больше не уезжает за правую границу;
    - checkbox `Я согласен с условиями лицензионного соглашения` снова виден;
    - кнопка `Далее >` остается disabled до согласия.
  - Добавлен regression test `InstallerWizardPagesHaveRealInitialSizeBeforeAnchoredControlsAreAdded`.
  - Public installer пересобран:
    - `C:\Projects\autorecord\.worktrees\mvp\artifacts\installer\Autorecord-Setup-WithModels.exe`;
    - размер `857.4 MiB`;
    - `C:\Projects\autorecord\Autorecord-Public-Setup.lnk` указывает на этот setup.
- Проверка:
  - `PublicReleaseInstallerTests` — 4/4 passed.
  - `C:\Users\User\.dotnet\dotnet.exe build Autorecord.sln --no-restore` — без предупреждений и ошибок.
  - `C:\Users\User\.dotnet\dotnet.exe test Autorecord.sln --no-restore` — 396/396 passed.
- С чего продолжать:
  - Повторно открыть public installer и визуально проверить экран лицензии.
  - Затем продолжить проверку установки и скачивания `pyannote-community-1`.

## Sync 2026-05-15

- Последнее изменение:
  - Исправлена установка в `C:\Program Files`:
    - выбор самой папки `C:\Program Files` нормализуется в `C:\Program Files\Autorecord`;
    - для защищённых путей installer перезапускается через UAC (`runas`);
    - опасные корни вроде `C:\` и системные папки Windows запрещены.
  - Добавлен regression test `InstallerNormalizesProgramFilesAndRelaunchesElevatedForProtectedInstallRoot`.
  - Старый `Autorecord-Setup-WithModels.exe` был заблокирован открытым installer window, поэтому исправленный setup собран отдельным файлом:
    - `C:\Projects\autorecord\.worktrees\mvp\artifacts\installer\Autorecord-Setup-WithModels-Fixed.exe`.
  - `C:\Projects\autorecord\Autorecord-Public-Setup.lnk` переключён на `Autorecord-Setup-WithModels-Fixed.exe`.
- Проверка:
  - `PublicReleaseInstallerTests` — 5/5 passed.
  - `C:\Users\User\.dotnet\dotnet.exe build Autorecord.sln --no-restore` — без предупреждений и ошибок.
  - `C:\Users\User\.dotnet\dotnet.exe test Autorecord.sln --no-restore` — 397/397 passed.
- С чего продолжать:
  - Закрыть старое окно installer.
  - Запустить `C:\Projects\autorecord\Autorecord-Public-Setup.lnk`.
  - Проверить установку в `C:\Program Files`: должен появиться UAC, установка должна идти в `C:\Program Files\Autorecord`.

## Sync 2026-05-15

- Последнее изменение:
  - Решено оставить public installer одним self-contained `.exe`; вариант `exe + payload.zip` отменён.
  - Причина: пользователю нельзя требовать скачивать/хранить два файла для установки, даже если большой `.exe` стартует на несколько секунд дольше.
  - Доработано отображение папки установки:
    - после выбора `C:\Program Files` через `Обзор...` поле сразу показывает `C:\Program Files\Autorecord`;
    - при ручном вводе путь нормализуется при уходе из поля и перед установкой;
    - UAC для protected install root сохранён.
  - Собран новый единый setup:
    - `C:\Projects\autorecord\.worktrees\mvp\artifacts\installer\Autorecord-Setup-WithModels-ProgramFilesFix.exe`;
    - размер `857.4 MiB`;
    - `C:\Projects\autorecord\Autorecord-Public-Setup.lnk` переключён на него.
- Проверка:
  - `PublicReleaseInstallerTests` — 5/5 passed.
  - `C:\Users\User\.dotnet\dotnet.exe build Autorecord.sln --no-restore` — без предупреждений и ошибок.
  - `C:\Users\User\.dotnet\dotnet.exe test Autorecord.sln --no-restore` — 397/397 passed.
- С чего продолжать:
  - Запустить `C:\Projects\autorecord\Autorecord-Public-Setup.lnk`.
  - На странице выбора папки выбрать `C:\Program Files` и проверить, что поле сразу меняется на `C:\Program Files\Autorecord`.
  - Продолжить установку и проверить UAC.

## Sync 2026-05-15

- Последнее изменение:
  - Исправлены последние public-release замечания:
    - при неверном Hugging Face token показывается `Ошибка - неверный токен`;
    - заменён `src\Autorecord.App\Assets\HuggingFace\hf8.png` на новую версию из `artifacts\hf-dialog-preview\Инструкция для скачивания с hugginface\hf8.png`;
    - убрана лишняя надпись `Модель установлена и готова...` после установки модели;
    - статус ошибки скачивания остаётся видимым после завершения попытки.
  - Подтверждено: вкладка `Запись` уже исправлена в source; проблема была в старом publish внутри installer payload.
  - `scripts\publish.ps1` пересобрал свежий publish перед упаковкой installer.
  - Собран новый единый installer из свежего publish:
    - `C:\Projects\autorecord\.worktrees\mvp\artifacts\installer\Autorecord-Setup-WithModels-ReleaseFix.exe`;
    - `C:\Projects\autorecord\Autorecord-Public-Setup.lnk` переключён на этот installer.
  - Окно `Установка приложения Autorecord сейчас начнется` не добавлено.
    - Причина: задержка большого self-contained `.exe` происходит до выполнения кода installer, поэтому такое окно не решит паузу перед стартом.
- Проверка:
  - Targeted tests `UserFacingErrorMessagesTests|MainWindowTranscriptionSettingsTests|HuggingFaceTokenDialogTests` — 28/28 passed.
  - `scripts\publish.ps1` — успешно.
  - `scripts\package-installer.ps1 -SkipPublish` — успешно.
  - `C:\Users\User\.dotnet\dotnet.exe build Autorecord.sln --no-restore` — без предупреждений и ошибок.
  - `C:\Users\User\.dotnet\dotnet.exe test Autorecord.sln --no-restore` — 397/397 passed.
- С чего продолжать:
  - Установить через `C:\Projects\autorecord\Autorecord-Public-Setup.lnk`.
  - Проверить, что установленная версия показывает обновлённую вкладку `Запись`.
  - Проверить скачивание `pyannote-community-1` с неверным token и увидеть `Ошибка - неверный токен`.

## Sync 2026-05-15

- Последнее изменение:
  - Исправлен стартовый размер главного окна:
    - окно открывается `760x720`;
    - минимальный размер `640x520`;
    - изменение размера оставлено включённым.
  - Вкладка `Запись` обёрнута в вертикальный `ScrollViewer`, чтобы настройки не обрезались при уменьшении окна.
  - Добавлен regression test `MainWindowUsesLargerResizableDefaultSizeAndScrollableRecordingTab`.
  - Пересобран fresh publish и новый installer:
    - `C:\Projects\autorecord\.worktrees\mvp\artifacts\installer\Autorecord-Setup-WithModels-WindowSizeFix.exe`;
    - `C:\Projects\autorecord\Autorecord-Public-Setup.lnk` переключён на этот installer.
- Проверка:
  - `MainWindowTranscriptionSettingsTests` — 22/22 passed.
  - `scripts\publish.ps1` — успешно.
  - `scripts\package-installer.ps1 -SkipPublish` — успешно.
  - `C:\Users\User\.dotnet\dotnet.exe build Autorecord.sln --no-restore` — без предупреждений и ошибок.
  - `C:\Users\User\.dotnet\dotnet.exe test Autorecord.sln --no-restore` — 398/398 passed.
- С чего продолжать:
  - Установить через `C:\Projects\autorecord\Autorecord-Public-Setup.lnk`.
  - Проверить, что главное окно открывается достаточно высоким и вкладка `Запись` скроллится при уменьшении.
  - Затем продолжить проверку неверного Hugging Face token и скачивания `pyannote-community-1`.

## Sync 2026-05-15

- Последнее изменение:
  - На вкладку `О программе` добавлена нижняя строка: `Предложения и пожелания можно отправить на zitramonio@proton.me`.
  - Email сделан кликабельной `mailto:`-ссылкой через существующий `OpenLink_RequestNavigate`.
  - Обновлён regression test `AboutTabContainsModelLicensesAndRecordingNotice`.
  - Fresh publish пересобран.
  - Для упаковки installer восстановлена локальная bundled-модель `gigaam-v3-ru-quality` из предыдущей staging-папки.
  - Собран новый единый installer:
    - `C:\Projects\autorecord\.worktrees\mvp\artifacts\installer\Autorecord-Setup-WithModels-AboutContact.exe`;
    - `C:\Projects\autorecord\Autorecord-Public-Setup.lnk` переключён на этот installer.
- Проверка:
  - `AboutTabContainsModelLicensesAndRecordingNotice` — passed.
  - `scripts\publish.ps1` — успешно.
  - `scripts\package-installer.ps1 -SkipPublish` — успешно после восстановления локальной GigaAM модели.
- С чего продолжать:
  - Установить через `C:\Projects\autorecord\Autorecord-Public-Setup.lnk`.
  - Проверить вкладку `О программе`: email должен быть внизу и открываться как `mailto:`.
  - Затем продолжить проверку неверного Hugging Face token и скачивания `pyannote-community-1`.
