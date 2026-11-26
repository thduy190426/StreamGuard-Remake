import numpy as np
from app.audio_capture import AudioBufferManager

def test_audio_buffer_manager():
    buf = AudioBufferManager(sample_rate=16000, min_chunk_sec=0.5)
    # Feed 0.25s of audio (4000 samples)
    data1 = np.zeros(4000, dtype=np.float32)
    ready1 = buf.add_data(data1)
    assert ready1 is None

    # Feed another 0.3s (4800 samples) -> total 8800 samples (> 8000 = 0.5s)
    data2 = np.ones(4800, dtype=np.float32)
    ready2 = buf.add_data(data2)
    assert ready2 is not None
    assert len(ready2) == 8800

def test_audio_buffer_stereo_to_mono():
    buf = AudioBufferManager(sample_rate=16000, min_chunk_sec=0.1)
    # 2 channels
    stereo_data = np.ones((2000, 2), dtype=np.float32)
    ready = buf.add_data(stereo_data)
    assert ready is not None
    assert ready.ndim == 1
    assert len(ready) == 2000
