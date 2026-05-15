# Autorecord: Compact Summary

## Цель проекта

`Autorecord` — локальное Windows-приложение для автоматической записи встреч и локальной расшифровки с диаризацией.

Цель: приложение записывает встречу, сохраняет аудио локально и автоматически создает транскрипт с разделением по спикерам без облачных API, телеметрии и ручной установки инструментов пользователем.

## Стек

- .NET 10 / WPF
- Windows-only
- NAudio для записи
- Локальные workers для ASR/диаризации
- GigaAM v3 для транскрибации
- pyannote Community-1 для диаризации
- Локальное хранение настроек, моделей и логов в `%LOCALAPPDATA%\Autorecord`

## Архитектура

- UI: WPF `MainWindow`
- Core services: запись, календарь, настройки, уведомления
- Transcription pipeline:
  - нормализация аудио
  - диаризация
  - транскрибация
  - экспорт transcript
- ASR и диаризация разделены архитектурно: GigaAM расшифровывает текст, pyannote назначает speaker turns.
- Модели хранятся локально, не в репозитории.
- Текущий релизный UX скрывает выбор моделей: используются фиксированные `gigaam-v3-ru-quality` + `pyannote-community-1`.

## Важные решения

- Никаких cloud ASR API: OpenAI, Google, Yandex, Azure, AWS, Deepgram и аналоги запрещены.
- Интернет разрешен только для явного скачивания моделей.
- После скачивания все должно работать offline.
- История транскрибаций скрыта; показывается только текущая транскрибация.
- Progress bar убран: вместо него показываются этапы `Чтение файла`, `Диаризация`, `Транскрибация`, `Сохранение транскрипта`.
- По умолчанию записи и транскрипты лежат в `Documents\Autorecord`.
- При остановке записи показывается диалог выбора количества спикеров, таймаут 2 минуты, потом запись уходит в транскрибацию.
- Parakeet пока не добавлять в основной релиз; рассматривать как follow-up экспериментальную ASR-модель.

## Что уже сделано

- Запись встреч работает.
- Автотранскрибация после записи подключена.
- Ручной выбор аудиофайла для транскрибации есть.
- GigaAM worker и pyannote Community-1 подключены.
- Добавлена защита от незавершенных `.recording.wav`: repair WAV header при аварийном завершении.
- Исправлено поведение отмены: cancelled job больше не висит как текущая.
- Исправлена работа с output devices: приложение не должно держать loopback/output device вне реальной записи.
- Добавлена иконка приложения и tray icon.
- Удален блок выбора/скачивания моделей из релизного UI.
- Удалена линия прогресса транскрибации.
- Последний известный тестовый прогон: `369 passed`.
- Ярлык запуска: `C:\Projects\autorecord\Autorecord.lnk`.

## Ключевые файлы

- `C:\Projects\autorecord\.worktrees\mvp\src\Autorecord.App\MainWindow.xaml`
- `C:\Projects\autorecord\.worktrees\mvp\src\Autorecord.App\MainWindow.xaml.cs`
- `C:\Projects\autorecord\.worktrees\mvp\src\Autorecord.App\Transcription\TranscriptionJobListItemViewModel.cs`
- `C:\Projects\autorecord\.worktrees\mvp\src\Autorecord.Core\Audio\NaudioWavRecorder.cs`
- `C:\Projects\autorecord\.worktrees\mvp\src\Autorecord.Core\Audio\AudioCaptureSessions.cs`
- `C:\Projects\autorecord\.worktrees\mvp\src\Autorecord.Core\Audio\WavFileRepair.cs`
- `C:\Projects\autorecord\.worktrees\mvp\src\Autorecord.Core\Transcription\Pipeline\AudioNormalizer.cs`
- `C:\Projects\autorecord\.worktrees\mvp\scripts\publish.ps1`

## Текущая задача

Подготовка к релизу и hardening.

Последний обсуждаемый follow-up: оценить возможность добавления Parakeet как экспериментальной ASR-модели, но не включать в основной релиз без тестов на реальных русских встречах.

## Что нельзя ломать

- Создание аудиозаписи встреч.
- Запись системного звука/наушников и микрофона.
- Безопасность output devices: не держать устройство вывода открытым без записи.
- Автотранскрибацию после сохранения записи.
- Ручную транскрибацию выбранного файла.
- Offline-принцип после скачивания моделей.
- Отсутствие облачных API, телеметрии, аналитики и отправки аудио/транскриптов наружу.
- Локальное хранение моделей вне репозитория.
- Релизный простой UX без выбора множества моделей.
