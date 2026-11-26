from dataclasses import dataclass
from pathlib import Path

@dataclass
class AppConfig:
    sample_rate: int = 16000
    whisper_model: str = "base.en"
    cpu_threads: int = 4
    output_dir: str = "transcripts"
    font_size: int = 18
    min_font_size: int = 12
    max_font_size: int = 30
    text_color: str = "#FFFFFF"
    prev_text_color: str = "#9CA3AF"
    background_opacity: float = 0.78
    state_file: str = "window_state.json"
    anti_screen_capture: bool = True
    
    min_speech_duration_sec: float = 0.25
    max_speech_duration_sec: float = 3.2
    silence_pause_sec: float = 0.22
    silence_threshold: float = 0.008
    overlap_duration_sec: float = 0.3

    enable_audio_boost: bool = True
    target_rms: float = 0.08
    beam_size: int = 1
    initial_prompt: str = "Hello, welcome to this English broadcast. Clear spoken English with proper vocabulary, punctuation, and capitalization."
    condition_on_previous_text: bool = False

    enable_bandpass_filter: bool = True
    bandpass_low_cutoff: float = 80.0
    bandpass_high_cutoff: float = 7500.0
    enable_noise_gate: bool = True
    noise_gate_threshold: float = 0.008
    noise_gate_attack_ms: float = 10.0
    noise_gate_release_ms: float = 50.0

    def get_output_path(self) -> Path:
        path = Path(self.output_dir)
        path.mkdir(parents=True, exist_ok=True)
        return path
