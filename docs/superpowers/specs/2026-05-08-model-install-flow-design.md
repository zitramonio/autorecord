# Model Install Flow Design

## Goal

Replace the temporary-only model download behavior with a local install flow that lets the GUI download a model and make `ModelManager` report it as installed.

## Scope

- Downloads are still started only by explicit GUI action.
- `ModelDownloadService` remains responsible for HTTP downloads and temp files.
- `ModelInstallService` handles local install:
  - verifies sha256 when catalog provides it;
  - installs into `%LOCALAPPDATA%\Autorecord\Models\<targetFolder>`;
  - uses a staging folder and only replaces the target after validation;
  - extracts archives or copies plain files;
  - validates `requiredFiles`;
  - writes `%LOCALAPPDATA%\Autorecord\Models\manifest.json`.
- GUI button `Скачать модель` runs download then install, and reports installed status only after validation.

## Archive Support

- `zip` is supported through .NET built-in APIs.
- `tar.bz2` is supported through a local library dependency because .NET has TAR support but no built-in BZip2 decompressor.
- Plain file install is supported for single-file downloads such as diarization embeddings.

## Diarization Downloads

Models with both `segmentationUrl` and `embeddingUrl` are installed as one model:

- download segmentation archive;
- extract it to staging;
- download embedding `.onnx`;
- copy embedding into staging root;
- validate all required files before publishing target folder.

## Manifest

`manifest.json` stores one entry per installed model:

- id, displayName, engine, version;
- localPath;
- installedAt;
- totalSizeBytes;
- file list;
- status.

The manifest is updated only after required files pass validation.

## Errors

The install flow must return clear local errors:

- checksum mismatch;
- unsupported archive type;
- extraction failure;
- missing required files;
- path outside models root.

No audio, transcript, log, or model-derived data is sent outside the machine.
