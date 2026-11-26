import sys
import threading
import queue
import time
import warnings
from typing import Callable
import numpy as np

try:
    import soundcard as sc
    if hasattr(sc, "SoundcardRuntimeWarning"):
        warnings.filterwarnings("ignore", category=sc.SoundcardRuntimeWarning)
except Exception:
    pass
warnings.filterwarnings("ignore", message=".*data discontinuity.*")

class AudioBufferManager:
    def __init__(self, sample_rate: int = 16000, min_chunk_sec: float = 0.6):
        self.sample_rate = sample_rate
        self.min_samples = int(sample_rate * min_chunk_sec)
        self.buffer = []
        self.total_samples = 0

    def add_data(self, chunk: np.ndarray) -> np.ndarray | None:
        if chunk.ndim > 1:
            chunk = np.mean(chunk, axis=1)
        self.buffer.append(chunk.astype(np.float32))
        self.total_samples += len(chunk)

        if self.total_samples >= self.min_samples:
            combined = np.concatenate(self.buffer)
            self.buffer = []
            self.total_samples = 0
            return combined
        return None

def get_loopback_microphone():
    co_initialized = False
    if sys.platform == "win32":
        try:
            import ctypes
            hr = ctypes.windll.ole32.CoInitialize(None)
            if hr in (0, 1):
                co_initialized = True
        except Exception:
            pass

    try:
        import soundcard as sc
        default_spk = sc.default_speaker()
        loopbacks = [m for m in sc.all_microphones(include_loopback=True) if getattr(m, 'isloopback', False)]

        for m in loopbacks:
            if m.id == default_spk.id:
                return m

        if hasattr(default_spk, 'name'):
            for m in loopbacks:
                if default_spk.name in m.name:
                    return m

        raise RuntimeError("No loopback device matches the default playback output. Microphone capture is disabled.")
    except Exception as e:
        raise RuntimeError(f"System playback device unavailable: {e}") from e
    finally:
        if co_initialized:
            try:
                import ctypes
                ctypes.windll.ole32.CoUninitialize()
            except Exception:
                pass

class AudioLoopbackCapture(threading.Thread):
    def __init__(
        self,
        audio_queue: queue.Queue,
        sample_rate: int = 16000,
        block_duration: float = 0.05,
        on_level: Callable[[float], None] | None = None
        , on_error: Callable[[str], None] | None = None
    ):
        super().__init__(daemon=True)
        self.audio_queue = audio_queue
        self.sample_rate = sample_rate
        self.block_size = int(sample_rate * block_duration)
        self.on_level = on_level
        self.on_error = on_error
        self.stop_requested = threading.Event()
        self.running = False
        self.paused = False
        self.device_name = "Default Loopback"

    def run(self):
        if self.stop_requested.is_set():
            return
        self.running = True
        co_initialized = False
        if sys.platform == "win32":
            try:
                import ctypes
                hr = ctypes.windll.ole32.CoInitialize(None)
                if hr in (0, 1):
                    co_initialized = True
            except Exception:
                pass

        try:
            mic = get_loopback_microphone()
            self.device_name = mic.name if hasattr(mic, 'name') else "Loopback Device"
            print(f"[AudioCapture] Capturing loopback from: {self.device_name} ({self.sample_rate}Hz)")

            # WASAPI can return invalid samples when requesting a single channel.
            # Read all playback channels and downmix; never open a physical microphone.
            with mic.recorder(samplerate=self.sample_rate, channels=None, blocksize=self.block_size) as recorder:
                while not self.stop_requested.is_set():
                    data = recorder.record(numframes=self.block_size)
                    if not self.paused:
                        # Invalid WASAPI samples must not poison the meter, FFT or model.
                        # Sanitize channels before downmixing so valid channels are retained.
                        data = np.clip(np.nan_to_num(data, nan=0.0, posinf=0.0, neginf=0.0), -1.0, 1.0)
                        mono = np.mean(data, axis=1) if data.ndim > 1 else data
                        mono_f32 = mono.astype(np.float32)
                        try:
                            self.audio_queue.put_nowait(mono_f32)
                        except queue.Full:
                            # Keep latency and memory bounded if recognition falls behind.
                            try:
                                self.audio_queue.get_nowait()
                            except queue.Empty:
                                pass
                            try:
                                self.audio_queue.put_nowait(mono_f32)
                            except queue.Full:
                                pass

                        if self.on_level:
                            rms = float(np.sqrt(np.mean(mono_f32**2)))
                            level = min(1.0, rms * 18.0)
                            self.on_level(level)
                    else:
                        if self.on_level:
                            self.on_level(0.0)
        except Exception as e:
            if self.on_error:
                self.on_error(str(e))
        finally:
            self.running = False
            if co_initialized:
                try:
                    import ctypes
                    ctypes.windll.ole32.CoUninitialize()
                except Exception:
                    pass

    def pause(self):
        self.paused = True

    def resume(self):
        self.paused = False

    def stop(self):
        self.running = False
        self.stop_requested.set()
