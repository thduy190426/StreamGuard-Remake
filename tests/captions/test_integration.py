import queue
import threading
from types import SimpleNamespace
import numpy as np
import pytest
from app import audio_capture
from app.transcriber import TranscriberWorker
from app import transcriber


def test_caption_timeline_ignores_wall_clock_adjustment(monkeypatch):
    audio = queue.Queue()
    tone = (np.sin(np.arange(800) * (2 * np.pi * 440 / 16000)) * 0.1).astype(np.float32)
    for _ in range(8): audio.put(tone)
    for _ in range(10): audio.put(np.zeros(800, np.float32))
    wall = iter([1000.0, 5000.0, 5001.0, 5002.0])
    monotonic = iter([10.0, 10.5, 11.0, 11.5])
    monkeypatch.setattr(transcriber, "time", SimpleNamespace(
        time=lambda: next(wall), monotonic=lambda: next(monotonic)))
    class Model:
        def transcribe(self, *args, **kwargs):
            return [SimpleNamespace(text="Clock independent caption", start=0, end=0.5)], None
    results = []
    def segment(*values):
        results.append(values); worker.stop()
    worker = TranscriberWorker(audio, segment, model=Model(), enable_audio_boost=False)
    worker.run()
    assert len(results) == 1 and 0 <= results[0][1] < results[0][2] < 5


def test_capture_sanitizes_invalid_device_samples(monkeypatch):
    audio = queue.Queue()
    capture = audio_capture.AudioLoopbackCapture(audio)
    class Recorder:
        def __enter__(self): return self
        def __exit__(self, *args): return False
        def record(self, **kwargs):
            capture.stop()
            return np.array([[np.nan, 0.2], [np.inf, 0.4], [-np.inf, 0.6]], dtype=np.float32)
    class Device:
        name = "Test device with invalid samples"
        def recorder(self, **kwargs): return Recorder()
    monkeypatch.setattr(audio_capture, "get_loopback_microphone", lambda: Device())
    capture.run()
    samples = audio.get_nowait()
    assert np.isfinite(samples).all() and np.max(np.abs(samples)) <= 1


def test_missing_loopback_never_opens_physical_microphone(monkeypatch):
    import soundcard
    physical = []
    monkeypatch.setattr(soundcard, "default_speaker", lambda: SimpleNamespace(id="speaker", name="Speaker"))
    monkeypatch.setattr(soundcard, "all_microphones", lambda **kwargs: [])
    monkeypatch.setattr(soundcard, "default_microphone", lambda: physical.append(True))
    with pytest.raises(RuntimeError, match="Microphone capture is disabled"):
        audio_capture.get_loopback_microphone()
    assert physical == []


def test_loopback_failure_does_not_fall_back_to_another_device(monkeypatch):
    import soundcard
    monkeypatch.setattr(soundcard, "default_speaker", lambda: (_ for _ in ()).throw(OSError("No output")))
    monkeypatch.setattr(soundcard, "default_microphone", lambda: pytest.fail("Physical microphone was accessed"))
    with pytest.raises(RuntimeError, match="No output"):
        audio_capture.get_loopback_microphone()


def test_an_unrelated_playback_device_is_not_selected(monkeypatch):
    import soundcard
    monkeypatch.setattr(soundcard, "default_speaker", lambda: SimpleNamespace(id="headphones", name="Headphones"))
    monkeypatch.setattr(soundcard, "all_microphones", lambda **kwargs: [SimpleNamespace(id="other", name="Other Speakers", isloopback=True)])
    with pytest.raises(RuntimeError, match="default playback output"):
        audio_capture.get_loopback_microphone()


def test_capture_downmixes_and_bounds_backlog(monkeypatch):
    audio = queue.Queue(maxsize=2)
    samples = np.tile(np.array([0.2, 0.6], np.float32), (800, 1))
    calls = []
    capture = audio_capture.AudioLoopbackCapture(audio)
    class Recorder:
        def __enter__(self): return self
        def __exit__(self, *args): return False
        def record(self, numframes):
            calls.append(numframes)
            if len(calls) == 8: capture.stop()
            return samples
    class Device:
        name = "Test-owned loopback"
        def recorder(self, **kwargs):
            assert kwargs["channels"] is None
            return Recorder()
    monkeypatch.setattr(audio_capture, "get_loopback_microphone", lambda: Device())
    capture.run()
    assert len(calls) == 8 and audio.qsize() == 2
    assert np.allclose(audio.get_nowait(), 0.4)


def test_capture_stop_before_start_cannot_restart_itself(monkeypatch):
    monkeypatch.setattr(audio_capture, "get_loopback_microphone", lambda: pytest.fail("Stopped capture opened a device"))
    capture = audio_capture.AudioLoopbackCapture(queue.Queue())
    capture.stop(); capture.run()
    assert not capture.running


def test_capture_errors_are_reported_to_owner(monkeypatch):
    monkeypatch.setattr(audio_capture, "get_loopback_microphone", lambda: (_ for _ in ()).throw(OSError("Test device removed")))
    errors = []
    capture = audio_capture.AudioLoopbackCapture(queue.Queue(), on_error=errors.append)
    capture.run()
    assert errors == ["Test device removed"] and not capture.running


def test_pause_clears_backlog_and_advances_generation():
    audio = queue.Queue(); audio.put(np.ones(100))
    worker = TranscriberWorker(audio, lambda *args: None, model=object())
    worker.set_paused(True)
    assert worker.paused.is_set() and worker.generation == 1 and audio.empty()
    worker.set_paused(False)
    assert not worker.paused.is_set() and worker.generation == 2
    worker.stop(); worker.run()
    assert not worker.running


def test_pause_drops_an_in_flight_result():
    audio = queue.Queue()
    tone = (np.sin(np.arange(800) * (2 * np.pi * 440 / 16000)) * 0.1).astype(np.float32)
    for _ in range(8): audio.put(tone)
    for _ in range(10): audio.put(np.zeros(800, np.float32))
    started = threading.Event(); release = threading.Event(); results = []
    class Model:
        def transcribe(self, *args, **kwargs):
            started.set(); release.wait(2)
            return [SimpleNamespace(text="Old audio", start=0, end=1)], None
    worker = TranscriberWorker(audio, lambda *args: results.append(args), model=Model(), enable_audio_boost=False)
    worker.start()
    try:
        assert started.wait(2)
        worker.set_paused(True); release.set()
        worker.stop(); worker.join(2)
        assert results == [] and not worker.is_alive()
    finally:
        release.set(); worker.stop(); worker.join(2)
