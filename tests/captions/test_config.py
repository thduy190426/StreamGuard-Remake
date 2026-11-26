from app.config import AppConfig

def test_config_defaults():
    config = AppConfig()
    assert config.sample_rate == 16000
    assert config.whisper_model == "base.en"
    assert config.cpu_threads == 4
    assert config.output_dir == "transcripts"
    assert config.state_file == "window_state.json"
    assert config.font_size >= 14
    path = config.get_output_path()
    assert path.exists()
    assert config.enable_audio_boost is True
    assert config.target_rms == 0.08
    assert config.beam_size in (1, 2)
    assert "English" in config.initial_prompt
    assert config.condition_on_previous_text is False
    assert config.enable_bandpass_filter is True
    assert config.bandpass_low_cutoff == 80.0
    assert config.bandpass_high_cutoff == 7500.0
    assert config.enable_noise_gate is True
    assert config.noise_gate_attack_ms == 10.0
    assert config.noise_gate_release_ms == 50.0
    assert config.anti_screen_capture is True
    assert config.min_speech_duration_sec == 0.25
    assert config.max_speech_duration_sec == 3.2
    assert config.silence_pause_sec == 0.22
    assert config.overlap_duration_sec == 0.3
