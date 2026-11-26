using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using StreamGuard.Localization;
using StreamGuard.Services;

namespace StreamGuard;

public partial class MainWindow
{
    private readonly DispatcherTimer _transcriptTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private CaptionTranscriptStore? _transcriptStore;
    private CancellationTokenSource? _transcriptReadCancel;
    private int _transcriptRequest;
    private string? _transcriptContentKey;
    private bool _transcriptTruncated;
    private bool _transcriptLoading, _settingTranscriptSelection;
    private int _liveSrtIndex;
    private bool _userScrolledUp;
    private int _unreadSegmentsCount;
    private bool _transcriptExpanded;
    private readonly List<int> _searchMatches = new();
    private int _currentSearchIndex = -1;
    private readonly System.Text.StringBuilder _liveRawTxtBuffer = new();

    internal bool UserScrolledUp => _userScrolledUp;
    internal int UnreadSegmentsCount => _unreadSegmentsCount;
    internal bool TranscriptExpanded => _transcriptExpanded;
    internal IReadOnlyList<int> SearchMatches => _searchMatches;
    internal int CurrentSearchIndex => _currentSearchIndex;

    private void InitializeTranscriptReader()
    {
        _transcriptStore = new CaptionTranscriptStore(_captions!.OutputDirectory);
        _transcriptTimer.Tick += async (_, _) =>
        {
            if (_transcriptLoading || _cleanupDone || CaptionTranscriptPanel.Visibility != Visibility.Visible || ViewCaptions.Visibility != Visibility.Visible) return;
            if (TranscriptFollowCheckBox.IsChecked != true) return;
            if (_captions?.State is CaptionState.Starting or CaptionState.Running or CaptionState.Paused or CaptionState.Stopping)
            {
                var active = Path.GetFileNameWithoutExtension(_captions.LastTranscriptPath);
                if (active != null && (TranscriptSessionCombo.SelectedItem as CaptionTranscriptSession)?.Id != active)
                {
                    await LoadTranscriptAsync(refreshList: true, followCurrent: true);
                }
            }
        };
    }

    private async void BtnCaptionTranscripts_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanupDone) return;
        if (CaptionTranscriptPanel.Visibility == Visibility.Visible) { CloseTranscriptReader(); return; }
        CaptionTranscriptPanel.Visibility = Visibility.Visible;
        _transcriptTimer.Start();
        await LoadTranscriptAsync(refreshList: true, followCurrent: TranscriptFollowCheckBox.IsChecked == true);
        if (!_cleanupDone && CaptionTranscriptPanel.Visibility == Visibility.Visible) CaptionTranscriptPanel.BringIntoView();
    }

    private void CloseTranscriptReader()
    {
        _transcriptTimer.Stop(); ++_transcriptRequest;
        _transcriptReadCancel?.Cancel(); _transcriptReadCancel?.Dispose(); _transcriptReadCancel = null;
        _transcriptLoading = false;
        _userScrolledUp = false;
        _unreadSegmentsCount = 0;
        _liveRawTxtBuffer.Clear();
        if (BtnTranscriptScrollLatest != null) BtnTranscriptScrollLatest.Visibility = Visibility.Collapsed;
        CaptionTranscriptPanel.Visibility = Visibility.Collapsed;
    }

    private async void TranscriptSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_settingTranscriptSelection || _transcriptStore == null || CaptionTranscriptPanel.Visibility != Visibility.Visible) return;
        if (ReferenceEquals(sender, TranscriptSessionCombo))
        {
            var active = Path.GetFileNameWithoutExtension(_captions?.LastTranscriptPath);
            var isLive = (TranscriptSessionCombo.SelectedItem as CaptionTranscriptSession)?.Id == active &&
                _captions?.State is CaptionState.Starting or CaptionState.Running or CaptionState.Paused or CaptionState.Stopping;
            if (BtnTranscriptDeleteSession != null)
                BtnTranscriptDeleteSession.IsEnabled = !isLive && TranscriptSessionCombo.SelectedItem != null;

            if ((TranscriptSessionCombo.SelectedItem as CaptionTranscriptSession)?.Id != active)
            {
                _settingTranscriptSelection = true;
                TranscriptFollowCheckBox.IsChecked = false;
                _settingTranscriptSelection = false;
            }
        }
        await LoadTranscriptAsync();
    }

    private async void TranscriptFollow_Changed(object sender, RoutedEventArgs e)
    {
        if (_settingTranscriptSelection || _transcriptStore == null || CaptionTranscriptPanel.Visibility != Visibility.Visible) return;
        await LoadTranscriptAsync(refreshList: true, followCurrent: TranscriptFollowCheckBox.IsChecked == true);
    }
    private async void BtnTranscriptRefresh_Click(object sender, RoutedEventArgs e) => await LoadTranscriptAsync(refreshList: true);
    private void BtnTranscriptClose_Click(object sender, RoutedEventArgs e) => CloseTranscriptReader();

    private async void BtnTranscriptCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanupDone || CaptionTranscriptText.Text.Length == 0) return;
        var selectedText = CaptionTranscriptText.SelectedText;
        var hasSelection = !string.IsNullOrEmpty(selectedText);

        string textToCopy;
        string noticeMessage;

        if (hasSelection)
        {
            textToCopy = selectedText;
            noticeMessage = LocalizationService.GetString("Transcripts.CopiedSelectionNotice", "Đã sao chép đoạn văn bản được chọn vào bộ nhớ tạm.");
        }
        else
        {
            textToCopy = CaptionTranscriptText.Text;
            if (_transcriptTruncated && TranscriptSessionCombo.SelectedItem is CaptionTranscriptSession session && _transcriptStore != null)
            {
                var formatIndex = TranscriptFormatCombo.SelectedIndex;
                var subtitle = formatIndex == 1;
                var toeic = formatIndex == 2;
                var full = await Task.Run(() => _transcriptStore.ReadAllText(session.Id, subtitle));
                if (!string.IsNullOrEmpty(full))
                    textToCopy = toeic ? TranscriptToeicFormatter.Format(full) : full;
            }
            noticeMessage = LocalizationService.GetString("Transcripts.CopiedNotice", "Đã sao chép toàn bộ nội dung bản chép lời vào bộ nhớ tạm.");
        }

        try
        {
            System.Windows.Clipboard.SetText(textToCopy);
            UpdateStatus(noticeMessage, true);

            var originalContent = BtnTranscriptCopy.Content;
            var originalIcon = Controls.Icon.GetData(BtnTranscriptCopy);
            BtnTranscriptCopy.Content = LocalizationService.GetString("Transcripts.CopiedButton", "Đã sao chép!");
            Controls.Icon.SetData(BtnTranscriptCopy, (Geometry)FindResource("UiIconCheck"));

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (!_cleanupDone && BtnTranscriptCopy != null)
                {
                    LocalizationService.BindKey(BtnTranscriptCopy, ContentControl.ContentProperty, "Transcripts.Copy");
                    Controls.Icon.SetData(BtnTranscriptCopy, (Geometry)FindResource("UiIconCopy"));
                }
            };
            timer.Start();
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            UpdateStatus("Clipboard is busy, please try again.", false);
        }
    }

    private async void BtnTranscriptClear_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanupDone || CaptionTranscriptText.Text.Length == 0 || _transcriptStore == null) return;
        if (TranscriptSessionCombo.SelectedItem is not CaptionTranscriptSession session) return;

        var active = Path.GetFileNameWithoutExtension(_captions?.LastTranscriptPath);
        var isLive = session.Id == active && _captions?.State is CaptionState.Starting or CaptionState.Running or CaptionState.Paused or CaptionState.Stopping;

        _userScrolledUp = false;
        _unreadSegmentsCount = 0;
        if (BtnTranscriptScrollLatest != null) BtnTranscriptScrollLatest.Visibility = Visibility.Collapsed;

        if (isLive)
        {
            CaptionTranscriptText.Text = "";
            _liveRawTxtBuffer.Clear();
            _liveSrtIndex = 0;
            TranscriptFollowCheckBox.IsChecked = false;
            BtnTranscriptCopy.IsEnabled = false;
            if (BtnTranscriptClear != null) BtnTranscriptClear.IsEnabled = false;
            UpdateStatus(LocalizationService.GetString("Transcripts.ClearedLiveNotice", "Phiên đang ghi trực tiếp: Đã xoá nội dung hiển thị trong khu vực xem."), true);
        }
        else
        {
            _transcriptStore.Delete(session.Id);
            CaptionTranscriptText.Text = "";
            _liveRawTxtBuffer.Clear();
            _liveSrtIndex = 0;
            _transcriptContentKey = null;
            BtnTranscriptCopy.IsEnabled = false;
            if (BtnTranscriptClear != null) BtnTranscriptClear.IsEnabled = false;
            UpdateStatus(LocalizationService.GetString("Transcripts.ClearedNotice", "Đã xoá sạch toàn bộ nội dung bản chép lời trong khu vực."), true);
            await LoadTranscriptAsync(refreshList: true);
        }

        PerformTranscriptSearch(keepPosition: false);
    }

    private async void BtnTranscriptDeleteSession_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanupDone || _transcriptStore == null) return;
        if (TranscriptSessionCombo.SelectedItem is not CaptionTranscriptSession session) return;

        var active = Path.GetFileNameWithoutExtension(_captions?.LastTranscriptPath);
        var isLive = session.Id == active && _captions?.State is CaptionState.Starting or CaptionState.Running or CaptionState.Paused or CaptionState.Stopping;
        if (isLive)
        {
            UpdateStatus(LocalizationService.GetString("Transcripts.CannotDeleteLiveNotice", "Không thể xoá phiên đang ghi âm trực tiếp."), false);
            return;
        }

        var label = session.Label;
        _transcriptStore.Delete(session.Id);
        CaptionTranscriptText.Text = "";
        _liveRawTxtBuffer.Clear();
        _transcriptContentKey = null;
        UpdateStatus(string.Format(LocalizationService.GetString("Transcripts.DeletedSessionNotice", "Đã xoá phiên chép lời '{0}' (chuyển vào Thùng rác)."), label), true);
        await LoadTranscriptAsync(refreshList: true);
    }

    private async void BtnTranscriptDeletePrevious_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanupDone || _transcriptStore == null) return;
        var active = Path.GetFileNameWithoutExtension(_captions?.LastTranscriptPath);
        var deletedCount = _transcriptStore.DeletePreviousSessions(active);
        if (deletedCount > 0)
        {
            CaptionTranscriptText.Text = "";
            _liveRawTxtBuffer.Clear();
            _transcriptContentKey = null;
            UpdateStatus(string.Format(LocalizationService.GetString("Transcripts.DeletedPreviousNotice", "Đã xoá {0} phiên chép lời trước đó (chuyển vào Thùng rác)."), deletedCount), true);
            await LoadTranscriptAsync(refreshList: true);
        }
        else
        {
            UpdateStatus("Không có phiên chép lời trước đó nào để xoá.", false);
        }
    }

    private async Task LoadTranscriptAsync(bool refreshList = false, bool followCurrent = false)
    {
        if (_cleanupDone || _transcriptStore == null || CaptionTranscriptPanel.Visibility != Visibility.Visible) return;
        var request = ++_transcriptRequest;
        _transcriptReadCancel?.Cancel(); _transcriptReadCancel?.Dispose();
        var cancel = _transcriptReadCancel = new CancellationTokenSource();
        var token = cancel.Token;
        _transcriptLoading = true;
        BtnTranscriptRefresh.IsEnabled = false;
        BtnTranscriptCopy.IsEnabled = false;
        if (BtnTranscriptClear != null) BtnTranscriptClear.IsEnabled = false;
        if (BtnTranscriptDeleteSession != null) BtnTranscriptDeleteSession.IsEnabled = false;
        if (BtnTranscriptDeletePrevious != null) BtnTranscriptDeletePrevious.IsEnabled = false;
        bool Current() => request == _transcriptRequest && !_cleanupDone && CaptionTranscriptPanel.Visibility == Visibility.Visible;
        try
        {
            if (refreshList)
            {
                var selected = (TranscriptSessionCombo.SelectedItem as CaptionTranscriptSession)?.Id;
                var sessions = await Task.Run(() => _transcriptStore.List(token), token);
                if (!Current()) return;
                var active = Path.GetFileNameWithoutExtension(_captions?.LastTranscriptPath);
                var desired = followCurrent && active != null ? active : selected;
                _settingTranscriptSelection = true;
                try
                {
                    TranscriptSessionCombo.ItemsSource = sessions;
                    TranscriptSessionCombo.SelectedItem = sessions.FirstOrDefault(item => item.Id == desired) ?? sessions.FirstOrDefault();
                    if (BtnTranscriptDeletePrevious != null)
                        BtnTranscriptDeletePrevious.IsEnabled = sessions.Count > 1;
                }
                finally { _settingTranscriptSelection = false; }
            }
            if (TranscriptSessionCombo.SelectedItem is not CaptionTranscriptSession session)
            {
                CaptionTranscriptText.Text = ""; _transcriptContentKey = null; BtnTranscriptCopy.IsEnabled = false;
                if (BtnTranscriptClear != null) BtnTranscriptClear.IsEnabled = false;
                if (BtnTranscriptDeleteSession != null) BtnTranscriptDeleteSession.IsEnabled = false;
                if (BtnTranscriptDeletePrevious != null) BtnTranscriptDeletePrevious.IsEnabled = false;
                LocalizationService.BindKey(TranscriptReadStatus, TextBlock.TextProperty, "Transcripts.Empty"); return;
            }
            var activeSession = Path.GetFileNameWithoutExtension(_captions?.LastTranscriptPath);
            var isLiveSession = session.Id == activeSession && _captions?.State is CaptionState.Starting or CaptionState.Running or CaptionState.Paused or CaptionState.Stopping;
            if (BtnTranscriptDeleteSession != null)
                BtnTranscriptDeleteSession.IsEnabled = !isLiveSession;

            var formatIndex = TranscriptFormatCombo.SelectedIndex;
            var subtitle = formatIndex == 1;
            var toeic = formatIndex == 2;
            var contentKey = session.Id + (subtitle ? "|SRT" : toeic ? "|TOEIC" : "|TXT");
            var sameContentKey = _transcriptContentKey == contentKey;
            if (!sameContentKey)
            {
                CaptionTranscriptText.Text = "";
                _liveRawTxtBuffer.Clear();
                LocalizationService.BindKey(TranscriptReadStatus, TextBlock.TextProperty, "Transcripts.Loading");
            }
            var content = await Task.Run(() => _transcriptStore.Read(session.Id, subtitle, token), token);
            if (!Current()) return;
            _transcriptContentKey = contentKey;
            var displayText = toeic ? TranscriptToeicFormatter.Format(content.Text) : content.Text;
            if (CaptionTranscriptText.Text != displayText)
            {
                var liveAhead = isLiveSession && TranscriptFollowCheckBox.IsChecked == true && sameContentKey &&
                    (toeic
                        ? _liveRawTxtBuffer.Length > content.Text.Length &&
                          _liveRawTxtBuffer.ToString().TrimEnd().StartsWith(content.Text.TrimEnd(), StringComparison.Ordinal)
                        : CaptionTranscriptText.Text.Length > displayText.Length &&
                          CaptionTranscriptText.Text.StartsWith(displayText, StringComparison.Ordinal));
                if (liveAhead)
                {
                    // In-memory live streaming is already ahead of the disk snapshot; retain live text.
                }
                else
                {
                    var offset = CaptionTranscriptText.VerticalOffset;
                    var selection = CaptionTranscriptText.SelectionStart;
                    var selectedLength = CaptionTranscriptText.SelectionLength;
                    var appended = displayText.StartsWith(CaptionTranscriptText.Text, StringComparison.Ordinal);
                    CaptionTranscriptText.Text = displayText;
                    if (!subtitle)
                    {
                        _liveRawTxtBuffer.Clear();
                        _liveRawTxtBuffer.Append(content.Text);
                        if (content.Text.Length > 0 && !content.Text.EndsWith('\n'))
                            _liveRawTxtBuffer.AppendLine();
                    }
                    if (isLiveSession && subtitle)
                    {
                        _liveSrtIndex = System.Text.RegularExpressions.Regex.Matches(displayText, @"(?m)^\d+(?=\r?\n\d{2}:\d{2}:\d{2},\d{3}\s*-->)").Count;
                    }
                    else if (isLiveSession)
                    {
                        _liveSrtIndex = 0;
                    }
                    if (TranscriptFollowCheckBox.IsChecked == true && !_userScrolledUp && selectedLength == 0)
                    {
                        CaptionTranscriptText.ScrollToEnd();
                    }
                    else
                    {
                        if (appended && selectedLength > 0) CaptionTranscriptText.Select(selection, selectedLength);
                        CaptionTranscriptText.ScrollToVerticalOffset(offset);
                    }
                }
            }
            BtnTranscriptCopy.IsEnabled = CaptionTranscriptText.Text.Length != 0 || displayText.Length != 0;
            if (BtnTranscriptClear != null) BtnTranscriptClear.IsEnabled = CaptionTranscriptText.Text.Length != 0 || displayText.Length != 0;
            _transcriptTruncated = content.Truncated; UpdateTranscriptReadStatus();

            if (!string.IsNullOrEmpty(TranscriptSearchBox?.Text))
            {
                PerformTranscriptSearch(keepPosition: true);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            if (!Current()) return;
            CaptionTranscriptText.Text = ""; _transcriptContentKey = null; BtnTranscriptCopy.IsEnabled = false;
            if (BtnTranscriptClear != null) BtnTranscriptClear.IsEnabled = false;
            if (BtnTranscriptDeleteSession != null) BtnTranscriptDeleteSession.IsEnabled = false;
            LocalizationService.BindKey(TranscriptReadStatus, TextBlock.TextProperty, "Transcripts.ReadError");
        }
        finally
        {
            if (Current())
            {
                _transcriptLoading = false;
                BtnTranscriptRefresh.IsEnabled = true;
                if (BtnTranscriptDeletePrevious != null && TranscriptSessionCombo.ItemsSource is IReadOnlyList<CaptionTranscriptSession> sl)
                    BtnTranscriptDeletePrevious.IsEnabled = sl.Count > 1;
            }
        }
    }

    private void UpdateTranscriptReadStatus()
    {
        if (_transcriptContentKey == null || TranscriptSessionCombo.SelectedItem is not CaptionTranscriptSession session) return;
        var live = session.Id == Path.GetFileNameWithoutExtension(_captions?.LastTranscriptPath) &&
            _captions?.State is CaptionState.Running or CaptionState.Paused or CaptionState.Stopping;
        LocalizationService.BindKey(TranscriptReadStatus, TextBlock.TextProperty,
            _transcriptTruncated ? "Transcripts.Truncated" : !live ? "Transcripts.Saved" :
            TranscriptFollowCheckBox.IsChecked == true ? "Transcripts.Live" : "Transcripts.Manual");
    }

    internal void AppendLiveSegment(string text, string? translation, double start, double end)
    {
        if (_cleanupDone || CaptionTranscriptPanel.Visibility != Visibility.Visible) return;
        if (TranscriptFollowCheckBox.IsChecked != true) return;

        var active = Path.GetFileNameWithoutExtension(_captions?.LastTranscriptPath);
        if (active != null && (TranscriptSessionCombo.SelectedItem as CaptionTranscriptSession)?.Id != active)
        {
            _ = LoadTranscriptAsync(refreshList: true, followCurrent: true);
            return;
        }

        var cleanText = text.Replace("\r", " ").Replace("\n", " ").Trim();
        if (cleanText.Length == 0) return;

        var cleanTrans = !string.IsNullOrWhiteSpace(translation) && !translation.Equals(text, StringComparison.OrdinalIgnoreCase)
            ? translation.Replace("\r", " ").Replace("\n", " ").Trim()
            : null;

        var formatIndex = TranscriptFormatCombo.SelectedIndex;
        var subtitle = formatIndex == 1;
        var toeic = formatIndex == 2;
        var sb = new System.Text.StringBuilder();

        if (subtitle)
        {
            _liveSrtIndex++;
            var startTs = CaptionTranscriptWriter.Timestamp(start);
            var endTs = CaptionTranscriptWriter.Timestamp(Math.Max(start + 0.5, end));
            sb.AppendLine(_liveSrtIndex.ToString());
            sb.AppendLine($"{startTs} --> {endTs}");
            sb.AppendLine(cleanText);
            if (!string.IsNullOrWhiteSpace(cleanTrans))
                sb.AppendLine(cleanTrans);
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine(cleanText);
            if (!string.IsNullOrWhiteSpace(cleanTrans))
                sb.AppendLine("  -> " + cleanTrans);
            sb.AppendLine();

            if (_liveRawTxtBuffer.Length == 0 && CaptionTranscriptText.Text.Length > 0 && !toeic)
            {
                _liveRawTxtBuffer.Append(CaptionTranscriptText.Text);
                if (!CaptionTranscriptText.Text.EndsWith('\n'))
                    _liveRawTxtBuffer.AppendLine();
            }
            _liveRawTxtBuffer.Append(sb);
            const int maxLiveRawChars = 128 * 1024;
            if (_liveRawTxtBuffer.Length > maxLiveRawChars)
            {
                var excess = _liveRawTxtBuffer.Length - maxLiveRawChars;
                var rawSnapshot = _liveRawTxtBuffer.ToString();
                var splitIdx = rawSnapshot.IndexOf("\n\n", excess, StringComparison.Ordinal);
                if (splitIdx >= 0 && splitIdx + 2 < rawSnapshot.Length)
                {
                    _liveRawTxtBuffer.Remove(0, splitIdx + 2);
                }
                else
                {
                    _liveRawTxtBuffer.Remove(0, excess);
                }
                _transcriptTruncated = true;
            }
        }

        var pauseAutoScroll = _userScrolledUp || CaptionTranscriptText.SelectionLength > 0;
        if (toeic)
        {
            var formatted = TranscriptToeicFormatter.Format(_liveRawTxtBuffer.ToString());
            if (pauseAutoScroll)
            {
                _userScrolledUp = true;
                var selStart = CaptionTranscriptText.SelectionStart;
                var selLen = CaptionTranscriptText.SelectionLength;
                var vOffset = CaptionTranscriptText.VerticalOffset;
                if (CaptionTranscriptText.Text != formatted)
                    CaptionTranscriptText.Text = formatted;
                if (selLen > 0 && selStart < formatted.Length)
                {
                    CaptionTranscriptText.Select(selStart, Math.Min(selLen, formatted.Length - selStart));
                }
                CaptionTranscriptText.ScrollToVerticalOffset(vOffset);
                _unreadSegmentsCount++;
                UpdateScrollLatestPill();
            }
            else
            {
                if (CaptionTranscriptText.Text != formatted)
                    CaptionTranscriptText.Text = formatted;
                CaptionTranscriptText.ScrollToEnd();
            }
        }
        else if (pauseAutoScroll)
        {
            _userScrolledUp = true;
            var selStart = CaptionTranscriptText.SelectionStart;
            var selLen = CaptionTranscriptText.SelectionLength;
            var vOffset = CaptionTranscriptText.VerticalOffset;
            CaptionTranscriptText.AppendText(sb.ToString());
            if (selLen > 0)
            {
                CaptionTranscriptText.Select(selStart, selLen);
            }
            CaptionTranscriptText.ScrollToVerticalOffset(vOffset);
            _unreadSegmentsCount++;
            UpdateScrollLatestPill();
        }
        else
        {
            CaptionTranscriptText.AppendText(sb.ToString());
            CaptionTranscriptText.ScrollToEnd();
        }

        BtnTranscriptCopy.IsEnabled = true;
        if (BtnTranscriptClear != null) BtnTranscriptClear.IsEnabled = true;
        UpdateTranscriptReadStatus();

        if (!string.IsNullOrEmpty(TranscriptSearchBox?.Text))
        {
            PerformTranscriptSearch(keepPosition: true);
        }
    }

    private void CaptionTranscriptText_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_cleanupDone || CaptionTranscriptText == null) return;

        var distanceFromBottom = Math.Max(0, e.ExtentHeight - e.ViewportHeight - e.VerticalOffset);

        if (distanceFromBottom <= 30)
        {
            if (CaptionTranscriptText.SelectionLength == 0)
            {
                _userScrolledUp = false;
                _unreadSegmentsCount = 0;
                if (BtnTranscriptScrollLatest != null)
                    BtnTranscriptScrollLatest.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            _userScrolledUp = true;
            UpdateScrollLatestPill();
        }
    }

    private void UpdateScrollLatestPill()
    {
        if (BtnTranscriptScrollLatest == null || TxtTranscriptScrollLatest == null) return;
        if (_userScrolledUp)
        {
            if (_unreadSegmentsCount > 0)
            {
                var fmt = LocalizationService.GetString("Transcripts.ScrollToLatest", "⬇ Cuộn xuống mới nhất (+{0})");
                TxtTranscriptScrollLatest.Text = string.Format(fmt, _unreadSegmentsCount);
            }
            else
            {
                TxtTranscriptScrollLatest.Text = LocalizationService.GetString("Transcripts.ScrollToLatestPlain", "⬇ Cuộn xuống mới nhất");
            }
            BtnTranscriptScrollLatest.Visibility = Visibility.Visible;
        }
        else
        {
            BtnTranscriptScrollLatest.Visibility = Visibility.Collapsed;
        }
    }

    private void BtnTranscriptScrollLatest_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanupDone) return;
        _userScrolledUp = false;
        _unreadSegmentsCount = 0;
        if (BtnTranscriptScrollLatest != null)
            BtnTranscriptScrollLatest.Visibility = Visibility.Collapsed;
        if (CaptionTranscriptText != null)
        {
            if (CaptionTranscriptText.SelectionLength > 0)
            {
                CaptionTranscriptText.Select(CaptionTranscriptText.Text.Length, 0);
            }
            CaptionTranscriptText.ScrollToEnd();
        }
    }

    private void BtnTranscriptScrollTop_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanupDone) return;
        _userScrolledUp = true;
        CaptionTranscriptText.ScrollToHome();
        UpdateScrollLatestPill();
    }

    private void BtnTranscriptScrollBottom_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanupDone) return;
        _userScrolledUp = false;
        _unreadSegmentsCount = 0;
        if (BtnTranscriptScrollLatest != null)
            BtnTranscriptScrollLatest.Visibility = Visibility.Collapsed;
        if (CaptionTranscriptText != null)
        {
            if (CaptionTranscriptText.SelectionLength > 0)
            {
                CaptionTranscriptText.Select(CaptionTranscriptText.Text.Length, 0);
            }
            CaptionTranscriptText.ScrollToEnd();
        }
    }

    private void BtnTranscriptExpand_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanupDone) return;
        _transcriptExpanded = !_transcriptExpanded;
        CaptionTranscriptText.Height = _transcriptExpanded ? 440 : 240;
        UpdateExpandButtonUi();
        if (!_userScrolledUp && TranscriptFollowCheckBox.IsChecked == true)
        {
            CaptionTranscriptText.ScrollToEnd();
        }
    }

    internal void UpdateExpandButtonUi()
    {
        if (BtnTranscriptExpand == null) return;
        if (_transcriptExpanded)
        {
            LocalizationService.BindKey(BtnTranscriptExpand, ContentControl.ContentProperty, "Transcripts.Collapse");
            LocalizationService.BindKey(BtnTranscriptExpand, FrameworkElement.ToolTipProperty, "Transcripts.CollapseTooltip");
            Controls.Icon.SetData(BtnTranscriptExpand, (Geometry)FindResource("ActionIconCollapse"));
        }
        else
        {
            LocalizationService.BindKey(BtnTranscriptExpand, ContentControl.ContentProperty, "Transcripts.Expand");
            LocalizationService.BindKey(BtnTranscriptExpand, FrameworkElement.ToolTipProperty, "Transcripts.ExpandTooltip");
            Controls.Icon.SetData(BtnTranscriptExpand, (Geometry)FindResource("ActionIconExpand"));
        }
    }

    private void TranscriptSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = TranscriptSearchBox?.Text ?? "";
        var hasQuery = !string.IsNullOrEmpty(query);
        if (TranscriptSearchPlaceholder != null)
            TranscriptSearchPlaceholder.Visibility = hasQuery ? Visibility.Collapsed : Visibility.Visible;
        if (BtnTranscriptClearSearch != null)
            BtnTranscriptClearSearch.Visibility = hasQuery ? Visibility.Visible : Visibility.Collapsed;
        PerformTranscriptSearch(keepPosition: false);
    }

    private void TranscriptSearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            e.Handled = true;
            if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0)
            {
                FindPreviousMatch();
            }
            else
            {
                FindNextMatch();
            }
        }
        else if (e.Key == System.Windows.Input.Key.Escape)
        {
            e.Handled = true;
            TranscriptSearchBox.Text = "";
        }
    }

    private void BtnTranscriptClearSearch_Click(object sender, RoutedEventArgs e)
    {
        TranscriptSearchBox.Text = "";
        TranscriptSearchBox.Focus();
    }

    private void BtnTranscriptFindPrev_Click(object sender, RoutedEventArgs e) => FindPreviousMatch();
    private void BtnTranscriptFindNext_Click(object sender, RoutedEventArgs e) => FindNextMatch();

    internal void PerformTranscriptSearch(bool keepPosition = true)
    {
        var query = TranscriptSearchBox?.Text;
        if (string.IsNullOrEmpty(query) || CaptionTranscriptText == null)
        {
            _searchMatches.Clear();
            _currentSearchIndex = -1;
            if (TranscriptSearchCount != null) TranscriptSearchCount.Text = "";
            if (CaptionTranscriptText != null && CaptionTranscriptText.SelectionLength > 0)
            {
                CaptionTranscriptText.Select(CaptionTranscriptText.SelectionStart, 0);
            }
            return;
        }

        var text = CaptionTranscriptText.Text;
        var matches = FindMatchIndices(text, query);
        _searchMatches.Clear();
        _searchMatches.AddRange(matches);

        if (_searchMatches.Count == 0)
        {
            _currentSearchIndex = -1;
            if (TranscriptSearchCount != null)
                TranscriptSearchCount.Text = LocalizationService.GetString("Transcripts.NoMatches", "0 kết quả");
            return;
        }

        if (!keepPosition || _currentSearchIndex < 0 || _currentSearchIndex >= _searchMatches.Count)
        {
            var currentSel = CaptionTranscriptText.SelectionStart;
            var foundIdx = _searchMatches.FindIndex(m => m >= currentSel);
            _currentSearchIndex = foundIdx >= 0 ? foundIdx : 0;
        }

        UpdateSearchCountAndHighlight();
    }

    internal static List<int> FindMatchIndices(string text, string query)
    {
        var list = new List<int>();
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query)) return list;

        var index = 0;
        while (index <= text.Length - query.Length)
        {
            var found = text.IndexOf(query, index, StringComparison.CurrentCultureIgnoreCase);
            if (found < 0) break;
            list.Add(found);
            index = found + Math.Max(1, query.Length);
        }
        return list;
    }

    private void FindNextMatch()
    {
        if (_searchMatches.Count == 0) return;
        _currentSearchIndex = (_currentSearchIndex + 1) % _searchMatches.Count;
        UpdateSearchCountAndHighlight();
    }

    private void FindPreviousMatch()
    {
        if (_searchMatches.Count == 0) return;
        _currentSearchIndex = (_currentSearchIndex - 1 + _searchMatches.Count) % _searchMatches.Count;
        UpdateSearchCountAndHighlight();
    }

    private void UpdateSearchCountAndHighlight()
    {
        if (_searchMatches.Count == 0 || _currentSearchIndex < 0 || _currentSearchIndex >= _searchMatches.Count)
        {
            if (TranscriptSearchCount != null)
                TranscriptSearchCount.Text = LocalizationService.GetString("Transcripts.NoMatches", "0 kết quả");
            return;
        }

        if (TranscriptSearchCount != null)
        {
            var fmt = LocalizationService.GetString("Transcripts.SearchCount", "{0} / {1}");
            TranscriptSearchCount.Text = string.Format(fmt, _currentSearchIndex + 1, _searchMatches.Count);
        }

        HighlightCurrentSearchMatch();
    }

    private void HighlightCurrentSearchMatch()
    {
        if (_searchMatches.Count == 0 || _currentSearchIndex < 0 || _currentSearchIndex >= _searchMatches.Count) return;
        var query = TranscriptSearchBox?.Text;
        if (string.IsNullOrEmpty(query)) return;

        var pos = _searchMatches[_currentSearchIndex];
        if (pos + query.Length <= CaptionTranscriptText.Text.Length)
        {
            CaptionTranscriptText.Select(pos, query.Length);
            var lineIndex = CaptionTranscriptText.GetLineIndexFromCharacterIndex(pos);
            if (lineIndex >= 0)
            {
                CaptionTranscriptText.ScrollToLine(lineIndex);
            }
            var distanceFromBottom = Math.Max(0, CaptionTranscriptText.ExtentHeight - CaptionTranscriptText.ViewportHeight - CaptionTranscriptText.VerticalOffset);
            if (distanceFromBottom > 30)
            {
                _userScrolledUp = true;
                UpdateScrollLatestPill();
            }
        }
    }

    private void CaptionTranscriptText_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var hasText = CaptionTranscriptText.Text.Length > 0;
        var hasSelection = CaptionTranscriptText.SelectionLength > 0;
        if (MenuTranscriptCopySelected != null) MenuTranscriptCopySelected.IsEnabled = hasSelection;
        if (MenuTranscriptCopyRecent != null) MenuTranscriptCopyRecent.IsEnabled = hasText;
        if (MenuTranscriptCopyAll != null) MenuTranscriptCopyAll.IsEnabled = hasText;
        if (MenuTranscriptSelectAll != null) MenuTranscriptSelectAll.IsEnabled = hasText;
    }

    private void MenuTranscriptCopySelected_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanupDone || string.IsNullOrEmpty(CaptionTranscriptText.SelectedText)) return;
        try
        {
            System.Windows.Clipboard.SetText(CaptionTranscriptText.SelectedText);
            UpdateStatus(LocalizationService.GetString("Transcripts.CopiedSelectionNotice", "Đã sao chép đoạn văn bản được chọn vào bộ nhớ tạm."), true);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            UpdateStatus("Clipboard is busy, please try again.", false);
        }
    }

    private void MenuTranscriptCopyRecent_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanupDone || CaptionTranscriptText.Text.Length == 0) return;
        var formatIndex = TranscriptFormatCombo.SelectedIndex;
        var isSrt = formatIndex == 1;
        var isToeic = formatIndex == 2;
        var recentText = ExtractRecentSegments(CaptionTranscriptText.Text, 10, isSrt, isToeic);
        if (string.IsNullOrEmpty(recentText)) return;
        try
        {
            System.Windows.Clipboard.SetText(recentText);
            UpdateStatus(LocalizationService.GetString("Transcripts.CopiedRecentNotice", "Đã sao chép 10 câu mới nhất vào bộ nhớ tạm."), true);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            UpdateStatus("Clipboard is busy, please try again.", false);
        }
    }

    private async void MenuTranscriptCopyAll_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanupDone || CaptionTranscriptText.Text.Length == 0) return;
        var textToCopy = CaptionTranscriptText.Text;
        if (_transcriptTruncated && TranscriptSessionCombo.SelectedItem is CaptionTranscriptSession session && _transcriptStore != null)
        {
            var formatIndex = TranscriptFormatCombo.SelectedIndex;
            var subtitle = formatIndex == 1;
            var toeic = formatIndex == 2;
            var full = await Task.Run(() => _transcriptStore.ReadAllText(session.Id, subtitle));
            if (!string.IsNullOrEmpty(full))
                textToCopy = toeic ? TranscriptToeicFormatter.Format(full) : full;
        }
        try
        {
            System.Windows.Clipboard.SetText(textToCopy);
            UpdateStatus(LocalizationService.GetString("Transcripts.CopiedNotice", "Đã sao chép toàn bộ nội dung bản chép lời vào bộ nhớ tạm."), true);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            UpdateStatus("Clipboard is busy, please try again.", false);
        }
    }

    private void MenuTranscriptSelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanupDone) return;
        CaptionTranscriptText.Focus();
        CaptionTranscriptText.SelectAll();
    }

    internal static string ExtractRecentSegments(string text, int count = 10, bool isSrt = false, bool isToeic = false)
    {
        if (string.IsNullOrWhiteSpace(text) || count <= 0) return "";

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (string.IsNullOrEmpty(normalized)) return "";

        List<string> blocks;
        if (isToeic && normalized.Contains(TranscriptToeicFormatter.BlockDivider, StringComparison.Ordinal))
        {
            blocks = new List<string>();
            var parts = normalized.Split(new[] { TranscriptToeicFormatter.BlockDivider }, StringSplitOptions.None);
            if (!string.IsNullOrWhiteSpace(parts[0]))
            {
                blocks.AddRange(parts[0].Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(b => b.Trim())
                                        .Where(b => b.Length > 0));
            }
            for (var i = 1; i < parts.Length; i++)
            {
                var body = parts[i].Trim('\r', '\n');
                if (string.IsNullOrWhiteSpace(body)) continue;
                var subChunks = body.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
                                    .Select(c => c.Trim('\r', '\n'))
                                    .Where(c => !string.IsNullOrWhiteSpace(c))
                                    .ToArray();
                if (subChunks.Length == 0) continue;

                var consumed = 1;
                var toeicBlock = TranscriptToeicFormatter.BlockDivider + "\n" + subChunks[0];
                if (!subChunks[0].StartsWith("  (", StringComparison.Ordinal) &&
                    subChunks.Length > 1 &&
                    subChunks[1].StartsWith("  (", StringComparison.Ordinal))
                {
                    toeicBlock += "\n\n" + subChunks[1];
                    consumed = 2;
                }
                blocks.Add(toeicBlock);

                for (var k = consumed; k < subChunks.Length; k++)
                {
                    blocks.Add(subChunks[k].Trim());
                }
            }
        }
        else if (isSrt)
        {
            blocks = normalized.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(b => b.Trim())
                               .Where(b => b.Length > 0)
                               .ToList();
        }
        else
        {
            var doubleNewlineBlocks = normalized.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
                                                .Select(b => b.Trim())
                                                .Where(b => b.Length > 0)
                                                .ToList();
            if (doubleNewlineBlocks.Count > 1 || (doubleNewlineBlocks.Count == 1 && (doubleNewlineBlocks[0].Contains("->") || !doubleNewlineBlocks[0].Contains('\n'))))
            {
                blocks = doubleNewlineBlocks;
            }
            else
            {
                blocks = normalized.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                   .Select(b => b.Trim())
                                   .Where(b => b.Length > 0)
                                   .ToList();
            }
        }

        if (blocks.Count == 0) return "";

        var recent = blocks.TakeLast(count).Select(b => b.Replace("\n", "\r\n"));
        return string.Join("\r\n\r\n", recent);
    }

    internal void RefreshTranscriptLanguage()
    {
        UpdateExpandButtonUi();
        UpdateScrollLatestPill();
        UpdateTranscriptReadStatus();
        if (BtnTranscriptCopy != null)
        {
            var isShowingCopied = BtnTranscriptCopy.Content is string s && s == LocalizationService.GetString("Transcripts.CopiedButton", "Đã sao chép!");
            if (!isShowingCopied)
            {
                LocalizationService.BindKey(BtnTranscriptCopy, ContentControl.ContentProperty, "Transcripts.Copy");
            }
        }
        if (_searchMatches.Count == 0 && !string.IsNullOrEmpty(TranscriptSearchBox?.Text))
        {
            if (TranscriptSearchCount != null)
                TranscriptSearchCount.Text = LocalizationService.GetString("Transcripts.NoMatches", "0 kết quả");
        }
        else if (_searchMatches.Count > 0 && TranscriptSearchCount != null)
        {
            var fmt = LocalizationService.GetString("Transcripts.SearchCount", "{0} / {1}");
            TranscriptSearchCount.Text = string.Format(fmt, _currentSearchIndex + 1, _searchMatches.Count);
        }
    }
}
