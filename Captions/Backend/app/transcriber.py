import threading
import queue
import time
import re
from typing import Callable
import numpy as np

def apply_bandpass_filter(
    audio: np.ndarray,
    sample_rate: int = 16000,
    low_cutoff: float = 80.0,
    high_cutoff: float = 7500.0,
    order: int = 4
) -> np.ndarray:
    if len(audio) < 64:
        return audio
    try:
        n = len(audio)
        freqs = np.fft.rfftfreq(n, 1.0 / sample_rate)
        eps = 1e-10
        hp = 1.0 / np.sqrt(1.0 + (low_cutoff / (freqs + eps)) ** (2 * order))
        lp = 1.0 / np.sqrt(1.0 + (freqs / high_cutoff) ** (2 * order))
        spec = np.fft.rfft(audio)
        filtered = np.fft.irfft(spec * (hp * lp), n=n)
        return filtered.astype(np.float32)
    except Exception as e:
        print(f"[DSP Filter Warning] Bandpass filter failed: {e}")
        return audio

def apply_noise_gate(
    audio: np.ndarray,
    sample_rate: int = 16000,
    threshold: float = 0.008,
    attack_ms: float = 10.0,
    release_ms: float = 50.0,
    attenuation_db: float = -40.0
) -> np.ndarray:
    if len(audio) == 0:
        return audio
    try:
        frame_size = int(sample_rate * 0.01)
        if frame_size <= 0 or len(audio) < frame_size:
            return audio

        num_frames = (len(audio) + frame_size - 1) // frame_size
        padded = np.pad(audio, (0, num_frames * frame_size - len(audio)), mode='constant')
        frames = padded.reshape(num_frames, frame_size)
        rms_per_frame = np.sqrt(np.mean(frames**2, axis=1))

        floor_gain = 10.0 ** (attenuation_db / 20.0)
        target_gains = np.where(rms_per_frame >= threshold, 1.0, floor_gain)

        attack_coef = np.exp(-1.0 / max(1, (attack_ms * 0.001 * sample_rate / frame_size)))
        release_coef = np.exp(-1.0 / max(1, (release_ms * 0.001 * sample_rate / frame_size)))

        smooth_gains = np.empty(num_frames, dtype=np.float32)
        current_gain = target_gains[0]
        for i in range(num_frames):
            target = target_gains[i]
            if target > current_gain:
                current_gain = target + attack_coef * (current_gain - target)
            else:
                current_gain = target + release_coef * (current_gain - target)
            smooth_gains[i] = current_gain

        sample_gains = np.repeat(smooth_gains, frame_size)[:len(audio)]
        return (audio * sample_gains).astype(np.float32)
    except Exception as e:
        print(f"[DSP Noise Gate Warning] Noise gate failed: {e}")
        return audio

def preprocess_audio(
    audio: np.ndarray,
    target_rms: float = 0.08,
    max_gain: float = 8.0,
    sample_rate: int = 16000,
    enable_bandpass: bool = True,
    low_cutoff: float = 80.0,
    high_cutoff: float = 7500.0,
    enable_noise_gate: bool = True,
    noise_gate_threshold: float = 0.008,
    noise_gate_attack_ms: float = 10.0,
    noise_gate_release_ms: float = 50.0,
) -> np.ndarray:
    if len(audio) == 0:
        return audio

    centered = audio - np.mean(audio)

    if enable_bandpass and len(centered) >= 64:
        centered = apply_bandpass_filter(
            centered,
            sample_rate=sample_rate,
            low_cutoff=low_cutoff,
            high_cutoff=high_cutoff
        )

    if enable_noise_gate:
        centered = apply_noise_gate(
            centered,
            sample_rate=sample_rate,
            threshold=noise_gate_threshold,
            attack_ms=noise_gate_attack_ms,
            release_ms=noise_gate_release_ms
        )

    rms = float(np.sqrt(np.mean(centered**2)))

    if rms < 1e-4:
        return centered.astype(np.float32)

    if rms < target_rms:
        gain = min(max_gain, target_rms / rms)
        centered = centered * gain

    peak = float(np.max(np.abs(centered)))
    if peak > 0.95:
        centered = centered * (0.95 / peak)

    return centered.astype(np.float32)

def is_silence(audio: np.ndarray, threshold: float = 0.008) -> bool:
    if len(audio) == 0:
        return True
    rms = np.sqrt(np.mean(audio**2))
    return bool(rms < threshold)

def clean_transcribed_text(text: str) -> str:
    if not text:
        return ""
    cleaned = re.sub(r"\[.*?\]|\(.*?\)", "", text).strip()
    if not cleaned:
        return ""

    if re.fullmatch(r"[\s.,?!;:\"'\-—_~…]+", cleaned):
        return ""

    spam_hallucinations = {
        "thanks for watching", "thanks for watching.",
        "thank you for watching", "thank you for watching.",
        "subscribe to my channel", "subscribe to my channel.",
        "please subscribe", "please subscribe."
    }
    if cleaned.lower() in spam_hallucinations:
        return ""

    valid_double_words = {"had", "that"}
    words = cleaned.split()
    filtered_words = []
    for w in words:
        clean_w = re.sub(r"[^\w]", "", w).lower()
        prev_w = re.sub(r"[^\w]", "", filtered_words[-1]).lower() if filtered_words else ""
        prev2_w = re.sub(r"[^\w]", "", filtered_words[-2]).lower() if len(filtered_words) >= 2 else ""
        if not filtered_words:
            filtered_words.append(w)
        elif clean_w != prev_w:
            filtered_words.append(w)
        elif clean_w in valid_double_words and prev_w != prev2_w:
            filtered_words.append(w)
    cleaned = " ".join(filtered_words).strip()

    if not cleaned:
        return ""

    cleaned = cleaned[0].upper() + cleaned[1:]
    return cleaned

def get_optimal_device_settings():
    device = "cpu"
    compute_type = "int8"
    try:
        import torch
        if torch.cuda.is_available():
            device = "cuda"
            compute_type = "float16"
    except Exception:
        pass
    return device, compute_type

def load_whisper_model(model_name: str = "base.en", cpu_threads: int = 4):
    import os
    from faster_whisper import WhisperModel
    from faster_whisper.utils import download_model

    device, compute_type = get_optimal_device_settings()
    num_threads = cpu_threads if device == "cpu" else 0

    is_cached = False
    if os.path.isdir(model_name):
        is_cached = True
    else:
        try:
            download_model(model_name, local_files_only=True)
            is_cached = True
        except Exception:
            is_cached = False

    if not is_cached:
        if model_name != "base.en":
            print(f"[Transcriber] Model '{model_name}' is not in local cache.")
            print("[Transcriber] Falling back to pre-cached 'base.en' for instant startup...")
            model_name = "base.en"
        else:
            print(f"[Transcriber] Downloading '{model_name}' from Hugging Face Hub (first run only, please wait)...")

    print(f"[Transcriber] Initializing model '{model_name}' on {device} ({compute_type}, {num_threads} cpu threads)...")
    try:
        model = WhisperModel(model_name, device=device, compute_type=compute_type, cpu_threads=num_threads)
    except Exception as e:
        if model_name != "base.en":
            print(f"[Transcriber Warning] Could not load '{model_name}' ({e}). Falling back to 'base.en'...")
            model = WhisperModel("base.en", device=device, compute_type=compute_type, cpu_threads=num_threads)
        else:
            raise e
    print("[Transcriber] Model ready.")
    return model

class TranscriberWorker(threading.Thread):
    def __init__(
        self,
        audio_queue: queue.Queue,
        on_segment: Callable[[str, float, float], None],
        model=None,
        model_name: str = "base.en",
        sample_rate: int = 16000,
        min_speech_duration_sec: float = 0.25,
        max_speech_duration_sec: float = 3.2,
        silence_pause_sec: float = 0.22,
        silence_threshold: float = 0.008,
        enable_audio_boost: bool = True,
        target_rms: float = 0.08,
        beam_size: int = 1,
        initial_prompt: str | None = None,
        condition_on_previous_text: bool = False,
        cpu_threads: int = 4,
        enable_bandpass_filter: bool = True,
        bandpass_low_cutoff: float = 80.0,
        bandpass_high_cutoff: float = 7500.0,
        enable_noise_gate: bool = True,
        noise_gate_threshold: float = 0.008,
        noise_gate_attack_ms: float = 10.0,
        noise_gate_release_ms: float = 50.0,
        overlap_duration_sec: float = 0.3,
    ):
        super().__init__(daemon=True)
        self.audio_queue = audio_queue
        self.on_segment = on_segment
        self.model = model
        self.model_name = model_name
        self.sample_rate = sample_rate
        self.min_speech_sec = min_speech_duration_sec
        self.max_speech_sec = max_speech_duration_sec
        self.silence_pause_sec = silence_pause_sec
        self.silence_threshold = silence_threshold
        self.enable_audio_boost = enable_audio_boost
        self.target_rms = target_rms
        self.beam_size = beam_size
        self.initial_prompt = initial_prompt
        self.condition_on_previous_text = condition_on_previous_text
        self.cpu_threads = cpu_threads
        self.enable_bandpass_filter = enable_bandpass_filter
        self.bandpass_low_cutoff = bandpass_low_cutoff
        self.bandpass_high_cutoff = bandpass_high_cutoff
        self.enable_noise_gate = enable_noise_gate
        self.noise_gate_threshold = noise_gate_threshold
        self.noise_gate_attack_ms = noise_gate_attack_ms
        self.noise_gate_release_ms = noise_gate_release_ms
        self.overlap_sec = overlap_duration_sec

        self.running = False
        self.session_start_time = None
        self.last_context = ""
        self.stop_requested = threading.Event()
        self.paused = threading.Event()
        self.generation = 0
        self.on_error = None

    def set_paused(self, paused: bool):
        self.generation += 1
        if paused:
            self.paused.set()
        else:
            self.paused.clear()
        while True:
            try:
                self.audio_queue.get_nowait()
            except queue.Empty:
                break

    def run(self):
        if self.stop_requested.is_set():
            return
        if self.model is None:
            try:
                self.model = load_whisper_model(self.model_name, cpu_threads=self.cpu_threads)
            except Exception as e:
                print(f"[Transcriber Model Load Error] {e}")
                return

        self.running = True
        self.session_start_time = time.monotonic()

        speech_chunks = []
        recent_silence = []
        is_speaking = False
        speech_duration = 0.0
        silence_duration = 0.0
        speech_wall_start = 0.0

        empty_queue_count = 0
        generation = self.generation
        while not self.stop_requested.is_set():
            if self.generation != generation:
                generation = self.generation
                speech_chunks = []; recent_silence = []; is_speaking = False
                speech_duration = silence_duration = 0.0
                self.last_context = ""
            if self.paused.is_set():
                self.stop_requested.wait(0.05)
                continue
            chunk = None
            ready = False
            reached_max = False
            try:
                chunk = self.audio_queue.get(timeout=0.08)
                empty_queue_count = 0
            except queue.Empty:
                empty_queue_count += 1
                if is_speaking and empty_queue_count >= 5:
                    if speech_duration >= self.min_speech_sec and speech_chunks:
                        ready = True
                    else:
                        speech_chunks = []
                        is_speaking = False
                        speech_duration = 0.0
                        silence_duration = 0.0

            if chunk is not None:
                chunk_sec = len(chunk) / self.sample_rate
                # Compute DC-centered RMS directly on 50ms chunk for rapid VAD gating
                # Eliminates redundant 20x/sec FFT/iFFT bandpass calls that lock GIL.
                # Full bandpass filtering and noise gating are performed in preprocess_audio on the complete segment.
                eval_chunk = chunk - np.mean(chunk) if len(chunk) > 0 else chunk
                rms = float(np.sqrt(np.mean(eval_chunk**2))) if len(eval_chunk) > 0 else 0.0

                if rms >= self.silence_threshold:
                    if not is_speaking:
                        is_speaking = True
                        speech_wall_start = time.monotonic()
                        speech_chunks.extend(recent_silence[-5:])
                    speech_chunks.append(chunk)
                    speech_duration += chunk_sec
                    silence_duration = 0.0
                    recent_silence = []
                else:
                    recent_silence.append(chunk)
                    if len(recent_silence) > 8:
                        recent_silence.pop(0)

                    if is_speaking:
                        speech_chunks.append(chunk)
                        speech_duration += chunk_sec
                        silence_duration += chunk_sec

                if is_speaking:
                    if speech_duration >= self.min_speech_sec and silence_duration >= self.silence_pause_sec:
                        ready = True
                    elif silence_duration >= 0.55 and speech_duration >= 0.15:
                        ready = True
                    elif speech_duration >= self.max_speech_sec:
                        ready = True
                        reached_max = True

            if ready and speech_chunks:
                audio_to_transcribe = np.concatenate(speech_chunks)
                now = time.monotonic()
                rel_end = now - self.session_start_time
                rel_start = max(0.0, rel_end - (len(audio_to_transcribe) / self.sample_rate))

                if reached_max and self.overlap_sec > 0:
                    last_chunk_sec = len(speech_chunks[-1]) / self.sample_rate
                    overlap_frames = int(round(self.overlap_sec / last_chunk_sec))
                    if overlap_frames > 0 and len(speech_chunks) > overlap_frames:
                        speech_chunks = list(speech_chunks[-overlap_frames:])
                        speech_duration = len(speech_chunks) * last_chunk_sec
                        silence_duration = 0.0
                        is_speaking = True
                    else:
                        speech_chunks = []
                        is_speaking = False
                        speech_duration = 0.0
                        silence_duration = 0.0
                else:
                    speech_chunks = []
                    is_speaking = False
                    speech_duration = 0.0
                    silence_duration = 0.0

                try:
                    if self.enable_audio_boost:
                        audio_to_transcribe = preprocess_audio(
                            audio_to_transcribe,
                            target_rms=self.target_rms,
                            sample_rate=self.sample_rate,
                            enable_bandpass=self.enable_bandpass_filter,
                            low_cutoff=self.bandpass_low_cutoff,
                            high_cutoff=self.bandpass_high_cutoff,
                            enable_noise_gate=self.enable_noise_gate,
                            noise_gate_threshold=self.noise_gate_threshold,
                            noise_gate_attack_ms=self.noise_gate_attack_ms,
                            noise_gate_release_ms=self.noise_gate_release_ms,
                        )

                    transcribe_args = {
                        "language": "en",
                        "vad_filter": False,
                        "no_speech_threshold": 0.6,
                        "beam_size": self.beam_size,
                        "best_of": 1,
                        "temperature": 0.0,
                        "condition_on_previous_text": self.condition_on_previous_text,
                    }
                    prompt_parts = []
                    if self.initial_prompt:
                        prompt_parts.append(self.initial_prompt)
                    if self.last_context:
                        prompt_parts.append(self.last_context)
                    if prompt_parts:
                        transcribe_args["initial_prompt"] = " ".join(prompt_parts)

                    segments, _ = self.model.transcribe(
                        audio_to_transcribe,
                        **transcribe_args
                    )
                    for seg in segments:
                        raw_text = seg.text.strip()
                        cleaned_text = clean_transcribed_text(raw_text)
                        if cleaned_text and generation == self.generation and not self.paused.is_set() and not self.stop_requested.is_set():
                            seg_start = rel_start + seg.start
                            seg_end = rel_start + seg.end
                            self.last_context = cleaned_text[-120:]
                            self.on_segment(cleaned_text, seg_start, seg_end)
                except Exception as e:
                    if self.on_error:
                        self.on_error(str(e))

    def stop(self):
        self.running = False
        self.stop_requested.set()
