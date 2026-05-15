# Autorecord

Autorecord - Windows-приложение для автоматической записи встреч из календаря по iCal/.ics-ссылке. Приложение читает приватную или публичную ссылку календаря, отслеживает начало встреч, записывает микрофон и системный звук, сохраняет запись локально и может автоматически подготовить транскрипт с разделением по спикерам.

Проект ориентирован на личное использование: записи, технические аудиодорожки и транскрипты остаются на компьютере пользователя. Интернет используется для синхронизации календаря и явного скачивания моделей, но транскрибация и диаризация выполняются локально.

## Возможности

- Автоматический старт записи по событиям из любого календаря, который отдает события по iCal/.ics-ссылке.
- Ручной старт и остановка записи из WPF-интерфейса.
- Захват микрофона и системного звука через NAudio/WASAPI.
- Сохранение основной записи в MP3 и отдельных технических дорожек `*.mic.wav` / `*.system.wav`.
- Защищенное сохранение: временный WAV остается резервной копией до успешной MP3-конвертации.
- Запрос остановки после периода тишины, с возможностью продолжить запись.
- Автозапуск вместе с Windows через Task Scheduler или fallback через `HKCU Run`.
- Локальная транскрибация после записи:
  - ASR: GigaAM v3;
  - разделение по спикерам: Pyannote Community-1;
  - экспорт транскриптов в `TXT`, `Markdown`, `SRT` и `JSON`.
- Скачивание моделей из GUI. Hugging Face token для Pyannote запрашивается только на время скачивания и не сохраняется.

## Как это работает

1. Пользователь добавляет iCal/.ics-ссылку календаря и выбирает папку для записей.
2. Autorecord синхронизирует события и запускает запись в момент начала подходящей встречи.
3. Приложение пишет общий MP3-файл, а также технические дорожки микрофона и системного звука.
4. После остановки записи файл автоматически попадает в локальную очередь транскрибации.
5. Локальные модели создают транскрипт и, при включенной диаризации, распределяют реплики по спикерам.

## Стек

- .NET 10, WPF
- NAudio / WASAPI
- Ical.Net
- Windows Task Scheduler и Registry Run fallback
- GigaAM v3 worker для локального ASR
- Pyannote Community-1 worker для локальной диаризации
- xUnit-тесты для core-логики

## Документация

Документация проекта на GitHub:

- [Контекст проекта](https://github.com/zitramonio/autorecord/blob/public-release/context/project.md)
- [Дизайн MVP](https://github.com/zitramonio/autorecord/blob/public-release/docs/superpowers/specs/2026-05-06-autorecord-design.md)
- [План реализации MVP](https://github.com/zitramonio/autorecord/blob/public-release/docs/superpowers/plans/2026-05-06-autorecord-mvp.md)
- [Дизайн локальной транскрибации v2](https://github.com/zitramonio/autorecord/blob/public-release/docs/superpowers/specs/2026-05-07-autorecord-v2-transcription-design.md)
- [План реализации локальной транскрибации v2](https://github.com/zitramonio/autorecord/blob/public-release/docs/superpowers/plans/2026-05-07-autorecord-v2-transcription.md)
- [Дизайн установки моделей](https://github.com/zitramonio/autorecord/blob/public-release/docs/superpowers/specs/2026-05-08-model-install-flow-design.md)
- [Инструкция по установке Pyannote Community-1](https://github.com/zitramonio/autorecord/blob/public-release/docs/huggingface-pyannote-install.md)

## Статус

Autorecord находится в активной разработке. Реализованы запись встреч, safe-save MP3, локальная транскрибация и базовый релизный сценарий с GigaAM v3 + Pyannote Community-1. Следующий практический шаг - ручная GUI-проверка полного сценария на реальной записи: старт, остановка, выбор числа спикеров, автотранскрибация и проверка созданных transcript-файлов.

## Ограничения

- Приложение рассчитано только на Windows.
- Для Pyannote Community-1 нужно принять условия модели на Hugging Face и один раз предоставить `Read` token при скачивании.
- Модели и publish-артефакты не предназначены для хранения в git.
- Качество разделения по спикерам зависит от качества исходного звука, числа участников и пересечений речи.
