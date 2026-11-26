using System.Windows;
using System.Threading;
using StreamGuard.Services;
using StreamGuard.Helpers;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace StreamGuard
{
    public partial class App : System.Windows.Application
    {
        private Mutex? _singleInstanceMutex;
        private EventWaitHandle? _showExistingEvent;
        private Thread? _showExistingThread;
        private bool _ownsSingleInstanceMutex;
        private volatile bool _isExiting;
        private string _activeShowEventName = SingleInstanceHelper.GlobalShowEventName;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Layer 1 - Startup Self-Healing:
            // Restore system cursors immediately in case a prior session crashed or terminated abnormally while cursor shield was active.
            try { CursorShieldService.RestoreAllCursors(); } catch { }
            try { CursorCaptureProtectionService.RestoreSystemCursors(); } catch { }

            Controls.HoverFeedback.Initialize();
            var preferences = new ConfigService();
            preferences.Load();
            Localization.LocalizationService.Current.SetLanguage(preferences.Language);
            InstallCrashLogging();

            if (SecureDesktopService.TryRunReturnHelper(e.Args))
            {
                Shutdown();
                return;
            }

            try
            {
                var acquisition = SingleInstanceHelper.AcquireSingleInstance();
                _singleInstanceMutex = acquisition.Mutex;
                _activeShowEventName = acquisition.ShowEventName;

                if (!acquisition.CreatedNew)
                {
                    SignalExistingInstance();
                    Shutdown();
                    return;
                }

                _ownsSingleInstanceMutex = true;

                // Layer 1 - Startup Self-Healing:
                // Scan and sweep any leftover system tool blocks from a previous crash or abnormal exit.
                // Restore system cursors if previous session crashed while cursor shield was active.
                // Layer 3 - Static emergency restore tool:
                // Ensure Emergency_Restore_Tools.bat exists beside the application executable.
                try
                {
                    CursorShieldService.RestoreAllCursors();
                    CursorCaptureProtectionService.RestoreSystemCursors();
                    SystemToolsBlockerService.ForceUnlockAllTools();
                    SystemToolsBlockerService.EnsureEmergencyRestoreScript();
                }
                catch (Exception ex)
                {
                    LogCrash("StartupSelfHealing", ex);
                }

                _showExistingEvent = SingleInstanceHelper.CreateShowExistingEvent(_activeShowEventName);
                _showExistingThread = new Thread(WaitForShowExistingRequests)
                {
                    IsBackground = true,
                    Name = "Driver Booster show-existing listener"
                };
                _showExistingThread.Start();
                base.OnStartup(e);
            }
            catch (Exception ex)
            {
                LogCrash("Startup", ex);
                try { CursorShieldService.RestoreAllCursors(); } catch { }
                try { SystemToolsBlockerService.ForceUnlockAllTools(); } catch { }
                System.Windows.MessageBox.Show(
                    Localization.LocalizationService.Current.Translate($"Driver Booster startup failed:\n{ex.Message}\n\nLog: {CrashLogPath}"),
                    "Driver Booster",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _isExiting = true;
            try { _showExistingEvent?.Set(); } catch { }
            if (_ownsSingleInstanceMutex)
            {
                try { CursorShieldService.RestoreAllCursors(); } catch { }
                try { CursorCaptureProtectionService.RestoreSystemCursors(); } catch { }
                try { SystemToolsBlockerService.ForceUnlockAllTools(); } catch { }
            }
            (MainWindow as MainWindow)?.CleanupAndExit();
            _showExistingEvent?.Dispose();
            if (_ownsSingleInstanceMutex)
                _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }

        private void SignalExistingInstance()
        {
            SingleInstanceHelper.SignalExistingInstance(_activeShowEventName);
        }

        private void WaitForShowExistingRequests()
        {
            while (!_isExiting)
            {
                try
                {
                    if (_showExistingEvent?.WaitOne(500) != true)
                        continue;

                    if (_isExiting)
                        break;

                    Dispatcher.BeginInvoke(() =>
                    {
                        if (MainWindow is MainWindow mainWindow)
                            mainWindow.ShowFromBackground();
                    });
                }
                catch
                {
                    if (_isExiting)
                        break;
                }
            }
        }

        private static string CrashLogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StreamGuard",
            "crash.log");

        private void InstallCrashLogging()
        {
            DispatcherUnhandledException += (_, e) =>
            {
                LogCrash("DispatcherUnhandledException", e.Exception);
                try { CursorShieldService.RestoreAllCursors(); } catch { }
                try { CursorCaptureProtectionService.RestoreSystemCursors(); } catch { }
                e.Handled = true;

                if (_isExiting)
                    return;

                System.Windows.MessageBox.Show(
                    Localization.LocalizationService.Current.Translate($"Driver Booster hit an error and kept running:\n{e.Exception.Message}\n\nLog: {CrashLogPath}"),
                    "Driver Booster",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            };

            // Layer 2 - Crash Handler:
            // Unlock all system tools immediately if an unhandled domain exception occurs.
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    LogCrash("UnhandledException", ex);

                try
                {
                    CursorShieldService.RestoreAllCursors();
                    CursorCaptureProtectionService.RestoreSystemCursors();
                    SystemToolsBlockerService.ForceUnlockAllTools();
                    if (MainWindow is MainWindow mainWindow)
                    {
                        if (mainWindow.Dispatcher.CheckAccess())
                        {
                            mainWindow.CleanupAndExit();
                        }
                        else
                        {
                            try
                            {
                                mainWindow.Dispatcher.Invoke(() => mainWindow.CleanupAndExit());
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            };

            // Layer 2 - Crash / Termination Handler:
            // Clean registry keys upon process exit so unexpected terminations won't leave Windows locked.
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try { CursorShieldService.RestoreAllCursors(); } catch { }
                try { CursorCaptureProtectionService.RestoreSystemCursors(); } catch { }
                if (_ownsSingleInstanceMutex)
                {
                    try
                    {
                        SystemToolsBlockerService.ForceUnlockAllTools();
                    }
                    catch { }
                }
            };

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                LogCrash("UnobservedTaskException", e.Exception);
                e.SetObserved();
            };
        }

        private static void LogCrash(string source, Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
                File.AppendAllText(
                    CrashLogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\r\n{ex}\r\n\r\n");
            }
            catch
            {
            }
        }
    }
}
