# Autorecord v2 Transcription Design

## Goal

Add full local transcription to Autorecord without changing the existing meeting recording behavior. The user configures transcription in the GUI, downloads models explicitly from the GUI, and after that recordings are transcribed locally without cloud APIs, telemetry, Docker, command-line steps, or manual Python installation.

The current v1 recording output remains the source of truth: recordings are saved as `.mp3`. The transcription pipeline accepts the saved `.mp3`, creates a temporary normalized `.wav` for ASR, and writes transcript files to the configured transcript output folder.

## Non-Goals

- No cloud ASR APIs.
- No OpenAI API, Google Speech, Yandex SpeechKit, Azure Speech, AWS Transcribe, AssemblyAI, Deepgram, or analogs.
- No uploading audio, transcripts, embeddings, logs, or telemetry.
- No cloud sync, auth, web service, Docker, or user-visible command-line workflow.
- No calendar editing, meeting summarization, LLM rewriting, messenger posting, or real person name identification.
- No automatic model download without explicit user action.

## User Scenario

1. The user opens the app.
2. The user opens the `Транскрибация` tab.
3. The user selects an ASR model and diarization mode.
4. If the selected model is missing, the user clicks `Скачать модель`.
5. The app downloads and installs the model into the local models folder.
6. The app records meetings as before.
7. After a recording is saved, the file is queued if automatic transcription is enabled.
8. The queued file is transcribed locally.
9. Transcript files appear in the selected transcript folder:
   - `.txt`
   - `.md`
   - `.srt`
   - `.json`
10. The user sees model, download, queue, transcription, completion, and error statuses in the GUI.

## Core Architecture

Recording remains a separate stable subsystem. Transcription attaches only to the successful recording-save event.

Flow:

1. `RecordingCoordinator.RecordingSaved` reports the saved `.mp3` path.
2. `App` checks `AppSettings.Transcription.AutoTranscribeAfterRecording`.
3. If enabled, `TranscriptionQueue.Enqueue(savedAudioPath)` creates and persists a `TranscriptionJob`.
4. `TranscriptionQueue` runs one job at a time in the background.
5. `TranscriptionPipeline` processes the job:
   - validate selected ASR model through `ModelManager`;
   - validate selected diarization model if diarization is enabled;
   - normalize audio to `16 kHz mono PCM wav` under `%LOCALAPPDATA%\Autorecord\Temp`;
   - run `DiarizationEngine` if enabled;
   - run selected `ITranscriptionEngine`;
   - assemble ASR segments and speaker turns through `TranscriptAssembler`;
   - export `.txt`, `.md`, `.srt`, and `.json` through `TranscriptExporter`;
   - update job status and output files.

Architectural boundaries:

- `ModelCatalog` reads `models/catalog.json`.
- `ModelManager` checks installed model state, required files, and manifest entries.
- `ModelDownloadService` is the only app service that performs network requests.
- `ITranscriptionEngine` knows only about local audio, local model paths, progress, and cancellation.
- `DiarizationEngine` is separate from ASR and can be reused with different ASR engines.
- `TranscriptAssembler` combines text segments with speaker turns.
- `TranscriptExporter` only writes transcript files.
- `TranscriptionJobRepository` persists queue/history locally.

## Network Rule

Internet access is allowed only for explicit model download through the GUI. After models are installed, transcription must work offline.

`LocalNetworkGuard` records and enforces this rule for app-owned services: only `ModelDownloadService` receives network/download dependencies. Transcription engines and workers receive local paths only. Native or bundled third-party code cannot be fully OS-sandboxed by this app alone, so the implementation must avoid invoking any network-enabled workflow during transcription and must not pass URLs or credentials to engines.

## Settings

Add `TranscriptionSettings` under the existing local settings file:

```json
{
  "transcription": {
    "autoTranscribeAfterRecording": true,
    "selectedAsrModelId": "sherpa-gigaam-v2-ru-fast",
    "selectedDiarizationModelId": "sherpa-diarization-pyannote-fast",
    "enableDiarization": true,
    "numSpeakers": null,
    "clusterThreshold": 0.65,
    "outputFolderMode": "SameAsRecording",
    "customOutputFolder": null,
    "outputFormats": ["txt", "md", "srt", "json"],
    "overwriteExistingTranscripts": false,
    "keepIntermediateFiles": false
  }
}
```

Types:

- `AutoTranscribeAfterRecording: bool`
- `SelectedAsrModelId: string`
- `SelectedDiarizationModelId: string`
- `OutputFolderMode: SameAsRecording | CustomFolder`
- `CustomOutputFolder: string?`
- `OutputFormats: txt | md | srt | json`
- `EnableDiarization: bool`
- `NumSpeakers: int?`
- `ClusterThreshold: double?`
- `OverwriteExistingTranscripts: bool`
- `KeepIntermediateFiles: bool`

Existing recording settings remain backward compatible. Missing transcription settings load defaults.

## Transcript Output Folder

Audio output and transcript output are separate concerns.

- Existing `OutputFolder` remains the recording folder.
- `TranscriptionSettings.OutputFolderMode` controls transcript placement.
- `SameAsRecording` writes transcripts beside the input audio file.
- `CustomFolder` writes transcripts to `CustomOutputFolder`.
- `TranscriptionJob.OutputDirectory` stores the resolved directory at enqueue time.
- Changing settings later affects only new jobs.
- If the transcript folder is unavailable, the job fails with `OutputFolderUnavailable`; the audio file is not modified.

## GUI

Add a `Транскрибация` tab to the existing `MainWindow`.

The tab contains:

- checkbox `Транскрибировать записи автоматически`;
- ASR model dropdown from `ModelCatalog`;
- diarization mode dropdown:
  - `Без разделения по спикерам`;
  - `Спикеры — быстро`;
  - `Спикеры — качество`;
- selected model status:
  - `Не установлена`;
  - `Скачивается`;
  - `Установлена`;
  - `Ошибка`;
- buttons:
  - `Скачать модель`;
  - `Удалить модель`;
  - `Проверить модель`;
  - `Открыть папку моделей`;
- download progress bar, speed, downloaded/total, and cancel action;
- speaker count selector:
  - `Auto`, `1`, `2`, `3`, `4`, `5`, `6`;
- transcript output folder selector:
  - beside recording;
  - custom folder;
- output format checkboxes:
  - `TXT`;
  - `Markdown`;
  - `SRT`;
  - `JSON`;
- recent jobs list with columns:
  - file;
  - model;
  - status;
  - progress;
  - created;
  - finished;
- job actions:
  - open transcript;
  - open folder;
  - retry;
  - cancel;
  - delete from history;
- button `Выбрать файл и транскрибировать`.

The model list is never hardcoded in XAML. It comes from `ModelCatalog`.

## Model Catalog

Create `models/catalog.json`. The catalog is extensible: adding a new model with an existing engine requires only a new catalog entry. Adding a new engine requires one engine implementation and catalog entries that reference it.

Minimum v2 catalog:

- `sherpa-gigaam-v2-ru-fast`
  - display: `Русский — быстро`
  - type: `asr`
  - engine: `sherpa-onnx`
  - language: `ru`
  - download URL: `https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-nemo-ctc-giga-am-v2-russian-2025-04-19.tar.bz2`
  - required files: `model.int8.onnx`, `tokens.txt`

- `gigaam-v3-ru-quality`
  - display: `Русский — качество`
  - type: `asr`
  - engine: `gigaam-v3`
  - language: `ru`
  - direct download may be unavailable until a suitable bundled package exists.

- `sherpa-diarization-pyannote-fast`
  - display: `Спикеры — быстро`
  - type: `diarization`
  - engine: `sherpa-onnx`
  - required files: `model.int8.onnx`, `3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx`

- `sherpa-diarization-pyannote-quality`
  - display: `Спикеры — качество`
  - type: `diarization`
  - engine: `sherpa-onnx`
  - required files: `model.onnx`, `3dspeaker_speech_eres2net_base_sv_zh-cn_3dspeaker_16k.onnx`

The `Без разделения по спикерам` mode is a GUI mode, not a downloadable model.

## Model Storage

Installed models are stored locally:

- models folder: `%LOCALAPPDATA%\Autorecord\Models`
- manifest: `%LOCALAPPDATA%\Autorecord\Models\manifest.json`
- temporary downloads: `%LOCALAPPDATA%\Autorecord\Temp\Downloads`

Manifest entries contain:

- model id;
- display name;
- engine;
- version;
- local path;
- installed timestamp;
- total size bytes;
- installed files;
- status.

Downloaded models are not stored in the repository and must not be committed.

## Model Download

`ModelDownloadService`:

- starts only after explicit GUI action;
- shows model name, approximate size, and install folder before download;
- downloads to a temporary file;
- reports progress, speed, and downloaded/total;
- supports cancellation;
- deletes temporary files after cancellation;
- checks available disk space where possible;
- checks response errors;
- verifies size and `sha256` when specified;
- extracts supported archives;
- validates `requiredFiles`;
- updates manifest only after successful validation.

Normalized user errors:

- `ModelDownloadFailed`
- `ModelValidationFailed`
- `NotEnoughDiskSpace`
- `OutputFolderUnavailable`

For GigaAM v3, if no direct GUI-installable package is available, the UI must show a clear unavailable-download status rather than silently failing.

## Queue and Jobs

`TranscriptionJob` fields:

- `Id`
- `InputFilePath`
- `OutputDirectory`
- `AsrModelId`
- `DiarizationModelId`
- `Status`
- `ProgressPercent`
- `CreatedAt`
- `StartedAt`
- `FinishedAt`
- `ErrorMessage`
- `OutputFiles`

Statuses:

- `Pending`
- `WaitingForModel`
- `Running`
- `Completed`
- `Failed`
- `Cancelled`

`TranscriptionJobRepository` stores queue and history in `%LOCALAPPDATA%\Autorecord\transcription-jobs.json`.

Behavior:

- Only one job runs at a time.
- UI never waits synchronously for transcription.
- If the selected model is missing, the job stays `WaitingForModel`.
- After model installation, matching waiting jobs may move to `Pending`.
- On app restart, `Running` jobs become `Pending` with a note that processing was interrupted, unless the input file is missing; missing inputs become `Failed`.
- Each job writes a local log to `%LOCALAPPDATA%\Autorecord\Logs\transcription-job-{id}.log`.
- Logs do not include full transcript text or full iCal URLs.

## Audio Pipeline

Accepted input extensions:

- `.wav`
- `.mp3`
- `.m4a`
- `.flac`
- `.ogg`
- `.mp4`
- `.mkv`

v2 must reliably support files recorded by the app (`.mp3`) and normalized `.wav`. Other formats may depend on the chosen local decoding component.

Processing:

- create normalized temp wav:
  - mono;
  - 16 kHz;
  - signed 16-bit PCM;
- store temp files under `%LOCALAPPDATA%\Autorecord\Temp`;
- avoid re-encoding if an input `.wav` already matches;
- ignore video streams for video files;
- delete temp files after success unless `KeepIntermediateFiles` is enabled;
- keep temp files after errors only when `KeepIntermediateFiles` is enabled.

FFmpeg is not required from the user. If needed later, it must be bundled with the app, downloaded as an explicit local component through GUI, or avoided for the guaranteed app-recorded-file path.

## ASR Engines

### SherpaOnnxTranscriptionEngine

Primary fast engine for v2.

Requirements:

- runs from .NET/WPF;
- no Python;
- uses local `.onnx` models;
- supports `sherpa-gigaam-v2-ru-fast`;
- accepts normalized local `.wav`;
- returns segments:
  - start;
  - end;
  - text;
  - confidence when available;
- supports progress and cancellation;
- performs no network requests.

### GigaAmV3TranscriptionEngine

Quality-mode engine for Russian speech.

Implementation strategy:

1. Try direct local backend first.
2. If direct .NET integration is not stable, use a hidden bundled worker.

Worker requirements:

- launched by the app automatically;
- no visible console;
- local only;
- no network requests during transcription;
- accepts input wav and model path;
- returns JSON segments;
- app handles all transcript exports;
- dependencies are bundled or installed by the app, never manually by the user.

Long-form requirements:

- supports 30 minute, 1 hour, and 2 hour recordings;
- chunk target 20 seconds;
- max chunk 30 seconds;
- 0.25 second left/right padding;
- global timestamps;
- duplicate removal at chunk boundaries;
- cancellation and progress.

If packaging is not solved during the first implementation pass, the model remains visible in the GUI with a clear unavailable-install status, while the engine interface and worker contract remain in place.

## Diarization

`DiarizationEngine` is separate from ASR but part of the same automatic transcription pipeline.

Modes:

- no diarization;
- fast diarization;
- quality diarization.

Requirements:

- accepts normalized wav;
- returns speaker turns:
  - start;
  - end;
  - speaker id;
- supports `NumSpeakers`:
  - `null` = auto;
  - `1..6` = fixed speaker count;
- supports `ClusterThreshold`;
- merges adjacent same-speaker turns when gap <= 0.7 seconds;
- removes turns shorter than 0.25 seconds;
- keeps raw diarization segments in JSON;
- never tries to infer real people names.

ASR and diarization merge:

- If diarization is disabled, output uses `Speaker 1`.
- If diarization is enabled, final segments use `Speaker 1`, `Speaker 2`, etc.
- If ASR has word-level timestamps, words may be assigned to turns precisely.
- The first implementation may run ASR per speaker/chunk turn and assign the result to that speaker.
- Long speaker turns are chunked to 20-30 seconds.
- Adjacent same-speaker transcript segments may merge when gap <= 1 second and combined text <= 600 characters.
- Adjacent different-speaker transcript segments must not merge.

If diarization fails while enabled, the job fails with `DiarizationFailed`. The user can retry without diarization.

## Transcript Export

`TranscriptExporter` receives a `TranscriptDocument` and writes selected formats.

If input is `06.05.2026 18.42.mp3`, base transcript name is `06.05.2026 18.42`.

Outputs:

- `06.05.2026 18.42.txt`
- `06.05.2026 18.42.md`
- `06.05.2026 18.42.srt`
- `06.05.2026 18.42.json`

If files exist and `OverwriteExistingTranscripts` is false:

- `06.05.2026 18.42 transcript 2.txt`
- `06.05.2026 18.42 transcript 2.md`
- `06.05.2026 18.42 transcript 2.srt`
- `06.05.2026 18.42 transcript 2.json`

If overwrite is true, write through temporary files and replace atomically.

TXT and Markdown include timestamps and speaker labels. JSON includes metadata, settings, speakers, final segments, and `rawDiarizationSegments`. When diarization is disabled, `rawDiarizationSegments` is an empty array.

## Notifications

Add `TranscriptionNotificationService` over the existing notification/tray pattern.

Events:

- model downloaded;
- model download failed;
- transcription started;
- transcript ready;
- transcription failed.

The existing `Запись сохранена` notification may include or be followed by an action/status to transcribe now when automatic transcription is disabled.

## Error Messages

User-facing normalized errors:

- `ModelNotInstalled`: `Модель не установлена. Скачайте модель во вкладке Транскрибация.`
- `ModelDownloadFailed`: `Не удалось скачать модель. Проверьте интернет и свободное место на диске.`
- `ModelValidationFailed`: `Модель скачана, но не прошла проверку. Попробуйте скачать её заново.`
- `TranscriptionFailed`: `Не удалось выполнить транскрибацию. Запись сохранена, её можно обработать повторно.`
- `DiarizationFailed`: `Не удалось разделить речь по спикерам. Можно повторить без диаризации.`
- `UnsupportedAudioFormat`: `Формат файла не поддерживается. Для версии 2 гарантированно поддерживаются записи приложения и .wav.`
- `OutputFolderUnavailable`: `Папка для транскриптов недоступна. Выберите другую папку.`
- `NotEnoughDiskSpace`: `Недостаточно места на диске для скачивания модели или сохранения транскрипта.`

## Logging

Logs are local only.

Job log path:

`%LOCALAPPDATA%\Autorecord\Logs\transcription-job-{id}.log`

Log:

- job id;
- input file path;
- selected ASR model;
- selected diarization model;
- start;
- finish;
- audio duration;
- processing duration;
- error if present.

Do not log full transcript text. Do not log full iCal URLs.

## Implementation Phases

### Phase A: Data, Settings, Catalog, UI

- Add `TranscriptionSettings`.
- Add model/job/transcript data types.
- Add `models/catalog.json`.
- Add `Транскрибация` tab.
- Load model dropdowns from `ModelCatalog`.
- Save/load transcription settings.

Verification:

- app builds;
- old settings remain compatible;
- tab is visible;
- models appear from catalog.

### Phase B: ModelManager and ModelDownloadService

- Installed model detection.
- Required-file validation.
- Manifest persistence.
- GUI download with progress/cancel/retry.
- Archive extraction.
- Model deletion and validation.

Verification:

- `Русский — быстро` downloads, validates, and shows as installed.

### Phase C: Queue and Export

- `TranscriptionJobRepository`.
- `TranscriptionQueue`.
- restore jobs after restart.
- export fake/test transcript to all selected formats.

Verification:

- jobs persist, restore, run one at a time, and export files.

### Phase D: SherpaOnnxTranscriptionEngine

- Add sherpa-onnx package.
- Normalize audio.
- Run local ASR for `sherpa-gigaam-v2-ru-fast`.
- Progress and cancellation.

Verification:

- app-recorded `.mp3` produces transcript files locally.

### Phase E: Diarization

- Add `DiarizationEngine`.
- Add fast and quality sherpa diarization modes.
- Add `TranscriptAssembler`.

Verification:

- transcript files include `Speaker 1`, `Speaker 2`, etc. when diarization is enabled;
- JSON includes raw diarization segments.

### Phase F: GigaAM v3 Quality Mode

- Try direct local backend.
- Fall back to hidden bundled worker if needed.
- Add long-form chunking, padding, timestamps, cancellation, and progress.

Verification:

- `Русский — качество` can be selected and used after GUI installation or bundled component installation.

### Phase G: Recording Integration and Manual File Pick

- Connect `RecordingSaved` to `TranscriptionQueue.Enqueue`.
- Add `Выбрать файл и транскрибировать`.
- Add notifications and retry/cancel/open actions.

Verification:

- after recording, transcript files appear automatically when enabled.

## Automated Tests

Add unit tests:

- `ModelCatalog` loads models.
- `ModelManager` detects installed model.
- `ModelManager` detects missing required files.
- `ModelDownloadService` handles failed download.
- `TranscriptionQueue` persists pending jobs.
- `TranscriptionQueue` restores jobs after restart.
- `TranscriptExporter` creates txt.
- `TranscriptExporter` creates md.
- `TranscriptExporter` creates srt.
- `TranscriptExporter` creates json.
- settings save/load transcription settings.
- existing recording settings remain compatible.

Add integration/manual tests:

- recording and auto-transcription of app-created audio;
- model download from GUI;
- model switching;
- offline transcription after download;
- model deletion;
- unavailable transcript output folder error.

## Acceptance Criteria

1. Project builds.
2. Existing recording behavior is not broken.
3. Meeting recording still saves as before.
4. `Транскрибация` tab exists.
5. Auto-transcription can be enabled and disabled.
6. ASR model can be selected.
7. Diarization mode can be selected.
8. Model can be downloaded from GUI.
9. Downloaded model shows as installed.
10. Installed model can be deleted from GUI.
11. Existing audio file can be selected and queued.
12. Saved recording is queued automatically when enabled.
13. UI does not freeze during transcription.
14. Transcription progress is visible.
15. Errors are understandable.
16. Successful transcription creates `.txt`, `.md`, `.srt`, `.json`.
17. `.txt` and `.md` include timestamps.
18. Diarized `.txt` and `.md` include speaker labels.
19. `.json` includes raw diarization segments.
20. Job history persists after restart.
21. Installed models remain installed after restart.
22. Transcription works offline after models are installed.
23. Model downloads happen only after explicit user click.
24. No cloud ASR API is used.
25. Docker is not required.
26. User command line is not required.
27. Manual Python installation is not required.
28. Audio, transcripts, logs, embeddings, and telemetry are not sent out.

## References

- sherpa-onnx C# API: https://k2-fsa.github.io/sherpa/onnx/csharp-api/index.html
- sherpa-onnx NuGet package: https://www.nuget.org/packages/org.k2fsa.sherpa.onnx
- sherpa-onnx speaker diarization: https://k2-fsa.github.io/sherpa/onnx/speaker-diarization/index.html
- GigaAM repository: https://github.com/salute-developers/GigaAM
