from __future__ import annotations

import argparse
import array
import json
import math
import os
import sys
import wave
from dataclasses import dataclass
from pathlib import Path


SAMPLE_RATE = 16_000
CHANNELS = 1
SAMPLE_WIDTH = 2
TARGET_CHUNK_SECONDS = 20.0
MAX_CHUNK_SECONDS = 24.0
PADDING_SECONDS = 0.25
FRAME_SECONDS = 0.03
MERGE_GAP_SECONDS = 0.35
MIN_SPEECH_SECONDS = 0.30


@dataclass(frozen=True)
class Audio:
    path: Path
    sample_rate: int
    channels: int
    sample_width: int
    total_samples: int


@dataclass(frozen=True)
class Interval:
    start: float
    end: float


@dataclass(frozen=True)
class Chunk:
    decode: Interval
    output: Interval


def main() -> int:
    args = parse_args()
    input_path = Path(args.input)
    model_path = Path(args.model)
    output_json_path = Path(args.output_json)

    require_model_files(model_path, args.model_name)
    audio = read_normalized_wav(input_path)
    speech_chunks = build_speech_chunks(audio)

    if not speech_chunks:
        write_result(output_json_path, [])
        return 0

    import gigaam  # Imported after local model validation to avoid implicit downloads.
    import torch

    model = gigaam.load_model(
        args.model_name,
        fp16_encoder=False,
        use_flash=False,
        device=args.device,
        download_root=str(model_path),
    )

    segments = []
    for chunk in speech_chunks:
        text = transcribe_chunk(model, torch, audio, chunk.decode)
        if text:
            segments.append(
                {
                    "start": round(chunk.output.start, 3),
                    "end": round(chunk.output.end, 3),
                    "text": text,
                    "confidence": None,
                }
            )

    write_result(output_json_path, segments)
    return 0


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--model", required=True)
    parser.add_argument("--output-json", required=True)
    parser.add_argument("--model-name", default="v3_e2e_rnnt")
    parser.add_argument("--device", default="cpu")
    return parser.parse_args()


def require_model_files(model_path: Path, model_name: str) -> None:
    required = [model_path / f"{model_name}.ckpt"]
    if "e2e" in model_name or model_name == "v1_rnnt":
        required.append(model_path / f"{model_name}_tokenizer.model")

    missing = [str(path) for path in required if not path.is_file()]
    if missing:
        raise FileNotFoundError(
            "GigaAM model files are missing. Install the model through the app first: "
            + ", ".join(missing)
        )


def read_normalized_wav(path: Path) -> Audio:
    with wave.open(str(path), "rb") as wav:
        audio = Audio(
            path=path,
            sample_rate=wav.getframerate(),
            channels=wav.getnchannels(),
            sample_width=wav.getsampwidth(),
            total_samples=wav.getnframes(),
        )

    if audio.sample_rate != SAMPLE_RATE or audio.channels != CHANNELS or audio.sample_width != SAMPLE_WIDTH:
        raise ValueError("Input WAV must be normalized to 16 kHz mono PCM16.")

    return audio


def build_speech_chunks(audio: Audio) -> list[Chunk]:
    speech = detect_speech(audio)
    if not speech:
        return []

    chunks: list[Interval] = []
    current_start = speech[0].start
    current_end = speech[0].end

    for interval in speech[1:]:
        candidate_end = interval.end
        if candidate_end - current_start <= TARGET_CHUNK_SECONDS:
            current_end = candidate_end
            continue

        chunks.extend(split_long_interval(Interval(current_start, current_end)))
        current_start = interval.start
        current_end = interval.end

    chunks.extend(split_long_interval(Interval(current_start, current_end)))
    duration = duration_seconds(audio)
    return [Chunk(decode=pad_interval(chunk, duration), output=chunk) for chunk in chunks]


def detect_speech(audio: Audio) -> list[Interval]:
    frame_samples = max(1, int(FRAME_SECONDS * audio.sample_rate))
    energies: list[float] = []
    frame_bounds: list[Interval] = []

    with wave.open(str(audio.path), "rb") as wav:
        for start_sample in range(0, audio.total_samples, frame_samples):
            frame = wav.readframes(frame_samples)
            if not frame:
                break

            frame_sample_count = len(frame) // audio.sample_width
            frame_start = start_sample / audio.sample_rate
            frame_end = min((start_sample + frame_sample_count) / audio.sample_rate, duration_seconds(audio))
            frame_bounds.append(Interval(frame_start, frame_end))
            energies.append(rms_pcm16(frame))

    if not energies:
        return []

    sorted_energies = sorted(energies)
    noise_floor = sorted_energies[int(len(sorted_energies) * 0.2)]
    peak = max(energies)
    threshold = max(60.0, noise_floor * 3.0, peak * 0.06)

    intervals: list[Interval] = []
    active_start: float | None = None
    for index, energy in enumerate(energies):
        frame_start = frame_bounds[index].start
        frame_end = frame_bounds[index].end
        if energy >= threshold:
            active_start = frame_start if active_start is None else active_start
        elif active_start is not None:
            intervals.append(Interval(active_start, frame_end))
            active_start = None

    if active_start is not None:
        intervals.append(Interval(active_start, duration_seconds(audio)))

    return merge_intervals(
        [interval for interval in intervals if interval.end - interval.start >= MIN_SPEECH_SECONDS],
        MERGE_GAP_SECONDS,
    )


def merge_intervals(intervals: list[Interval], max_gap: float) -> list[Interval]:
    if not intervals:
        return []

    merged = [intervals[0]]
    for interval in intervals[1:]:
        previous = merged[-1]
        if interval.start - previous.end <= max_gap:
            merged[-1] = Interval(previous.start, max(previous.end, interval.end))
        else:
            merged.append(interval)

    return merged


def split_long_interval(interval: Interval) -> list[Interval]:
    if interval.end - interval.start <= MAX_CHUNK_SECONDS:
        return [interval]

    chunks: list[Interval] = []
    start = interval.start
    while start < interval.end:
        end = min(interval.end, start + TARGET_CHUNK_SECONDS)
        chunks.append(Interval(start, end))
        start = end
    return chunks


def pad_interval(interval: Interval, duration: float) -> Interval:
    return Interval(
        max(0.0, interval.start - PADDING_SECONDS),
        min(duration, interval.end + PADDING_SECONDS),
    )


def transcribe_chunk(model: object, torch: object, audio: Audio, interval: Interval) -> str:
    start_sample = max(0, int(math.floor(interval.start * audio.sample_rate)))
    end_sample = min(audio.total_samples, int(math.ceil(interval.end * audio.sample_rate)))
    with wave.open(str(audio.path), "rb") as wav_file:
        wav_file.setpos(start_sample)
        frame_bytes = wav_file.readframes(end_sample - start_sample)

    samples = array.array("h")
    samples.frombytes(frame_bytes)

    with torch.inference_mode():
        wav = torch.tensor(samples, dtype=torch.float32, device=model._device) / 32768.0
        wav = wav.to(model._device).to(model._dtype).unsqueeze(0)
        length = torch.full([1], wav.shape[-1], device=model._device)
        encoded, encoded_len = model.forward(wav, length)
        text, _ = model._decode(encoded, encoded_len, length, False)[0]
        return text.strip()


def rms_pcm16(frame: bytes) -> float:
    if not frame:
        return 0.0

    total = 0
    sample_count = len(frame) // SAMPLE_WIDTH
    for index in range(0, len(frame), SAMPLE_WIDTH):
        value = int.from_bytes(frame[index : index + SAMPLE_WIDTH], "little", signed=True)
        total += value * value

    return math.sqrt(total / sample_count) if sample_count else 0.0


def duration_seconds(audio: Audio) -> float:
    return audio.total_samples / audio.sample_rate


def write_result(path: Path, segments: list[dict[str, object]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp_path = path.with_suffix(path.suffix + ".tmp")
    with temp_path.open("w", encoding="utf-8") as file:
        json.dump({"segments": segments}, file, ensure_ascii=False)
    os.replace(temp_path, path)


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(str(error), file=sys.stderr)
        raise SystemExit(1)
