# Pyannote Community-1 worker

Local worker source for the `pyannote-community-1` diarization engine.

The user-facing app must run a bundled `worker.exe`; users must not install
Python or run this script manually. This folder contains source files used to
build that worker artifact outside git.

Expected packaged layout:

```text
artifacts/vendor/pyannote-community-worker/
  worker.exe
  ...
```

Runtime contract:

```text
worker.exe --input normalized.wav --model <local-model-folder> --output-json result.json [--num-speakers N]
```

The model folder must already contain the local Pyannote Community-1 snapshot:

```text
config.yaml
segmentation/pytorch_model.bin
embedding/pytorch_model.bin
plda/plda.npz
plda/xvec_transform.npz
```

The worker sets Hugging Face offline environment variables and disables
`PYANNOTE_METRICS_ENABLED` before loading the pipeline, so diarization does not
trigger network requests or pyannote telemetry during processing.
