using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using StreamGuard.Services;

namespace StreamGuard.Tests
{
    internal static class AntigravityInstallerChecks
    {
        public static void Run(Action<bool, string> check, string artifactDirectory)
        {
            TestDesktopDistinctionFromIdeAndCli(check);
            TestInstallerPathResolution(check);
            TestSilentArgumentsSelection(check);
            TestCurrentSystemInstallationDetection(check);
            TestMockInstallationWorkflow(check);
            TestLaunchHandling(check);
        }

        private static void TestDesktopDistinctionFromIdeAndCli(Action<bool, string> check)
        {
            // 1. Exact known GUID
            check(AntigravityInstallerService.IsAntigravityDesktopRegistryEntry(
                AntigravityInstallerService.KnownDesktopUninstallGuid,
                "Antigravity 2.32.1"),
                "Known GUID for Antigravity Desktop is recognized");

            check(AntigravityInstallerService.IsAntigravityDesktopRegistryEntry(
                AntigravityInstallerService.KnownDesktopUninstallGuid,
                null),
                "Known GUID matches even without DisplayName");

            // 2. Generic Desktop names
            check(AntigravityInstallerService.IsAntigravityDesktopRegistryEntry(
                "any-random-guid",
                "Antigravity 2.33.0"),
                "Standard Antigravity Desktop version entry is recognized");

            check(AntigravityInstallerService.IsAntigravityDesktopRegistryEntry(
                "antigravity-app",
                "Antigravity Desktop (Agentic AI)"),
                "Antigravity Desktop full title is recognized");

            check(AntigravityInstallerService.IsAntigravityDesktopRegistryEntry(
                "google-antigravity",
                "Google Antigravity 2.32.1"),
                "Google Antigravity variant is recognized");

            // 3. Strict exclusion of Antigravity IDE (even with known GUID)
            check(!AntigravityInstallerService.IsAntigravityDesktopRegistryEntry(
                AntigravityInstallerService.KnownDesktopUninstallGuid,
                "Antigravity IDE 2.7.0"),
                "Known GUID with IDE DisplayName is strictly excluded");

            check(!AntigravityInstallerService.IsAntigravityDesktopRegistryEntry(
                "{AA73B3E3-C6C8-45C8-B1DC-4AE56C751432}_is1",
                "Antigravity IDE (User)"),
                "Antigravity IDE (User) is strictly excluded");

            check(!AntigravityInstallerService.IsAntigravityDesktopRegistryEntry(
                "Google.AntigravityIDE",
                "Google Antigravity IDE"),
                "Google Antigravity IDE is strictly excluded");

            check(!AntigravityInstallerService.IsAntigravityDesktopRegistryEntry(
                "any-guid",
                "Antigravity IDE 2.7.0"),
                "DisplayName containing IDE is strictly excluded");

            check(!AntigravityInstallerService.IsAntigravityDesktopRegistryEntry(
                "AntigravityIDE_x64",
                "Antigravity"),
                "KeyName containing IDE is strictly excluded");

            // 4. Strict exclusion of Antigravity CLI (even with known GUID)
            check(!AntigravityInstallerService.IsAntigravityDesktopRegistryEntry(
                AntigravityInstallerService.KnownDesktopUninstallGuid,
                "Antigravity CLI"),
                "Known GUID with CLI DisplayName is strictly excluded");

            check(!AntigravityInstallerService.IsAntigravityDesktopRegistryEntry(
                "any-guid",
                "Antigravity CLI"),
                "Antigravity CLI in DisplayName is strictly excluded");

            check(!AntigravityInstallerService.IsAntigravityDesktopRegistryEntry(
                "AntigravityCLI",
                "Antigravity"),
                "Antigravity CLI in KeyName is strictly excluded");

            check(!AntigravityInstallerService.IsAntigravityDesktopRegistryEntry(
                "antigravity-cli-tools",
                "Antigravity Command Line Interface"),
                "CLI entry is strictly excluded");

            // 5. Null and empty robustness
            check(!AntigravityInstallerService.IsAntigravityDesktopRegistryEntry(null, null),
                "Null keyName and displayName is safely rejected without exception");

            check(!AntigravityInstallerService.IsAntigravityDesktopRegistryEntry("", ""),
                "Empty strings are safely rejected");

            // 6. Unrelated software
            check(!AntigravityInstallerService.IsAntigravityDesktopRegistryEntry(
                "vscode",
                "Visual Studio Code"),
                "VS Code is not recognized as Antigravity Desktop");
        }

        private static void TestInstallerPathResolution(Action<bool, string> check)
        {
            var service = new AntigravityInstallerService();
            var path = service.GetOfflineInstallerPath();

            check(!string.IsNullOrEmpty(path), "Offline installer path is located");
            if (!string.IsNullOrEmpty(path))
            {
                check(File.Exists(path), $"Offline installer file exists at {path}");
                var fileInfo = new FileInfo(path);
                check(fileInfo.Length > 100 * 1024 * 1024, $"Offline installer size is ~199MB (actual: {fileInfo.Length / (1024 * 1024)} MB)");
            }

            // Custom locator override test
            var mockPath = @"C:\Mock\installers\AntigravitySetup.exe";
            var mockService = new AntigravityInstallerService(customInstallerLocator: () => mockPath);
            check(mockService.GetOfflineInstallerPath() == mockPath, "Custom installer locator override is honored");
        }

        private static void TestSilentArgumentsSelection(Action<bool, string> check)
        {
            var defaultArgs = AntigravityInstallerService.GetSilentArguments("nonexistent.exe");
            check(defaultArgs == "/S", "Default silent flag is /S");

            var service = new AntigravityInstallerService();
            var realInstaller = service.GetOfflineInstallerPath();
            if (!string.IsNullOrEmpty(realInstaller) && File.Exists(realInstaller))
            {
                var realArgs = AntigravityInstallerService.GetSilentArguments(realInstaller);
                check(!string.IsNullOrWhiteSpace(realArgs), $"Real offline installer produces valid silent arguments: {realArgs}");
                check(realArgs.Contains("/VERYSILENT", StringComparison.OrdinalIgnoreCase) || realArgs.Contains("/S", StringComparison.OrdinalIgnoreCase),
                    "Silent arguments support Inno Setup /VERYSILENT or NSIS /S");
            }
        }

        private static void TestCurrentSystemInstallationDetection(Action<bool, string> check)
        {
            var service = new AntigravityInstallerService();
            var info = service.GetInstallationInfo();

            check(info.IsInstalled, "Antigravity Desktop installation is detected on current machine");
            if (info.IsInstalled)
            {
                check(!string.IsNullOrWhiteSpace(info.Version), $"Detected version is populated: {info.Version}");
                check(!string.IsNullOrWhiteSpace(info.ExePath), $"Detected ExePath is populated: {info.ExePath}");
                if (!string.IsNullOrWhiteSpace(info.ExePath))
                {
                    check(File.Exists(info.ExePath), $"Detected ExePath exists on disk: {info.ExePath}");
                    check(info.ExePath.EndsWith("Antigravity.exe", StringComparison.OrdinalIgnoreCase), "Executable name is Antigravity.exe");
                }
            }
        }

        private static void TestMockInstallationWorkflow(Action<bool, string> check)
        {
            bool? receivedSilentArg = null;
            var installedState = false;

            var mockService = new AntigravityInstallerService(
                customDetector: () => new AntigravityInstallationInfo(
                    installedState,
                    installedState ? "2.32.1" : null,
                    installedState ? @"C:\Program Files\antigravity\Antigravity.exe" : null,
                    installedState ? @"C:\Program Files\antigravity" : null),
                customInstallerLocator: () => @"C:\Mock\AntigravitySetup.exe",
                customProcessLauncher: silent =>
                {
                    receivedSilentArg = silent;
                    installedState = true;
                    return Task.FromResult(true);
                });

            AntigravityInstallationInfo? eventInfo = null;
            mockService.InstallationChanged += (s, e) => eventInfo = e;

            // 1. Interactive install
            var interactiveResult = mockService.InstallAsync(silent: false).GetAwaiter().GetResult();
            check(interactiveResult.Success, "Interactive install succeeded");
            check(receivedSilentArg == false, "Interactive install passed silent = false");
            check(interactiveResult.Info.IsInstalled, "Install result info reflects installed state");
            check(eventInfo != null && eventInfo.IsInstalled, "InstallationChanged event fired with installed info");

            // 2. Silent install
            installedState = false;
            eventInfo = null;
            var silentResult = mockService.InstallAsync(silent: true).GetAwaiter().GetResult();
            check(silentResult.Success, "Silent install succeeded");
            check(receivedSilentArg == true, "Silent install passed silent = true (/S)");
            check(silentResult.Info.IsInstalled, "Silent install result info reflects installed state");

            // 3. Missing installer handling
            var missingInstallerService = new AntigravityInstallerService(
                customDetector: () => AntigravityInstallationInfo.NotInstalled,
                customInstallerLocator: () => null);

            var missingResult = missingInstallerService.InstallAsync(silent: true).GetAwaiter().GetResult();
            check(!missingResult.Success, "Missing installer fails gracefully");
            check(missingResult.Message.Contains("Offline installer not found", StringComparison.OrdinalIgnoreCase),
                "Missing installer error message informs user");
        }

        private static void TestLaunchHandling(Action<bool, string> check)
        {
            var launched = false;
            var mockService = new AntigravityInstallerService(
                customDetector: () => new AntigravityInstallationInfo(
                    true, "2.32.1", @"C:\Fake\Antigravity.exe", @"C:\Fake"),
                customLaunchHandler: () => { launched = true; return true; });

            var result = mockService.LaunchAntigravity();
            check(result && launched, "LaunchAntigravity invokes handler and returns success");

            var uninstalledService = new AntigravityInstallerService(
                customDetector: () => AntigravityInstallationInfo.NotInstalled);
            check(!uninstalledService.LaunchAntigravity(), "LaunchAntigravity returns false when not installed");
        }

        public static void RunUiChecks(MainWindow window, Action<bool, string> check)
        {
            var btnInstall = window.FindName("BtnInstallAntigravity") as Button;
            var btnSilent = window.FindName("BtnSilentInstallAntigravity") as Button;
            var btnLaunch = window.FindName("BtnLaunchAntigravity") as Button;
            var notInstalledPanel = window.FindName("AntigravityNotInstalledPanel") as StackPanel;
            var installedPanel = window.FindName("AntigravityInstalledPanel") as StackPanel;
            var badgeText = window.FindName("AntigravityStatusBadgeText") as TextBlock;
            var badgeBorder = window.FindName("AntigravityStatusBadgeBorder") as Border;
            var dndCheckbox = window.FindName("MuteAntigravityNotificationsCheckBox") as CheckBox;
            var versionText = window.FindName("AntigravityVersionInfoText") as TextBlock;

            check(btnInstall != null, "BtnInstallAntigravity exists in MainWindow");
            check(btnSilent != null, "BtnSilentInstallAntigravity exists in MainWindow");
            check(btnLaunch != null, "BtnLaunchAntigravity exists in MainWindow");
            check(notInstalledPanel != null, "AntigravityNotInstalledPanel exists in MainWindow");
            check(installedPanel != null, "AntigravityInstalledPanel exists in MainWindow");
            check(badgeText != null, "AntigravityStatusBadgeText exists in MainWindow");
            check(badgeBorder != null, "AntigravityStatusBadgeBorder exists in MainWindow");
            check(dndCheckbox != null, "MuteAntigravityNotificationsCheckBox exists in MainWindow");
            check(versionText != null, "AntigravityVersionInfoText exists in MainWindow");

            var detector = new AntigravityInstallerService();
            var info = detector.GetInstallationInfo();
            if (info.IsInstalled)
            {
                check(installedPanel?.Visibility == Visibility.Visible, "Installed panel is visible when Antigravity is detected");
                check(notInstalledPanel?.Visibility == Visibility.Collapsed, "Not installed panel is collapsed when Antigravity is detected");
                check(btnLaunch != null && btnLaunch.IsEnabled, "BtnLaunch is enabled when Antigravity is installed and executable exists");
            }
            else
            {
                check(installedPanel?.Visibility == Visibility.Collapsed, "Installed panel is collapsed when Antigravity is not detected");
                check(notInstalledPanel?.Visibility == Visibility.Visible, "Not installed panel is visible when Antigravity is not detected");
            }
        }
    }
}
