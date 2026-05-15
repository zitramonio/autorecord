import argparse
import json
import os
import sys
import wave


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Autorecord Pyannote Community-1 diarization worker")
    parser.add_argument("--input", required=True, help="Normalized wav file")
    parser.add_argument("--model", required=True, help="Local Pyannote Community-1 model folder")
    parser.add_argument("--output-json", required=True, help="Output JSON file")
    parser.add_argument("--num-speakers", type=int, default=None, help="Fixed number of speakers")
    parser.add_argument("--cluster-threshold", type=float, default=None, help="Reserved for future tuning")
    return parser.parse_args()


def require_file(path: str) -> None:
    if not os.path.isfile(path):
        raise FileNotFoundError(path)


def load_normalized_wav(path: str):
    import torch

    with wave.open(path, "rb") as wav_file:
        if wav_file.getcomptype() != "NONE":
            raise ValueError("Input WAV must be uncompressed PCM.")

        channels = wav_file.getnchannels()
        sample_width = wav_file.getsampwidth()
        sample_rate = wav_file.getframerate()
        frame_count = wav_file.getnframes()

        if sample_width != 2:
            raise ValueError("Input WAV must be signed 16-bit PCM.")

        raw = wav_file.readframes(frame_count)

    if frame_count == 0:
        return torch.empty((1, 0), dtype=torch.float32), sample_rate

    samples = torch.frombuffer(bytearray(raw), dtype=torch.int16).to(torch.float32) / 32768.0
    if channels > 1:
        samples = samples.reshape(-1, channels).mean(dim=1)

    return samples.unsqueeze(0).contiguous(), sample_rate


def main() -> int:
    args = parse_args()

    os.environ["HF_HUB_OFFLINE"] = "1"
    os.environ["TRANSFORMERS_OFFLINE"] = "1"
    os.environ["HF_HUB_DISABLE_TELEMETRY"] = "1"
    os.environ["PYANNOTE_METRICS_ENABLED"] = "false"

    require_file(args.input)
    require_file(os.path.join(args.model, "config.yaml"))
    require_file(os.path.join(args.model, "segmentation", "pytorch_model.bin"))
    require_file(os.path.join(args.model, "embedding", "pytorch_model.bin"))
    require_file(os.path.join(args.model, "plda", "plda.npz"))
    require_file(os.path.join(args.model, "plda", "xvec_transform.npz"))

    from pyannote.audio import Pipeline

    pipeline = Pipeline.from_pretrained(args.model)
    kwargs = {}
    if args.num_speakers is not None:
        kwargs["num_speakers"] = args.num_speakers

    waveform, sample_rate = load_normalized_wav(args.input)
    output = pipeline({"waveform": waveform, "sample_rate": sample_rate}, **kwargs)
    annotation = getattr(output, "exclusive_speaker_diarization", None)
    if annotation is None:
        annotation = getattr(output, "speaker_diarization", output)

    turns = []
    for turn, _, speaker in annotation.itertracks(yield_label=True):
        turns.append(
            {
                "start": float(turn.start),
                "end": float(turn.end),
                "speakerId": str(speaker),
            }
        )

    with open(args.output_json, "w", encoding="utf-8") as output_file:
        json.dump({"turns": turns}, output_file, ensure_ascii=False)

    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"Pyannote Community-1 worker failed: {exc}", file=sys.stderr)
        raise
