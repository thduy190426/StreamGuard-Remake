using System;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace StreamGuard.Services
{
    public sealed class AntigravityAuthBridgeService : IDisposable
    {
        private static readonly Regex GoogleAuthUrlRegex = new(
            @"https://accounts\.google\.com/[^\s""'<>]+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly object _syncLock = new();
        private readonly Func<string?>? _customClipboardReader;
        private readonly Action? _customClipboardClearer;
        private readonly Action<int>? _customProcessTerminator;
        private readonly Action<Action>? _uiDispatcher;

        private ManagementEventWatcher? _processWatcher;
        private CancellationTokenSource? _listeningCts;
        private bool _isListening;
        private bool _disposed;

        public bool IsListening
        {
            get
            {
                lock (_syncLock)
                {
                    return _isListening;
                }
            }
        }

        public event EventHandler<string>? AuthUrlCaptured;
        public event EventHandler<bool>? ListeningStateChanged;
        public event EventHandler? TimeoutOccurred;

        public AntigravityAuthBridgeService(
            Func<string?>? customClipboardReader = null,
            Action<int>? customProcessTerminator = null,
            Action<Action>? uiDispatcher = null,
            Action? customClipboardClearer = null)
        {
            _customClipboardReader = customClipboardReader;
            _customClipboardClearer = customClipboardClearer;
            _customProcessTerminator = customProcessTerminator;
            _uiDispatcher = uiDispatcher;
        }

        public static bool IsValidGoogleAuthUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
                return false;

            if (uri.Scheme != Uri.UriSchemeHttps)
                return false;

            if (!string.Equals(uri.Host, "accounts.google.com", StringComparison.OrdinalIgnoreCase))
                return false;

            var path = uri.AbsolutePath.ToLowerInvariant();
            if (!path.Contains("oauth") && !path.Contains("signin") && !path.Contains("auth"))
                return false;

            var query = uri.Query.ToLowerInvariant();
            // Google OAuth requests typically specify response_type, client_id, and redirect_uri
            var hasRedirect = query.Contains("redirect_uri=");
            var hasClientId = query.Contains("client_id=");
            var hasResponseType = query.Contains("response_type=");

            return hasRedirect || hasClientId || hasResponseType;
        }

        public static bool IsLoopbackAuthCallback(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
                return false;

            var host = uri.Host.ToLowerInvariant();
            if (host != "127.0.0.1" && host != "localhost")
                return false;

            var path = uri.AbsolutePath.ToLowerInvariant();
            var query = uri.Query.ToLowerInvariant();

            return path.Contains("callback") ||
                   path.Contains("oauth") ||
                   query.Contains("code=") ||
                   query.Contains("state=");
        }

        public static string? ExtractAuthUrl(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var match = GoogleAuthUrlRegex.Match(text);
            while (match.Success)
            {
                var candidate = match.Value.TrimEnd('.', ',', ';', ')', ']');
                if (IsValidGoogleAuthUrl(candidate))
                {
                    return candidate;
                }
                match = match.NextMatch();
            }

            return null;
        }

        public bool StartListening(Action<string>? onUrlCaptured = null, TimeSpan? timeout = null)
        {
            CancellationTokenSource cts;
            lock (_syncLock)
            {
                if (_disposed) return false;
                StopListeningInternal();

                _isListening = true;
                cts = new CancellationTokenSource();
                _listeningCts = cts;
            }

            TriggerListeningStateChanged(true);

            var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(90);

            // 1. Immediately check current clipboard content
            var initialClipboard = ReadClipboardTextSafe();
            var extracted = ExtractAuthUrl(initialClipboard);
            if (!string.IsNullOrEmpty(extracted))
            {
                ClearClipboardSafe();
                TriggerUrlCaptured(extracted, onUrlCaptured);
                StopListening();
                return true;
            }

            // 2. Start Process Start Watcher for external browser launches
            StartProcessWatcher(cts.Token, onUrlCaptured);

            // 3. Start Clipboard Polling loop
            StartClipboardPolling(cts.Token, onUrlCaptured);

            // 4. Start Timeout Task
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(effectiveTimeout, cts.Token);
                    if (!cts.IsCancellationRequested)
                    {
                        StopListening();
                        TriggerTimeout();
                    }
                }
                catch (OperationCanceledException) { }
            }, cts.Token);

            return true;
        }

        public void StopListening()
        {
            lock (_syncLock)
            {
                if (!_isListening) return;
                StopListeningInternal();
            }

            TriggerListeningStateChanged(false);
        }

        private void StopListeningInternal()
        {
            _isListening = false;
            try
            {
                _listeningCts?.Cancel();
                _listeningCts?.Dispose();
            }
            catch { }
            _listeningCts = null;

            try
            {
                if (_processWatcher != null)
                {
                    _processWatcher.Stop();
                    _processWatcher.Dispose();
                    _processWatcher = null;
                }
            }
            catch { }
        }

        private void StartProcessWatcher(CancellationToken ct, Action<string>? onUrlCaptured)
        {
            try
            {
                _processWatcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
                _processWatcher.EventArrived += (_, args) =>
                {
                    if (ct.IsCancellationRequested || !IsListening) return;

                    try
                    {
                        var processName = Convert.ToString(args.NewEvent.Properties["ProcessName"]?.Value) ?? "";
                        if (IsTargetBrowserProcess(processName))
                        {
                            var pid = Convert.ToInt32(args.NewEvent.Properties["ProcessID"]?.Value);
                            InspectAndHandleBrowserProcess(pid, ct, onUrlCaptured);
                        }
                    }
                    catch { }
                };
                _processWatcher.Start();
            }
            catch
            {
                // Non-admin or WMI event restrictions; clipboard polling remains active
                _processWatcher?.Dispose();
                _processWatcher = null;
            }
        }

        private void StartClipboardPolling(CancellationToken ct, Action<string>? onUrlCaptured)
        {
            Task.Run(async () =>
            {
                var lastSeenText = "";
                while (!ct.IsCancellationRequested && IsListening)
                {
                    try
                    {
                        var text = ReadClipboardTextSafe();
                        if (!string.IsNullOrWhiteSpace(text) && text != lastSeenText)
                        {
                            lastSeenText = text;
                            var url = ExtractAuthUrl(text);
                            if (!string.IsNullOrEmpty(url))
                            {
                                ClearClipboardSafe();
                                TriggerUrlCaptured(url, onUrlCaptured);
                                StopListening();
                                break;
                            }
                        }
                    }
                    catch { }

                    try
                    {
                        await Task.Delay(350, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }, ct);
        }

        private void InspectAndHandleBrowserProcess(int pid, CancellationToken ct, Action<string>? onUrlCaptured)
        {
            if (pid <= 0) return;

            Task.Run(() =>
            {
                try
                {
                    // Query CommandLine for the spawned process
                    using var searcher = new ManagementObjectSearcher(
                        $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
                    using var results = searcher.Get();
                    foreach (ManagementObject obj in results)
                    {
                        var commandLine = Convert.ToString(obj["CommandLine"]) ?? "";
                        var url = ExtractAuthUrl(commandLine);
                        if (!string.IsNullOrEmpty(url))
                        {
                            // Terminate external browser to prevent credential exposure outside the protected browser
                            TerminateProcessSafe(pid);

                            TriggerUrlCaptured(url, onUrlCaptured);
                            StopListening();
                            break;
                        }
                    }
                }
                catch { }
            }, ct);
        }

        private void TriggerUrlCaptured(string url, Action<string>? onUrlCaptured)
        {
            InvokeOnDispatcher(() =>
            {
                AuthUrlCaptured?.Invoke(this, url);
                onUrlCaptured?.Invoke(url);
            });
        }

        private void TriggerListeningStateChanged(bool isListening)
        {
            InvokeOnDispatcher(() => ListeningStateChanged?.Invoke(this, isListening));
        }

        private void TriggerTimeout()
        {
            InvokeOnDispatcher(() => TimeoutOccurred?.Invoke(this, EventArgs.Empty));
        }

        private void InvokeOnDispatcher(Action action)
        {
            if (_uiDispatcher != null)
            {
                try { _uiDispatcher(action); } catch { }
            }
            else
            {
                try { action(); } catch { }
            }
        }

        private string? ReadClipboardTextSafe()
        {
            if (_customClipboardReader != null)
                return _customClipboardReader();

            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    string? result = null;
                    var app = System.Windows.Application.Current;
                    if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess())
                    {
                        app.Dispatcher.Invoke(() =>
                        {
                            if (System.Windows.Clipboard.ContainsText())
                                result = System.Windows.Clipboard.GetText();
                        });
                    }
                    else
                    {
                        if (System.Windows.Clipboard.ContainsText())
                            result = System.Windows.Clipboard.GetText();
                    }

                    return result;
                }
                catch (ExternalException)
                {
                    // Transient clipboard lock by another process (e.g. CLIPBRD_E_CANT_OPEN 0x800401D0)
                    Thread.Sleep(30);
                }
                catch (TaskCanceledException)
                {
                    return null;
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        private void ClearClipboardSafe()
        {
            if (_customClipboardClearer != null)
            {
                try { _customClipboardClearer(); } catch { }
                return;
            }

            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var app = System.Windows.Application.Current;
                    if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess())
                    {
                        app.Dispatcher.Invoke(System.Windows.Clipboard.Clear);
                    }
                    else
                    {
                        System.Windows.Clipboard.Clear();
                    }
                    return;
                }
                catch (ExternalException)
                {
                    Thread.Sleep(30);
                }
                catch
                {
                    return;
                }
            }
        }

        private void TerminateProcessSafe(int pid)
        {
            if (pid <= 0) return;

            if (_customProcessTerminator != null)
            {
                _customProcessTerminator(pid);
                return;
            }

            try
            {
                using var proc = Process.GetProcessById(pid);
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                }
            }
            catch { }
        }

        private static bool IsTargetBrowserProcess(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return false;
            var lower = processName.ToLowerInvariant();
            return lower == "chrome.exe" ||
                   lower == "msedge.exe" ||
                   lower == "brave.exe" ||
                   lower == "firefox.exe" ||
                   lower == "opera.exe" ||
                   lower == "browser.exe";
        }

        public void Dispose()
        {
            lock (_syncLock)
            {
                if (_disposed) return;
                _disposed = true;
                StopListeningInternal();
            }
        }
    }
}
