using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using StreamGuard.Localization;
using StreamGuard.Services;

namespace StreamGuard;

public partial class MainWindow
{
    private bool _isBrowserInitialized;
    private bool _isBrowserInitializing;
    private bool _browserEventsHooked;
    private uint _browserProcessId;
    private string? _copyPasteScriptId;
    private string? _antiBlurScriptId;
    private string? _keystrokeGuardScriptId;
    private string? _pendingNavigateUrl;

    private async Task EnsureBrowserInitializedAsync()
    {
        if (_isBrowserInitialized || _isBrowserInitializing) return;
        _isBrowserInitializing = true;

        try
        {
            var userDataFolder = BrowserService.GetUserDataFolderPath();
            Directory.CreateDirectory(userDataFolder);

            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder, null);
            await BrowserControl.EnsureCoreWebView2Async(env);

            _isBrowserInitialized = true;
            try { _browserProcessId = BrowserControl.CoreWebView2.BrowserProcessId; } catch { }
            if (BrowserFallbackPanel != null) BrowserFallbackPanel.Visibility = Visibility.Collapsed;
            if (BrowserControl != null)
            {
                if (TabBrowser?.IsChecked == true && ViewBrowser?.Visibility == Visibility.Visible)
                {
                    BrowserControl.Visibility = Visibility.Visible;
                }
                else
                {
                    BrowserControl.Visibility = Visibility.Collapsed;
                }
            }

            HookBrowserEvents();
            SyncBrowserColorScheme();
            await ApplyActiveBrowserScriptsAsync();

            var targetUrl = !string.IsNullOrWhiteSpace(_pendingNavigateUrl)
                ? _pendingNavigateUrl
                : (BrowserControl?.Source == null || BrowserControl.Source.ToString() == "about:blank"
                    ? BrowserService.DefaultHomeUrl
                    : null);
            _pendingNavigateUrl = null;

            if (BrowserControl?.CoreWebView2 != null && targetUrl != null)
            {
                BrowserControl.CoreWebView2.Navigate(targetUrl);
            }
        }
        catch (Exception)
        {
            _isBrowserInitialized = false;
            UnhookBrowserEvents();
            if (BrowserControl != null) BrowserControl.Visibility = Visibility.Collapsed;
            if (BrowserFallbackPanel != null) BrowserFallbackPanel.Visibility = Visibility.Visible;
            UpdateStatus(LocalizationService.GetString("Browser.RuntimeMissingNotice", "Cannot initialize browser: WebView2 Runtime is not installed on this machine."), false);
        }
        finally
        {
            _isBrowserInitializing = false;
        }
    }

    private void HookBrowserEvents()
    {
        if (_browserEventsHooked || BrowserControl?.CoreWebView2 == null) return;
        _browserEventsHooked = true;

        BrowserControl.NavigationStarting += OnBrowserNavigationStarting;
        BrowserControl.NavigationCompleted += OnBrowserNavigationCompleted;
        BrowserControl.SourceChanged += OnBrowserSourceChanged;
        BrowserControl.CoreWebView2.HistoryChanged += OnBrowserHistoryChanged;
    }

    private void UnhookBrowserEvents()
    {
        if (!_browserEventsHooked) return;
        _browserEventsHooked = false;

        if (BrowserControl != null)
        {
            try { BrowserControl.NavigationStarting -= OnBrowserNavigationStarting; } catch { }
            try { BrowserControl.NavigationCompleted -= OnBrowserNavigationCompleted; } catch { }
            try { BrowserControl.SourceChanged -= OnBrowserSourceChanged; } catch { }
            try
            {
                if (BrowserControl.CoreWebView2 != null)
                {
                    BrowserControl.CoreWebView2.HistoryChanged -= OnBrowserHistoryChanged;
                }
            }
            catch { }
        }
    }

    private void OnBrowserNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (TxtBrowserUrl != null && !TxtBrowserUrl.IsKeyboardFocusWithin)
        {
            TxtBrowserUrl.Text = e.Uri;
        }

        if (AntigravityAuthBridgeService.IsLoopbackAuthCallback(e.Uri))
        {
            UpdateStatus(LocalizationService.GetString("AgyBridge.Success", "Xác thực Antigravity thành công! Phiên làm việc đã được đồng bộ."), true);
        }
    }

    private void OnBrowserNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (TxtBrowserUrl != null && !TxtBrowserUrl.IsKeyboardFocusWithin && BrowserControl?.Source != null)
        {
            TxtBrowserUrl.Text = BrowserControl.Source.ToString();
        }
        if (BtnBrowserBack != null && BrowserControl != null) BtnBrowserBack.IsEnabled = BrowserControl.CanGoBack;
        if (BtnBrowserForward != null && BrowserControl != null) BtnBrowserForward.IsEnabled = BrowserControl.CanGoForward;

        if (BrowserControl?.Source != null && AntigravityAuthBridgeService.IsLoopbackAuthCallback(BrowserControl.Source.ToString()))
        {
            UpdateStatus(LocalizationService.GetString("AgyBridge.Success", "Xác thực Antigravity thành công! Phiên làm việc đã được đồng bộ."), true);
        }
    }

    private void OnBrowserSourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        if (TxtBrowserUrl != null && !TxtBrowserUrl.IsKeyboardFocusWithin && BrowserControl?.Source != null)
        {
            TxtBrowserUrl.Text = BrowserControl.Source.ToString();
        }
        if (BtnBrowserBack != null && BrowserControl != null) BtnBrowserBack.IsEnabled = BrowserControl.CanGoBack;
        if (BtnBrowserForward != null && BrowserControl != null) BtnBrowserForward.IsEnabled = BrowserControl.CanGoForward;
    }

    private void OnBrowserHistoryChanged(object? sender, object e)
    {
        if (BtnBrowserBack != null && BrowserControl != null) BtnBrowserBack.IsEnabled = BrowserControl.CanGoBack;
        if (BtnBrowserForward != null && BrowserControl != null) BtnBrowserForward.IsEnabled = BrowserControl.CanGoForward;
    }

    private void CleanupBrowser()
    {
        if (!Dispatcher.CheckAccess())
        {
            try { Dispatcher.Invoke(CleanupBrowser); } catch { }
            return;
        }

        try
        {
            UnhookBrowserEvents();
            if (BrowserControl != null)
            {
                if (BrowserControl.CoreWebView2 != null)
                {
                    try { _browserProcessId = BrowserControl.CoreWebView2.BrowserProcessId; } catch { }
                    try { BrowserControl.Stop(); } catch { }
                }
                BrowserControl.Visibility = Visibility.Collapsed;
                BrowserControl.Dispose();
            }

            if (_browserProcessId > 0)
            {
                try
                {
                    using var proc = Process.GetProcessById((int)_browserProcessId);
                    if (!proc.HasExited)
                    {
                        proc.Kill(entireProcessTree: true);
                        proc.WaitForExit(1000);
                    }
                }
                catch { }
            }
        }
        catch { }
        finally
        {
            _isBrowserInitialized = false;
            _isBrowserInitializing = false;
        }
    }

    private async Task ApplyActiveBrowserScriptsAsync()
    {
        if (BrowserControl?.CoreWebView2 == null) return;
        try
        {
            if (ToggleBrowserCopyPaste?.IsChecked == true && _copyPasteScriptId == null)
            {
                var script = BrowserScriptService.GetCopyPasteScript();
                _copyPasteScriptId = await BrowserControl.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
                await BrowserControl.CoreWebView2.ExecuteScriptAsync(script);
            }
            if (ToggleBrowserAntiBlur?.IsChecked == true && _antiBlurScriptId == null)
            {
                var script = BrowserScriptService.GetAntiBlurScript();
                _antiBlurScriptId = await BrowserControl.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
                await BrowserControl.CoreWebView2.ExecuteScriptAsync(script);
            }
            if (ToggleBrowserKeystrokeGuard?.IsChecked == true && _keystrokeGuardScriptId == null)
            {
                var script = BrowserScriptService.GetKeystrokeGuardScript();
                _keystrokeGuardScriptId = await BrowserControl.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
                await BrowserControl.CoreWebView2.ExecuteScriptAsync(script);
            }
        }
        catch { }
    }

    private async void NavigateBrowser(string? urlInput)
    {
        var resolved = BrowserService.ResolveNavigationUrl(urlInput);
        if (TxtBrowserUrl != null)
        {
            TxtBrowserUrl.Text = resolved;
        }
        if (BrowserControl?.CoreWebView2 != null)
        {
            BrowserControl.CoreWebView2.Navigate(resolved);
        }
        else
        {
            _pendingNavigateUrl = resolved;
            await EnsureBrowserInitializedAsync();
        }
    }

    private void BtnBrowserBack_Click(object sender, RoutedEventArgs e)
    {
        if (BrowserControl?.CanGoBack == true)
        {
            BrowserControl.GoBack();
        }
    }

    private void BtnBrowserForward_Click(object sender, RoutedEventArgs e)
    {
        if (BrowserControl?.CanGoForward == true)
        {
            BrowserControl.GoForward();
        }
    }

    private void BtnBrowserReload_Click(object sender, RoutedEventArgs e)
    {
        if (BrowserControl?.CoreWebView2 != null)
        {
            BrowserControl.Reload();
        }
        else
        {
            _ = EnsureBrowserInitializedAsync();
        }
    }

    private void BtnBrowserHome_Click(object sender, RoutedEventArgs e)
    {
        NavigateBrowser(BrowserService.DefaultHomeUrl);
    }

    private void BtnBrowserGo_Click(object sender, RoutedEventArgs e)
    {
        NavigateBrowser(TxtBrowserUrl?.Text);
    }

    private void TxtBrowserUrl_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            NavigateBrowser(TxtBrowserUrl?.Text);
            e.Handled = true;
        }
    }

    private async void ToggleBrowserCopyPaste_Checked(object sender, RoutedEventArgs e)
    {
        if (BrowserControl?.CoreWebView2 == null) return;
        try
        {
            var script = BrowserScriptService.GetCopyPasteScript();
            _copyPasteScriptId = await BrowserControl.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
            await BrowserControl.CoreWebView2.ExecuteScriptAsync(script);
            UpdateStatus(LocalizationService.GetString("Browser.ClipboardUnlockedNotice", "Copy/Paste unlock script injected into the page."), true);
        }
        catch (Exception ex)
        {
            UpdateStatus(ex.Message, false);
        }
    }

    private async void ToggleBrowserCopyPaste_Unchecked(object sender, RoutedEventArgs e)
    {
        if (BrowserControl?.CoreWebView2 == null) return;
        try
        {
            if (_copyPasteScriptId != null)
            {
                BrowserControl.CoreWebView2.RemoveScriptToExecuteOnDocumentCreated(_copyPasteScriptId);
                _copyPasteScriptId = null;
            }
            await BrowserControl.CoreWebView2.ExecuteScriptAsync(BrowserScriptService.GetDisableCopyPasteScript());
            UpdateStatus(LocalizationService.GetString("Browser.ClipboardLockedNotice", "Copy/Paste unlock disabled."), true);
        }
        catch (Exception ex)
        {
            UpdateStatus(ex.Message, false);
        }
    }

    private async void ToggleBrowserAntiBlur_Checked(object sender, RoutedEventArgs e)
    {
        if (BrowserControl?.CoreWebView2 == null) return;
        try
        {
            var script = BrowserScriptService.GetAntiBlurScript();
            _antiBlurScriptId = await BrowserControl.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
            await BrowserControl.CoreWebView2.ExecuteScriptAsync(script);
            UpdateStatus(LocalizationService.GetString("Browser.AntiBlurEnabledNotice", "Anti-Blur / Focus lock active on this page."), true);
        }
        catch (Exception ex)
        {
            UpdateStatus(ex.Message, false);
        }
    }

    private async void ToggleBrowserAntiBlur_Unchecked(object sender, RoutedEventArgs e)
    {
        if (BrowserControl?.CoreWebView2 == null) return;
        try
        {
            if (_antiBlurScriptId != null)
            {
                BrowserControl.CoreWebView2.RemoveScriptToExecuteOnDocumentCreated(_antiBlurScriptId);
                _antiBlurScriptId = null;
            }
            await BrowserControl.CoreWebView2.ExecuteScriptAsync(BrowserScriptService.GetDisableAntiBlurScript());
            UpdateStatus(LocalizationService.GetString("Browser.AntiBlurDisabledNotice", "Anti-Blur / Focus lock disabled. Reload page to restore normal tab events."), true);
        }
        catch (Exception ex)
        {
            UpdateStatus(ex.Message, false);
        }
    }

    private async void ToggleBrowserKeystrokeGuard_Checked(object sender, RoutedEventArgs e)
    {
        if (BrowserControl?.CoreWebView2 == null) return;
        try
        {
            var script = BrowserScriptService.GetKeystrokeGuardScript();
            _keystrokeGuardScriptId = await BrowserControl.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
            await BrowserControl.CoreWebView2.ExecuteScriptAsync(script);
            UpdateStatus(LocalizationService.GetString("Browser.KeystrokeGuardEnabledNotice", "Keystroke privacy guard active on this page."), true);
        }
        catch (Exception ex)
        {
            UpdateStatus(ex.Message, false);
        }
    }

    private async void ToggleBrowserKeystrokeGuard_Unchecked(object sender, RoutedEventArgs e)
    {
        if (BrowserControl?.CoreWebView2 == null) return;
        try
        {
            if (_keystrokeGuardScriptId != null)
            {
                BrowserControl.CoreWebView2.RemoveScriptToExecuteOnDocumentCreated(_keystrokeGuardScriptId);
                _keystrokeGuardScriptId = null;
            }
            await BrowserControl.CoreWebView2.ExecuteScriptAsync(BrowserScriptService.GetDisableKeystrokeGuardScript());
            UpdateStatus(LocalizationService.GetString("Browser.KeystrokeGuardDisabledNotice", "Keystroke privacy guard disabled. Reload page to restore normal key listeners."), true);
        }
        catch (Exception ex)
        {
            UpdateStatus(ex.Message, false);
        }
    }

    private void BtnBrowserDevTools_Click(object sender, RoutedEventArgs e)
    {
        if (BrowserControl?.CoreWebView2 != null)
        {
            BrowserControl.CoreWebView2.OpenDevToolsWindow();
        }
    }

    private void BtnOpenInExternalChrome_Click(object sender, RoutedEventArgs e)
    {
        var targetUrl = !string.IsNullOrWhiteSpace(TxtBrowserUrl?.Text)
            ? TxtBrowserUrl.Text
            : BrowserService.DefaultHomeUrl;
        var resolvedUrl = BrowserService.ResolveNavigationUrl(targetUrl);

        var chromeFound = BrowserService.FindChromeExecutable() != null;
        BrowserService.OpenInExternalBrowser(resolvedUrl, preferChrome: true);

        if (chromeFound)
        {
            var template = LocalizationService.GetString("Browser.OpenExternalNotice", "Opening {0} in external browser");
            UpdateStatus(string.Format(template, resolvedUrl), true);
        }
        else
        {
            UpdateStatus(LocalizationService.GetString("Browser.ChromeNotFound", "Google Chrome was not found. Opening in default system browser instead."), true);
        }
    }

    private void BtnDownloadWebView2_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo("https://go.microsoft.com/fwlink/p/?LinkId=2124703") { UseShellExecute = true });
        }
        catch { }
    }

    private void BtnOpenFallbackExternal_Click(object sender, RoutedEventArgs e)
    {
        var targetUrl = !string.IsNullOrWhiteSpace(TxtBrowserUrl?.Text)
            ? TxtBrowserUrl.Text
            : BrowserService.DefaultHomeUrl;
        var resolvedUrl = BrowserService.ResolveNavigationUrl(targetUrl);
        BrowserService.OpenInExternalBrowser(resolvedUrl, preferChrome: false);
    }

    private void BtnBrowserAntigravityLogin_Click(object sender, RoutedEventArgs e)
    {
        StartAntigravitySecureLoginFlow();
    }

    internal void SyncBrowserColorScheme()
    {
        try
        {
            if (BrowserControl?.CoreWebView2?.Profile != null)
            {
                BrowserControl.CoreWebView2.Profile.PreferredColorScheme = Services.ThemeService.Instance.IsDark
                    ? CoreWebView2PreferredColorScheme.Dark
                    : CoreWebView2PreferredColorScheme.Light;
            }
        }
        catch
        {
        }
    }
}
