"""Headless, local-only caption worker. stdout is a bounded JSON-lines protocol."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import argparse
import contextlib
import json
import queue
import threading
import time
import os
os.environ["OPENBLAS_NUM_THREADS"] = "1"
os.environ["CT2_PACKED_GEMM"] = "0"

for _stream in (sys.stdin, sys.stdout, sys.stderr):
    if hasattr(_stream, "reconfigure"):
        _stream.reconfigure(encoding="utf-8", errors="replace")

_output = sys.stdout
_output_lock = threading.Lock()


def emit(kind, **values):
    with _output_lock:
        _output.write(json.dumps({"type": kind, **values}, ensure_ascii=False, allow_nan=False) + "\n")
        _output.flush()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", required=True)
    parser.add_argument("--file-test")  # Test-owned WAV only; not used by the app UI.
    parser.add_argument("--probe", action="store_true")
    args = parser.parse_args()
    stopped = threading.Event()
    commands = queue.Queue(maxsize=64)

    def read_commands():
        try:
            for line in sys.stdin:
                if len(line) > 4096:
                    stopped.set(); break
                command = json.loads(line)
                if command.get("command") == "stop":
                    stopped.set(); break
                try:
                    commands.put_nowait(command)
                except queue.Full:
                    pass
        except (ValueError, OSError):
            pass
        finally:
            # EOF also means the owning Driver Booster process disappeared.
            stopped.set()

    capture = transcriber = None
    try:
        with contextlib.redirect_stdout(sys.stderr):
            if args.probe:
                from app.audio_capture import get_loopback_microphone
                device = get_loopback_microphone()
                emit("device", name=str(device.name))
                return 0
            from faster_whisper import WhisperModel
            from app.audio_capture import AudioLoopbackCapture
            from app.transcriber import TranscriberWorker, clean_transcribed_text
            from app.config import AppConfig
            config = AppConfig()
            # Initialize NumPy/native DLLs before starting the stdin reader.
            # On Windows the reverse order can deadlock native module initialization.
            threading.Thread(target=read_commands, daemon=True).start()
            emit("loading")
            # Packaged English model, no downloads, GPU/CUDA probing or API keys.
            model = WhisperModel(args.model, device="cpu", compute_type="int8", cpu_threads=config.cpu_threads, local_files_only=True)
            if stopped.is_set():
                return 0

            def fail(message):
                emit("error", message=str(message)[:2000]); stopped.set()

            def segment(text, start, end):
                if not stopped.is_set():
                    emit("segment", text=text[:8000], start=max(0.0, start), end=max(start, end))

            emit("ready")
            if args.file_test:
                segments, _ = model.transcribe(args.file_test, language="en", beam_size=1, best_of=1, vad_filter=False)
                for item in segments:
                    text = clean_transcribed_text(item.text)
                    if text:
                        segment(text, item.start, item.end)
                emit("file_complete")
            else:
                audio = queue.Queue(maxsize=100)
                last_level = [0.0]
                def level(value):
                    now = time.monotonic()
                    if now - last_level[0] >= 0.1:
                        last_level[0] = now
                        emit("level", value=max(0.0, min(1.0, value)))
                capture = AudioLoopbackCapture(audio, sample_rate=config.sample_rate, on_level=level, on_error=fail)
                transcriber = TranscriberWorker(
                    audio, segment, model=model, sample_rate=config.sample_rate,
                    min_speech_duration_sec=config.min_speech_duration_sec,
                    max_speech_duration_sec=config.max_speech_duration_sec,
                    silence_pause_sec=config.silence_pause_sec,
                    silence_threshold=config.silence_threshold,
                    enable_audio_boost=config.enable_audio_boost,
                    target_rms=config.target_rms,
                    beam_size=config.beam_size,
                    initial_prompt=config.initial_prompt,
                    condition_on_previous_text=config.condition_on_previous_text,
                    cpu_threads=config.cpu_threads,
                    enable_bandpass_filter=config.enable_bandpass_filter,
                    bandpass_low_cutoff=config.bandpass_low_cutoff,
                    bandpass_high_cutoff=config.bandpass_high_cutoff,
                    enable_noise_gate=config.enable_noise_gate,
                    noise_gate_threshold=config.noise_gate_threshold,
                    noise_gate_attack_ms=config.noise_gate_attack_ms,
                    noise_gate_release_ms=config.noise_gate_release_ms,
                    overlap_duration_sec=config.overlap_duration_sec
                )
                transcriber.on_error = fail
                transcriber.start(); capture.start()
            while not stopped.wait(0.05):
                try:
                    command = commands.get_nowait()
                except queue.Empty:
                    continue
                if command.get("command") == "pause":
                    paused = command.get("paused") is True
                    if capture:
                        capture.pause() if paused else capture.resume()
                    if transcriber:
                        transcriber.set_paused(paused)
                    emit("paused", value=paused)
            return 0
    except Exception as error:
        emit("error", message=str(error)[:2000])
        return 1
    finally:
        if capture:
            capture.stop(); capture.join(timeout=1.0)
        if transcriber:
            transcriber.stop(); transcriber.join(timeout=1.0)
        emit("stopped")


if __name__ == "__main__":
    raise SystemExit(main())
