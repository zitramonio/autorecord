from __future__ import annotations

import argparse
import json
import math
import os
import tempfile
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
    frames: bytes
    sample_rate: int
    channels: int
    sample_width: int


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

    model = gigaam.load_model(
        args.model_name,
        fp16_encoder=False,
        use_flash=False,
        device=args.device,
        download_root=str(model_path),
    )

    segments = []
    with tempfile.TemporaryDirectory(prefix="autorecord-gigaam-") as temp_dir:
        for chunk in speech_chunks:
            chunk_path = Path(temp_dir) / f"chunk-{len(segments):05d}.wav"
            write_chunk(audio, chunk.decode, chunk_path)
            result = model.transcribe(str(chunk_path))
            text = getattr(result, "text", str(result)).strip()
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
            frames=wav.readframes(wav.getnframes()),
            sample_rate=wav.getframerate(),
            channels=wav.getnchannels(),
            sample_width=wav.getsampwidth(),
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
    total_samples = len(audio.frames) // SAMPLE_WIDTH
    energies: list[float] = []

    for start_sample in range(0, total_samples, frame_samples):
        end_sample = min(total_samples, start_sample + frame_samples)
        frame = audio.frames[start_sample * SAMPLE_WIDTH : end_sample * SAMPLE_WIDTH]
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
        frame_start = index * FRAME_SECONDS
        frame_end = min(frame_start + FRAME_SECONDS, duration_seconds(audio))
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


def write_chunk(audio: Audio, interval: Interval, path: Path) -> None:
    start_sample = max(0, int(math.floor(interval.start * audio.sample_rate)))
    end_sample = min(len(audio.frames) // SAMPLE_WIDTH, int(math.ceil(interval.end * audio.sample_rate)))
    frames = audio.frames[start_sample * SAMPLE_WIDTH : end_sample * SAMPLE_WIDTH]

    with wave.open(str(path), "wb") as wav:
        wav.setnchannels(audio.channels)
        wav.setsampwidth(audio.sample_width)
        wav.setframerate(audio.sample_rate)
        wav.writeframes(frames)


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
    return len(audio.frames) / (audio.sample_rate * audio.sample_width)


def write_result(path: Path, segments: list[dict[str, object]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp_path = path.with_suffix(path.suffix + ".tmp")
    with temp_path.open("w", encoding="utf-8") as file:
        json.dump({"segments": segments}, file, ensure_ascii=False)
    os.replace(temp_path, path)


if __name__ == "__main__":
    raise SystemExit(main())
