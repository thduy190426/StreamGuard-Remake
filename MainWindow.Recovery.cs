using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using StreamGuard.Localization;
using StreamGuard.Models;
using StreamGuard.Services;

namespace StreamGuard;

public partial class MainWindow
{
    private readonly RecoveryService _recovery;
    [DllImport("user32.dll", EntryPoint = "GetParent")] private static extern IntPtr GetRecoveryParent(IntPtr window);

    private void InitializeRecoveryCenter()
    {
        RecoveryListBox.ItemsSource = _recovery.Records;
        _recovery.Changed += UpdateRecoveryCenter;
        UpdateRecoveryCenter();
        if (_recovery.Records.Any(record => record.CanRetry && record.IsPreviousSession)) TabRecovery.IsChecked = true;
    }
    private void UpdateRecoveryCenter()
    {
        if (RecoverySummaryText == null || _cleanupDone) return;
        LocalizationService.BindText(RecoverySummaryText, TextBlock.TextProperty,
            $"Recovery center: {_recovery.PendingCount} pending, {_recovery.Records.Count(record => !record.CanRetry)} finished.");
        RecoveryEmptyState.Visibility = _recovery.Records.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RecoveryJournalWarning.Visibility = string.IsNullOrEmpty(_recovery.Warning) ? Visibility.Collapsed : Visibility.Visible;
        LocalizationService.BindText(RecoveryJournalWarning, TextBlock.TextProperty, _recovery.Warning ?? "");
        BtnRecoverAll.IsEnabled = _recovery.PendingCount > 0;
        if (BtnClearFinishedRecovery != null) BtnClearFinishedRecovery.IsEnabled = _recovery.Records.Any(record => !record.CanRetry);
    }
    private void BtnRecoveryRefresh_Click(object sender, RoutedEventArgs e) { if (!_cleanupDone) _recovery.Refresh(); }
    private void BtnDeleteRecoveryRecord_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanupDone || sender is not FrameworkElement { Tag: RecoveryRecord record }) return;
        if (!record.CanDelete)
        {
            UpdateStatus(LocalizationService.GetString("Recovery.CannotDeleteProtected", "Cannot delete a currently protected window session. Stop protection first."), false);
            return;
        }
        if (_recovery.Delete(record.Id))
        {
            var successTpl = LocalizationService.GetString("Recovery.DeleteSuccess", "Removed recovery record for {0}.");
            UpdateStatus(string.Format(successTpl, record.ApplicationName), true);
        }
    }
    private void BtnClearFinishedRecovery_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanupDone) return;
        var count = _recovery.ClearFinished();
        UpdateStatus(count > 0 ? $"Cleared {count} finished recovery session(s)." : "No finished recovery sessions to clear.", true);
    }
    private void BtnRecoveryRetry_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanupDone || sender is not FrameworkElement { Tag: RecoveryRecord record }) return;
        PauseCurtainsForRecovery();
        RestoreRecoveryRecord(record.Id);
        SynchronizeRecoveryProtectionState();
    }
    private void BtnRecoverAll_Click(object sender, RoutedEventArgs e)
    {
        if (_cleanupDone) return;
        PauseCurtainsForRecovery();
        foreach (var record in _recovery.Records.Where(record => record.CanRetry).ToList()) RestoreRecoveryRecord(record.Id);
        SynchronizeRecoveryProtectionState();
    }
    private void PauseCurtainsForRecovery()
    {
        _protectionRevision++;
        _captureGuardPaused = true;
        _privacyCurtain.Hide(); _capturePrivacyCurtain.Hide();
    }
    private RecoveryOutcome RestoreRecoveryRecord(Guid id)
    {
        var outcome = _recovery.Restore(id);
        if (!outcome.WindowReleased) return outcome;
        foreach (var app in _apps.ToList())
        {
            var states = app.ProtectedWindows.Where(state => state.RecoveryId == id).ToList();
            if (states.Count == 0) continue;
            foreach (var state in states) { CloseRecoveredHost(state); app.ProtectedWindows.Remove(state); }
            UpdateRecoveredApp(app);
        }
        return outcome;
    }
    private static void CloseRecoveredHost(ProtectedWindowState state)
    {
        // A host may only close after its original child was detached or has gone away.
        try
        {
            if (state.WrapperForm is { IsDisposed: false } form &&
                (!IsWindow(state.Handle) || GetRecoveryParent(state.Handle) != form.Handle)) form.Close();
            if (state.WrapperWindow is { } window)
            {
                var host = new WindowInteropHelper(window).Handle;
                if (!IsWindow(state.Handle) || GetRecoveryParent(state.Handle) != host) window.Close();
            }
        }
        catch { }
    }
    private void UpdateRecoveredApp(GuardedApp app)
    {
        app.IsProtected = app.ProtectedWindows.Count > 0;
        app.DesktopId = app.ProtectedWindows.FirstOrDefault(state => state.DesktopId.HasValue)?.DesktopId;
        app.HiddenByStreamGuard = app.ProtectedWindows.Any(state => state.HiddenByStreamGuard);
        if (!app.IsProtected) app.Status = "Visible";
        Refresh(app);
    }
    private void SynchronizeRecoveryProtectionState()
    {
        SetProtectionUiState(HasActiveAppProtection());
        UpdateCursorShieldForProtectionState();
        if (!HasActiveAppProtection() && _recovery.PendingCount == 0) _vdService.RemoveHiddenDesktop();
        UpdateStatus(_recovery.PendingCount == 0 ? "Recovery completed." : "Some recovery items still need attention.", _recovery.PendingCount == 0);
    }
}
