import numpy as np
import pytest
from app.transcriber import preprocess_audio

def test_preprocess_dc_offset():
    # Audio with constant DC bias
    raw = np.ones(16000, dtype=np.float32) * 0.05 + np.random.normal(0, 0.02, 16000).astype(np.float32)
    processed = preprocess_audio(raw, target_rms=0.08)
    # Mean of processed audio should be practically zero
    assert abs(np.mean(processed)) < 1e-3

def test_preprocess_boosts_quiet_speech():
    # Quiet speech with RMS ~ 0.015
    np.random.seed(42)
    raw = np.random.normal(0, 0.015, 16000).astype(np.float32)
    initial_rms = np.sqrt(np.mean(raw**2))
    processed = preprocess_audio(raw, target_rms=0.08)
    final_rms = np.sqrt(np.mean(processed**2))
    
    # Processed audio RMS should be significantly higher than initial
    assert final_rms > initial_rms * 2.0
    # Peak must not clip
    assert np.max(np.abs(processed)) <= 0.96

def test_preprocess_does_not_boost_pure_silence():
    # True digital silence should remain near zero
    raw = np.zeros(16000, dtype=np.float32)
    processed = preprocess_audio(raw, target_rms=0.08)
    assert np.max(np.abs(processed)) == 0.0

def test_preprocess_prevents_clipping():
    # Extra loud signal with peaks up to 1.5
    raw = np.random.normal(0, 0.5, 16000).astype(np.float32)
    processed = preprocess_audio(raw, target_rms=0.08)
    assert np.max(np.abs(processed)) <= 0.96

def test_bandpass_filter_attenuates_rumble_and_preserves_speech():
    from app.transcriber import apply_bandpass_filter
    sr = 16000
    t = np.linspace(0, 1.0, sr, endpoint=False)
    
    # 40Hz rumble (sub-bass / fan)
    rumble = (0.1 * np.sin(2 * np.pi * 40 * t)).astype(np.float32)
    rumble_filtered = apply_bandpass_filter(rumble, sample_rate=sr, low_cutoff=80.0, high_cutoff=7500.0)
    
    # 1000Hz speech tone
    speech = (0.1 * np.sin(2 * np.pi * 1000 * t)).astype(np.float32)
    speech_filtered = apply_bandpass_filter(speech, sample_rate=sr, low_cutoff=80.0, high_cutoff=7500.0)
    
    rumble_rms_before = np.sqrt(np.mean(rumble**2))
    rumble_rms_after = np.sqrt(np.mean(rumble_filtered**2))
    speech_rms_before = np.sqrt(np.mean(speech**2))
    speech_rms_after = np.sqrt(np.mean(speech_filtered**2))
    
    # 40Hz rumble should be attenuated by over 80%
    assert rumble_rms_after < rumble_rms_before * 0.20
    # 1000Hz speech tone should be almost completely preserved (> 95%)
    assert speech_rms_after > speech_rms_before * 0.95

def test_noise_gate_mutes_breathing_and_preserves_speech():
    from app.transcriber import apply_noise_gate
    sr = 16000
    t = np.linspace(0, 1.0, sr, endpoint=False)
    
    # Quiet breathing / keyboard clicks with RMS around 0.003 (below 0.008 threshold)
    quiet_breathing = (np.random.normal(0, 0.003, sr)).astype(np.float32)
    gated_breathing = apply_noise_gate(quiet_breathing, sample_rate=sr, threshold=0.008)
    
    # Active spoken audio with RMS around 0.05 (well above 0.008 threshold)
    active_speech = (0.05 * np.sin(2 * np.pi * 300 * t)).astype(np.float32)
    gated_speech = apply_noise_gate(active_speech, sample_rate=sr, threshold=0.008)
    
    breath_rms_before = np.sqrt(np.mean(quiet_breathing**2))
    breath_rms_after = np.sqrt(np.mean(gated_breathing**2))
    speech_rms_before = np.sqrt(np.mean(active_speech**2))
    speech_rms_after = np.sqrt(np.mean(gated_speech**2))
    
    # Breathing should be muted by over 20dB (attenuated to less than 10% of original)
    assert breath_rms_after < breath_rms_before * 0.10
    # Active speech should remain intact (> 90%)
    assert speech_rms_after > speech_rms_before * 0.90

def test_dsp_edge_cases():
    from app.transcriber import apply_bandpass_filter, apply_noise_gate
    # Empty inputs
    assert len(apply_bandpass_filter(np.array([], dtype=np.float32))) == 0
    assert len(apply_noise_gate(np.array([], dtype=np.float32))) == 0
    # Short inputs (< 64 samples)
    short = np.array([0.05, -0.05], dtype=np.float32)
    assert len(apply_bandpass_filter(short)) == 2
    assert len(apply_noise_gate(short)) == 2
