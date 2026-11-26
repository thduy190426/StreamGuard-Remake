import numpy as np
from app.transcriber import is_silence, get_optimal_device_settings, clean_transcribed_text

def test_silence_detection():
    # Audio with very low energy should be detected as silence
    silent = np.zeros(16000, dtype=np.float32)
    assert is_silence(silent, threshold=0.01) is True

    # Noise below threshold
    subtle_noise = np.random.normal(0, 0.002, 16000).astype(np.float32)
    assert is_silence(subtle_noise, threshold=0.01) is True

    # Loud speech audio above threshold
    loud_speech = np.random.normal(0, 0.2, 16000).astype(np.float32)
    assert is_silence(loud_speech, threshold=0.01) is False

def test_device_settings():
    device, compute_type = get_optimal_device_settings()
    assert device in ["cuda", "cpu"]
    assert compute_type in ["float16", "int8"]

def test_clean_transcribed_text():
    # Removes tags like [Music]
    assert clean_transcribed_text("[Music] Welcome to our show.") == "Welcome to our show."
    # Suppresses hallucinated YouTube spam artifacts
    assert clean_transcribed_text("thanks for watching") == ""
    assert clean_transcribed_text("subscribe to my channel") == ""
    # Preserves legitimate short phrases and common words
    assert clean_transcribed_text("thank you.") == "Thank you."
    assert clean_transcribed_text("yes") == "Yes"
    assert clean_transcribed_text("...") == ""
    # Collapses consecutive duplicates
    assert clean_transcribed_text("this is very very important") == "This is very important"
    # Preserves valid English double occurrences but stops run-on loops
    assert clean_transcribed_text("I had had enough") == "I had had enough"
    assert clean_transcribed_text("I had had had enough") == "I had had enough"
    assert clean_transcribed_text("he said that that was true") == "He said that that was true"
    # Capitalizes initial letter
    assert clean_transcribed_text("good morning everyone") == "Good morning everyone"

def test_transcriber_worker_init_settings():
    from app.transcriber import TranscriberWorker
    import queue
    q = queue.Queue()
    worker = TranscriberWorker(
        audio_queue=q,
        on_segment=lambda t, s, e: None,
        model_name="distil-small.en",
        cpu_threads=4
    )
    assert worker.model_name == "distil-small.en"
    assert worker.cpu_threads == 4
    assert worker.enable_bandpass_filter is True
    assert worker.bandpass_low_cutoff == 80.0
    assert worker.bandpass_high_cutoff == 7500.0
    assert worker.enable_noise_gate is True
    assert worker.noise_gate_threshold == 0.008
    assert worker.min_speech_sec == 0.25
    assert worker.max_speech_sec == 3.2
    assert worker.silence_pause_sec == 0.22
    assert worker.overlap_sec == 0.3

def test_load_whisper_model_uncached_fallback(monkeypatch):
    from unittest.mock import MagicMock
    import app.transcriber as transcriber_mod

    # Mock download_model to simulate uncached model
    def mock_download(model, local_files_only=False):
        if model != "base.en":
            raise FileNotFoundError(f"{model} not found locally")
        return "/mock/base.en"

    mock_whisper = MagicMock()
    monkeypatch.setattr("faster_whisper.utils.download_model", mock_download)
    monkeypatch.setattr("faster_whisper.WhisperModel", mock_whisper)

    # Calling with uncached distil-small.en should fall back to base.en
    model = transcriber_mod.load_whisper_model("distil-small.en", cpu_threads=4)
    assert model is not None
    mock_whisper.assert_called_with("base.en", device="cpu", compute_type="int8", cpu_threads=4)

