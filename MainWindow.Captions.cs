using System.Windows;
using System.Windows.Controls;
using StreamGuard.Localization;
using StreamGuard.Models;
using StreamGuard.Services;

namespace StreamGuard;

public partial class MainWindow
{
    private CaptionService? _captions;
    private CaptionSettings _captionSettings = new();
    private readonly CaptionTranslationService _captionTranslation = new();
    private readonly CaptionVocabularyService _captionVocabulary = new();
    private int _captionTranslationHotkeyId = -1;
    private CaptionOverlayWindow? _captionOverlay;
    private bool _captionOverlayHiddenWithMain;
    private string _latestCaption = "";
    private bool _captionUiReady;
    private bool _captionSettingsSaveFailed;
    private double _pendingCaptionLevel;
    private int _captionLevelQueued;
    private void InitializeCaptions(CaptionService? service = null)
    {
        _captionSettings = CaptionSettings.Load(_config.CaptionSettingsPath);
        _captionVocabulary.UpdateKeywords(_captionSettings.CustomKeywords);
        _captions = service ?? new CaptionService(_config.CaptionTranscriptDirectory);
        _captions.Translator = async text =>
        {
            if (!_captionSettings.EnableTranslation) return null;
            var corrected = _captionVocabulary.CorrectText(text);
            var translated = await _captionTranslation.TranslateToVietnameseAsync(corrected).ConfigureAwait(false);
            return _captionVocabulary.PreserveJargonInTranslation(corrected, translated);
        };
        _captions.Changed += CaptionStateChanged;
        _captions.Segment += CaptionSegmentReceived;
        _captions.SegmentTranslated += CaptionSegmentTranslatedReceived;
        _captions.Level += CaptionLevelReceived;
        CaptionSingleLineCheckBox.IsChecked = _captionSettings.SingleLine;
        CaptionCaptureCheckBox.IsChecked = _captionSettings.ExcludeFromCapture;
        if (CaptionTranslationCheckBox != null)
            CaptionTranslationCheckBox.IsChecked = _captionSettings.EnableTranslation;
        if (CaptionModeBilingualRadio != null)
            CaptionModeBilingualRadio.IsChecked = _captionSettings.TranslationMode == "Bilingual";
        if (CaptionModeVietnameseRadio != null)
            CaptionModeVietnameseRadio.IsChecked = _captionSettings.TranslationMode == "VietnameseOnly";
        if (CaptionCustomKeywordsTextBox != null)
        {
            CaptionCustomKeywordsTextBox.Text = _captionSettings.CustomKeywords;
            UpdateCaptionVocabularyButtonsState();
        }
        RegisterCaptionTranslationHotkey();
        _captionUiReady = true; UpdateCaptionUi();
        InitializeTranscriptReader();
    }
    private void UpdateCaptionVocabularyButtonsState()
    {
        var rawText = CaptionCustomKeywordsTextBox?.Text;
        var hasText = !string.IsNullOrWhiteSpace(rawText);
        if (BtnCaptionClearKeywords != null)
            BtnCaptionClearKeywords.IsEnabled = hasText;

        if (BtnCaptionSaveKeywords != null)
        {
            var isValid = CaptionVocabularyService.TryValidateKeywords(rawText, out _, out var errKey);
            BtnCaptionSaveKeywords.IsEnabled = isValid;
            BtnCaptionSaveKeywords.ToolTip = isValid
                ? LocalizationService.GetString("Captions.SaveVocabularyTooltip", "Lưu từ điển từ khóa hợp lệ để chuẩn hóa phụ đề và bản dịch")
                : LocalizationService.GetString(errKey, "Vui lòng nhập từ khóa hợp lệ (không để trống, mỗi từ >= 2 ký tự, cách nhau bằng dấu phẩy)");
        }
    }
    private void CaptionCustomKeywordsTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateCaptionVocabularyButtonsState();
    }
    private void BtnCaptionClearKeywords_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanupDone || CaptionCustomKeywordsTextBox == null) return;
        CaptionCustomKeywordsTextBox.Text = string.Empty;
        _captionSettings.CustomKeywords = string.Empty;
        _captionVocabulary.UpdateKeywords(string.Empty);
        SaveCaptionSettings();
        UpdateCaptionVocabularyButtonsState();
        UpdateStatus(LocalizationService.GetString("Captions.KeywordsClearedNotice", "Đã xóa toàn bộ từ điển từ khóa và thuật ngữ riêng"), true);
    }
    private void BtnCaptionSaveKeywords_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanupDone || CaptionCustomKeywordsTextBox == null) return;
        if (!CaptionVocabularyService.TryValidateKeywords(CaptionCustomKeywordsTextBox.Text, out var normalized, out var errKey))
        {
            UpdateCaptionVocabularyButtonsState();
            UpdateStatus(LocalizationService.GetString(errKey, "Từ điển không hợp lệ hoặc đang để trống."), false);
            return;
        }
        _captionSettings.CustomKeywords = normalized;
        if (CaptionCustomKeywordsTextBox.Text != normalized)
            CaptionCustomKeywordsTextBox.Text = normalized;
        _captionVocabulary.UpdateKeywords(_captionSettings.CustomKeywords);
        SaveCaptionSettings();
        UpdateCaptionVocabularyButtonsState();
        UpdateStatus(LocalizationService.GetString("Captions.KeywordsSavedNotice", "Đã lưu từ điển từ khóa chuyên ngành cho phụ đề"), true);
    }
    private void RegisterCaptionTranslationHotkey()
    {
        if (_captionTranslationHotkeyId >= 0)
        {
            _hotkeys.Unregister(_captionTranslationHotkeyId);
            _captionTranslationHotkeyId = -1;
        }
        _captionTranslationHotkeyId = _hotkeys.Register(
            HotkeyService.MOD_CTRL | HotkeyService.MOD_ALT,
            0x54, // 'T' (Ctrl+Alt+T)
            ToggleCaptionTranslation);
    }
    public void ToggleCaptionTranslation()
    {
        if (_cleanupDone) return;
        _captionSettings.EnableTranslation = !_captionSettings.EnableTranslation;
        if (CaptionTranslationCheckBox != null)
            CaptionTranslationCheckBox.IsChecked = _captionSettings.EnableTranslation;
        _captionOverlay?.ApplySettings();
        SaveCaptionSettings();
        var msg = _captionSettings.EnableTranslation
            ? LocalizationService.GetString("Captions.TranslationEnabledNotice", "Đã bật dịch phụ đề thời gian thực sang Tiếng Việt (Ctrl+Alt+T)")
            : LocalizationService.GetString("Captions.TranslationDisabledNotice", "Đã tắt dịch phụ đề thời gian thực (Ctrl+Alt+T)");
        UpdateStatus(msg, _captionSettings.EnableTranslation);
    }
    private void PostCaptionUpdate(Action update)
    {
        if (_cleanupDone || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        try { Dispatcher.BeginInvoke(() => { if (!_cleanupDone) update(); }); }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) { }
    }
    private void CaptionStateChanged() => PostCaptionUpdate(() =>
    {
        UpdateCaptionUi();
        if (CaptionTranscriptPanel.Visibility == Visibility.Visible)
        {
            if (TranscriptFollowCheckBox.IsChecked == true) _ = LoadTranscriptAsync(refreshList: true, followCurrent: true);
            else if (!_transcriptLoading) UpdateTranscriptReadStatus();
        }
    });
    private void CaptionSegmentReceived(CaptionSegment segment) => PostCaptionUpdate(() =>
    {
        if (_cleanupDone) return;
        var correctedText = _captionVocabulary.CorrectText(segment.Text);
        _latestCaption = correctedText;

        // Stage 1: Display English text instantly (< 10ms)
        CaptionPreviewText.Text = correctedText;
        _captionOverlay?.UpdateSubtitle(correctedText, segment.Translation);
    });
    private void CaptionSegmentTranslatedReceived(CaptionSegment segment) => PostCaptionUpdate(() =>
    {
        if (_cleanupDone) return;
        var correctedText = _captionVocabulary.CorrectText(segment.Text);
        var translation = segment.Translation != null
            ? _captionVocabulary.PreserveJargonInTranslation(correctedText, segment.Translation)
            : null;

        // Stage 2: Fill in translation when ready
        if (!string.IsNullOrWhiteSpace(translation) && !translation.Equals(correctedText, StringComparison.OrdinalIgnoreCase))
        {
            CaptionPreviewText.Text = $"{correctedText}\n-> {translation}";
            _captionOverlay?.UpdateTranslation(correctedText, translation);
        }

        // Live stream directly to Transcript Viewer in RAM (0ms delay, no disk read)
        AppendLiveSegment(correctedText, translation, segment.Start, segment.End);
    });
    private void CaptionLevelReceived(double value)
    {
        if (_cleanupDone || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        System.Threading.Volatile.Write(ref _pendingCaptionLevel, value);
        if (System.Threading.Interlocked.Exchange(ref _captionLevelQueued, 1) != 0) return;
        try
        {
            // A busy dispatcher needs the latest meter value, not an unbounded backlog.
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                System.Threading.Interlocked.Exchange(ref _captionLevelQueued, 0);
                if (_cleanupDone) return;
                var latest = System.Threading.Volatile.Read(ref _pendingCaptionLevel);
                CaptionAudioLevel.Value = latest; _captionOverlay?.UpdateLevel(latest);
            }));
        }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        { System.Threading.Interlocked.Exchange(ref _captionLevelQueued, 0); }
    }
    private void UpdateCaptionUi()
    {
        if (!_captionUiReady || _captions == null) return;
        var state = _captions.State;
        LocalizationService.BindKey(CaptionStatusText, TextBlock.TextProperty, "Captions." + state);
        UpdateCaptionError();
        BtnCaptionStart.IsEnabled = _captions.CanStart;
        BtnCaptionStop.IsEnabled = state is not (CaptionState.Stopped or CaptionState.Stopping);
        BtnCaptionPause.IsEnabled = state is CaptionState.Running or CaptionState.Paused;
        LocalizationService.BindKey(BtnCaptionPause, ContentControl.ContentProperty, state == CaptionState.Paused ? "Captions.Resume" : "Captions.Pause");
        Controls.Icon.SetData(BtnCaptionPause, (System.Windows.Media.Geometry)FindResource(state == CaptionState.Paused ? "UiIconPlay" : "UiIconPause"));
        _captionOverlay?.SetStatus(state);
        if (state is CaptionState.Stopped or CaptionState.Faulted) { _captionOverlayHiddenWithMain = false; _captionOverlay?.Hide(); CaptionAudioLevel.Value = 0; }
        UpdateCaptionCaptureStatus();
    }
    private void EnsureCaptionOverlay()
    {
        if (_captionOverlay != null) return;
        _captionOverlay = new CaptionOverlayWindow(_captionSettings);
        _cursorCaptureProtection.AttachInAppCloak(_captionOverlay);
        _captionOverlay.SetClickThrough(CaptionClickThroughCheckBox.IsChecked == true);
        _captionOverlay.PauseRequested += ToggleCaptionPause;
        _captionOverlay.HideRequested += () => { _captionOverlayHiddenWithMain = false; _captionOverlay?.Hide(); };
        _captionOverlay.StopRequested += () => _ = StopCaptionsAsync();
        _captionOverlay.ToggleTranslationRequested += ToggleCaptionTranslation;
        _captionOverlay.SettingsChanged += SaveCaptionSettings;
        _captionOverlay.SourceInitialized += (_, _) => { if (!_cleanupDone) UpdateCaptionCaptureStatus(); };
        _captionOverlay.Closed += (_, _) => { _captionOverlayHiddenWithMain = false; _captionOverlay = null; };
        if (_latestCaption.Length != 0) _captionOverlay.UpdateSubtitle(_latestCaption);
    }
    private void ReinforceCaptionOverlayProtection()
    {
        if (_captionOverlay == null || _cleanupDone) return;
        _captionOverlay.ApplyNativeStyle();
        if (_captionOverlay.RefreshCaptureProtection())
            UpdateCaptionCaptureStatus();
    }
    private void BtnCaptionStart_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanupDone || _captions == null) return;
        if (_captions.Start())
        {
            _captionOverlayHiddenWithMain = false;
            EnsureCaptionOverlay(); _captionOverlay!.Show(); _captionOverlay.ApplySettings();
            _captionOverlay.SetClickThrough(CaptionClickThroughCheckBox.IsChecked == true);
            UpdateCaptionUi();
        }
    }
    private async Task StopCaptionsAsync()
    {
        if (_captions == null) return;
        _captionOverlayHiddenWithMain = false;
        _captionOverlay?.Hide(); await _captions.StopAsync();
        if (!_cleanupDone) UpdateCaptionUi();
    }
    private async void BtnCaptionStop_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanupDone) return;
        try { await StopCaptionsAsync(); }
        catch (Exception ex) { if (!_cleanupDone) UpdateStatus($"Caption stop error: {ex.Message}", false); }
    }
    private void ToggleCaptionPause()
    { if (!_cleanupDone && _captions != null) _captions.SetPaused(!_captions.RequestedPaused); }
    private void BtnCaptionPause_Click(object sender, RoutedEventArgs e) => ToggleCaptionPause();
    private void BtnCaptionShow_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanupDone) return;
        _captionOverlayHiddenWithMain = false;
        EnsureCaptionOverlay();
        _captionOverlay!.SetStatus(_captions?.State ?? CaptionState.Stopped);
        if (_captionOverlay!.IsVisible) _captionOverlay.Hide(); else { _captionOverlay.Show(); _captionOverlay.ApplySettings(); }
        UpdateCaptionCaptureStatus();
    }
    private void CaptionOptions_Changed(object sender, RoutedEventArgs e)
    {
        if (!_captionUiReady || _cleanupDone || _isLoadingConfig) return;
        _captionSettings.SingleLine = CaptionSingleLineCheckBox.IsChecked == true;
        _captionSettings.ExcludeFromCapture = CaptionCaptureCheckBox.IsChecked == true;
        if (CaptionTranslationCheckBox != null)
            _captionSettings.EnableTranslation = CaptionTranslationCheckBox.IsChecked == true;
        if (CaptionModeVietnameseRadio != null && CaptionModeVietnameseRadio.IsChecked == true)
            _captionSettings.TranslationMode = "VietnameseOnly";
        else
            _captionSettings.TranslationMode = "Bilingual";
        _captionOverlay?.ApplySettings();
        _captionOverlay?.SetClickThrough(CaptionClickThroughCheckBox.IsChecked == true);
        SaveCaptionSettings(); UpdateCaptionCaptureStatus();
    }
    private void SaveCaptionSettings()
    {
        _captionSettingsSaveFailed = !_captionSettings.Save(_config.CaptionSettingsPath);
        if (!_cleanupDone) UpdateCaptionError();
    }
    private void UpdateCaptionError()
    {
        var detail = _captions?.Detail ?? "";
        if (detail.Length != 0) CaptionErrorText.Text = detail;
        else if (_captionSettingsSaveFailed) LocalizationService.BindKey(CaptionErrorText, TextBlock.TextProperty, "Captions.SaveError");
        else CaptionErrorText.Text = "";
        CaptionErrorText.Visibility = detail.Length != 0 || _captionSettingsSaveFailed ? Visibility.Visible : Visibility.Collapsed;
    }
    private void UpdateCaptionCaptureStatus()
    {
        var key = !_captionSettings.ExcludeFromCapture ? "Captions.CaptureOff" : _captionOverlay?.CaptureResult?.Mode switch
        {
            CaptureAffinityMode.Excluded => "Captions.CaptureVerified",
            CaptureAffinityMode.MonitorOnly => "Captions.CaptureLimited",
            _ => "Captions.CapturePending"
        };
        LocalizationService.BindKey(CaptionCaptureStatusText, TextBlock.TextProperty, key);
    }
    private void BtnCaptionReset_Click(object sender, RoutedEventArgs e)
    { if (!_cleanupDone) { EnsureCaptionOverlay(); _captionOverlay!.ResetPosition(); } }
    private void BtnCaptionCopy_Click(object sender, RoutedEventArgs e)
    {
        var text = CaptionPreviewText?.Text;
        if (string.IsNullOrWhiteSpace(text) || text == LocalizationService.GetString("Captions.Waiting", "Đang chờ phát hiện lời nói..."))
            text = _latestCaption;
        if (!string.IsNullOrWhiteSpace(text))
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
                UpdateStatus(LocalizationService.GetString("Captions.CopiedNotice", "Đã sao chép nội dung phụ đề vào bộ nhớ tạm"), true);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                UpdateStatus(LocalizationService.GetString("Captions.CopyFailedNotice", "Không thể sao chép phụ đề vào bộ nhớ tạm"), false);
            }
        }
    }
    private void CleanupCaptions()
    {
        CloseTranscriptReader();
        if (_captionTranslationHotkeyId >= 0)
        {
            _hotkeys.Unregister(_captionTranslationHotkeyId);
            _captionTranslationHotkeyId = -1;
        }
        _captionTranslation.Dispose();
        if (_captions != null)
        {
            _captions.Changed -= CaptionStateChanged;
            _captions.Segment -= CaptionSegmentReceived;
            _captions.SegmentTranslated -= CaptionSegmentTranslatedReceived;
            _captions.Level -= CaptionLevelReceived;
            _captions.Dispose();
        }
        _captionOverlay?.Close(); _captionOverlay = null;
    }
}
