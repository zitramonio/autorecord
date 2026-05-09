# GigaAM worker

Local worker source for the `gigaam-v3` ASR engine.

The user-facing app must run a bundled `worker.exe`; users must not install Python
or run this script manually. This folder contains source files used to build that
worker artifact outside git.

Expected packaged layout:

```text
artifacts/vendor/gigaam-worker/
  worker.exe
  ...
```

`scripts/publish.ps1` copies that folder to:

```text
artifacts/publish/Autorecord/workers/gigaam/
```

Runtime contract:

```text
worker.exe --input normalized.wav --model <local-model-folder> --output-json result.json
```

The model folder must already contain the local GigaAM files. For the default
`v3_e2e_rnnt` model:

```text
v3_e2e_rnnt.ckpt
v3_e2e_rnnt_tokenizer.model
```

The worker verifies these files before importing/loading GigaAM so transcription
does not trigger network downloads.

Build note: GigaAM v3 is currently available from the upstream GitHub code path,
not from the older PyPI `gigaam==0.1.0` package. The worker requirements install
the upstream package source with the `torch` extra for packaging, pinned to the
commit used for the verified worker build.

If Python 3.10 is not available through `py`, pass an explicit interpreter:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\gigaam-worker\build.ps1 -PythonExe C:\Path\To\python.exe
```
