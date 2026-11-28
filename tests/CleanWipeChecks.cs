using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using StreamGuard.Models;
using StreamGuard.Services;

namespace StreamGuard.Tests;

public static class CleanWipeChecks
{
    public static void Run(Action<bool, string> check, string tempRoot)
    {
        var testDir = Path.Combine(tempRoot, "CleanWipeTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);

        TestPurgeApplicationData(check, testDir);
        TestPurgeExcludingScreenshots(check, testDir);
        TestScreenshotSafetyAndProtectedDirectoryCheck(check, testDir);
        TestSourceRepositorySafety(check, testDir);
        TestUninstallScriptGeneration(check);
        TestMockHostsRevert(check);
        TestRevertAllSystemModifications(check);
        TestTerminateBackgroundProcessesSafety(check);
        TestRecoveryServiceReset(check, testDir);
    }

    private static void TestPurgeApplicationData(Action<bool, string> check, string root)
    {
        var mockAppData = Path.Combine(root, "AppData_StreamGuard");
        var mockLocalAppData = Path.Combine(root, "LocalAppData_StreamGuard");
        var mockTemp = Path.Combine(root, "MockTemp");
        var mockScreenshots = Path.Combine(root, "DriverBooster_Screenshots");

        Directory.CreateDirectory(mockAppData);
        Directory.CreateDirectory(Path.Combine(mockAppData, "Captions", "transcripts"));
        Directory.CreateDirectory(mockLocalAppData);
        Directory.CreateDirectory(Path.Combine(mockLocalAppData, "BrowserProfile", "Default"));
        Directory.CreateDirectory(mockTemp);
        Directory.CreateDirectory(mockScreenshots);

        // Populate files, some with ReadOnly or Hidden attributes
        var roFile = Path.Combine(mockAppData, "config.json");
        File.WriteAllText(roFile, "{\"test\": true}");
        File.SetAttributes(roFile, FileAttributes.ReadOnly);

        var hiddenFile = Path.Combine(mockLocalAppData, "BrowserProfile", "Default", "Preferences");
        File.WriteAllText(hiddenFile, "profile_data");
        File.SetAttributes(hiddenFile, FileAttributes.Hidden);

        var screenshotFile = Path.Combine(mockScreenshots, "Screenshot_20261122_120000.png");
        File.WriteAllText(screenshotFile, "image_bytes");

        var tempTargetFile = Path.Combine(mockTemp, "StreamGuard_test.tmp");
        File.WriteAllText(tempTargetFile, "temp_data");

        var corruptConfigFile = Path.Combine(mockTemp, "config.json.corrupt-9876");
        File.WriteAllText(corruptConfigFile, "corrupt_data");

        var unrelatedTempFile = Path.Combine(mockTemp, "other_app_file.txt");
        File.WriteAllText(unrelatedTempFile, "keep_me");

        // Execute purge with includeScreenshots = true
        var (dirsRemoved, filesRemoved) = CleanWipeService.PurgeApplicationData(
            includeScreenshots: true,
            customScreenshotDir: null,
            appDataOverride: mockAppData,
            localAppDataOverride: mockLocalAppData,
            tempOverride: mockTemp,
            screenshotsOverride: mockScreenshots);

        check(!Directory.Exists(mockAppData), "Mock AppData directory must be completely removed");
        check(!Directory.Exists(mockLocalAppData), "Mock LocalAppData directory must be completely removed");
        check(!Directory.Exists(mockScreenshots), "Mock Screenshots directory must be completely removed");
        check(!File.Exists(tempTargetFile), "Matching temp target file must be purged");
        check(!File.Exists(corruptConfigFile), "Matching corrupt config file must be purged");
        check(File.Exists(unrelatedTempFile), "Unrelated temp files must NOT be touched");
        check(dirsRemoved > 0, "Dirs removed count should be greater than 0");
        check(filesRemoved >= 4, "Files removed count should include all deleted files");
    }

    private static void TestPurgeExcludingScreenshots(Action<bool, string> check, string root)
    {
        var mockAppData = Path.Combine(root, "AppData_ExcludeScreenshots");
        var mockLocalAppData = Path.Combine(root, "LocalAppData_ExcludeScreenshots");
        var mockTemp = Path.Combine(root, "Temp_ExcludeScreenshots");
        var mockScreenshots = Path.Combine(root, "DriverBooster_Screenshots_Keep");

        Directory.CreateDirectory(mockAppData);
        File.WriteAllText(Path.Combine(mockAppData, "data.txt"), "hello");
        Directory.CreateDirectory(mockLocalAppData);
        Directory.CreateDirectory(mockTemp);
        Directory.CreateDirectory(mockScreenshots);
        File.WriteAllText(Path.Combine(mockScreenshots, "pic.png"), "image");

        CleanWipeService.PurgeApplicationData(
            includeScreenshots: false,
            customScreenshotDir: null,
            appDataOverride: mockAppData,
            localAppDataOverride: mockLocalAppData,
            tempOverride: mockTemp,
            screenshotsOverride: mockScreenshots);

        check(!Directory.Exists(mockAppData), "AppData must be purged");
        check(!Directory.Exists(mockLocalAppData), "LocalAppData must be purged");
        check(Directory.Exists(mockScreenshots), "Screenshots directory must be preserved when includeScreenshots is false");
        check(File.Exists(Path.Combine(mockScreenshots, "pic.png")), "Screenshot file preserved");
    }

    private static void TestScreenshotSafetyAndProtectedDirectoryCheck(Action<bool, string> check, string root)
    {
        // 1. MyPictures, Desktop, UserProfile must NOT be recognized as dedicated screenshot folders
        var myPics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        check(!CleanWipeService.IsDedicatedScreenshotDirectory(myPics),
            "MyPictures root must NEVER be flagged as dedicated screenshot directory");

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        check(!CleanWipeService.IsDedicatedScreenshotDirectory(desktop),
            "Desktop must NEVER be flagged as dedicated screenshot directory");

        // 2. A dedicated folder like DriverBooster_Screenshots or Screenshoot should be recognized
        var dedicated = Path.Combine(root, "MyProject_DriverBooster_Screenshots");
        check(CleanWipeService.IsDedicatedScreenshotDirectory(dedicated),
            "Dedicated screenshot directory with DriverBooster name should be recognized");
        check(CleanWipeService.IsDedicatedScreenshotDirectory(@"D:\Screenshoot"),
            "Dedicated D:\\Screenshoot directory should be recognized");

        // 3. Shared directory purge test: if user points screenshot dir to a shared folder,
        // it must only delete screenshot files, NOT the shared folder or unrelated files!
        var sharedFolder = Path.Combine(root, "SharedUserFolder");
        Directory.CreateDirectory(sharedFolder);
        var personalFile = Path.Combine(sharedFolder, "MyFamilyPhoto.jpg");
        var screenshotFile = Path.Combine(sharedFolder, "Screenshot_20261122_150000.png");
        File.WriteAllText(personalFile, "personal");
        File.WriteAllText(screenshotFile, "screenshot");

        CleanWipeService.PurgeApplicationData(
            includeScreenshots: true,
            customScreenshotDir: sharedFolder,
            appDataOverride: Path.Combine(root, "DummyApp"),
            localAppDataOverride: Path.Combine(root, "DummyLocal"),
            tempOverride: Path.Combine(root, "DummyTemp"),
            screenshotsOverride: Path.Combine(root, "DummyDefaultScreen"));

        check(Directory.Exists(sharedFolder), "Shared directory must NOT be deleted");
        check(File.Exists(personalFile), "Personal file in shared directory must NOT be touched");
        check(!File.Exists(screenshotFile), "Screenshot file in shared directory must be purged");
    }

    private static void TestSourceRepositorySafety(Action<bool, string> check, string root)
    {
        // 1. Current bin\Release or bin\Debug paths should be recognized as source repo
        var currentAssembly = typeof(CleanWipeService).Assembly.Location;
        check(CleanWipeService.IsSourceRepository(currentAssembly),
            "Current assembly inside StreamGuard build folder must be recognized as source repository");

        // 2. An arbitrary folder without any .csproj / .cs / .git should NOT be recognized as source repo
        var standaloneFolder = Path.Combine(root, "StandaloneApp", "SubDir");
        Directory.CreateDirectory(standaloneFolder);
        var dummyExe = Path.Combine(standaloneFolder, "Driver Booster.exe");
        File.WriteAllText(dummyExe, "dummy");
        check(!CleanWipeService.IsSourceRepository(dummyExe),
            "Clean folder without source code must NOT be flagged as source repository");

        // 3. Adding a .csproj to a parent should immediately protect it
        var parentFolder = Directory.GetParent(standaloneFolder)!.FullName;
        var fakeCsproj = Path.Combine(parentFolder, "FakeProject.csproj");
        File.WriteAllText(fakeCsproj, "<Project></Project>");
        check(CleanWipeService.IsSourceRepository(dummyExe),
            "Folder with a parent containing .csproj must be flagged as source repository");
    }

    private static void TestUninstallScriptGeneration(Action<bool, string> check)
    {
        var exe = @"C:\Program Files\Driver Booster\Driver Booster.exe";
        var customScreenshots = @"C:\Users\Test\Pictures\DriverBooster_Screenshots";
        var script = CleanWipeService.GenerateUninstallScriptContent(
            exePath: exe,
            currentPid: 9999,
            isSourceRepo: false,
            includeScreenshots: true,
            customScreenshotDir: customScreenshots);

        check(script.Contains("$TargetPid = 9999"), "Script must contain target PID");
        check(script.Contains("Driver Booster.exe"), "Script must reference target executable");
        check(script.Contains("$IsSourceRepo = $false"), "Script must reflect isSourceRepo value");
        check(script.Contains("$IncludeScreenshots = $true"), "Script must reflect includeScreenshots value");
        check(script.Contains("DisableTaskMgr"), "Script must revert DisableTaskMgr");
        check(script.Contains("DisableRegistryTools"), "Script must revert DisableRegistryTools");
        check(script.Contains("DisableCMD"), "Script must revert DisableCMD");
        check(script.Contains("StreamGuard"), "Script must clean StreamGuard firewall rules");
        check(script.Contains("Join-Path $env:APPDATA 'StreamGuard'"), "Script must target APPDATA StreamGuard");
        check(script.Contains("Join-Path $env:LOCALAPPDATA 'StreamGuard'"), "Script must target LOCALAPPDATA StreamGuard");
        check(script.Contains("*.csproj"), "Script must check for .csproj before removing app dir");
        check(script.Contains("$PSCommandPath"), "Script must self-delete via $PSCommandPath");
        check(script.Contains("$exeName.dll"), "Script must target companion dll file");
        check(script.Contains("Emergency_Restore_Tools.bat"), "Script must target emergency restore batch file");
        check(script.Contains("# StreamGuard RAIA block START"), "Script must contain hosts cleanup fallback");
        check(script.Contains("Stop-Process -Id $TargetPid -Force"), "Script must contain force kill fallback for hung process");
    }

    private static void TestMockHostsRevert(Action<bool, string> check)
    {
        var originalHosts = string.Join(Environment.NewLine, new[]
        {
            "127.0.0.1 localhost",
            "::1 localhost",
            ThreatBlockerService.HostsStartMarker,
            "0.0.0.0 raia.edu.vn",
            "0.0.0.0 portal.rikkei.edu.vn",
            ThreatBlockerService.HostsEndMarker,
            "192.168.1.10 gateway.local"
        }) + Environment.NewLine;

        var pattern = $@"(?ms)\r?\n?{Regex.Escape(ThreatBlockerService.HostsStartMarker)}.*?{Regex.Escape(ThreatBlockerService.HostsEndMarker)}\r?\n?";
        var clean = Regex.Replace(originalHosts, pattern, Environment.NewLine).TrimEnd() + Environment.NewLine;

        check(!clean.Contains("raia.edu.vn"), "Hosts cleaned content must not contain raia domain");
        check(!clean.Contains(ThreatBlockerService.HostsStartMarker), "Hosts cleaned content must not contain start marker");
        check(!clean.Contains(ThreatBlockerService.HostsEndMarker), "Hosts cleaned content must not contain end marker");
        check(clean.Contains("127.0.0.1 localhost"), "Hosts cleaned content must preserve localhost");
        check(clean.Contains("gateway.local"), "Hosts cleaned content must preserve other entries");
    }

    private static void TestRevertAllSystemModifications(Action<bool, string> check)
    {
        var success = CleanWipeService.RevertAllSystemModifications(out var logs);
        check(logs != null && logs.Count > 0, "RevertAllSystemModifications must produce log records");
        check(!SystemToolsBlockerService.IsAnyToolBlocked(), "After revert, system tools must not be blocked");
    }

    private static void TestTerminateBackgroundProcessesSafety(Action<bool, string> check)
    {
        var currentPid = Process.GetCurrentProcess().Id;
        var terminated = CleanWipeService.TerminateBackgroundProcesses(excludePid: currentPid);
        check(terminated >= 0, "TerminateBackgroundProcesses should return non-negative count");
        check(!Process.GetCurrentProcess().HasExited, "Current process must not be terminated");
    }

    private static void TestRecoveryServiceReset(Action<bool, string> check, string root)
    {
        var journalPath = Path.Combine(root, "recovery_reset_test.json");
        var store = new RecoveryJournalStore(journalPath);
        var mockWindowApi = new DummyRecoveryWindowApi();
        var recoveryService = new RecoveryService(store, mockWindowApi);

        // Prepare dummy record
        recoveryService.TryPrepare(new IntPtr(1234), "TestApp", out var record);
        check(record != null, "Candidate record prepared");
        check(recoveryService.Records.Count > 0, "Records collection has items before reset");

        // Call Reset
        recoveryService.Reset();
        check(recoveryService.Records.Count == 0, "RecoveryService.Reset must clear all records in memory");
        check(recoveryService.PendingCount == 0, "PendingCount must be 0 after reset");
    }

    private sealed class DummyRecoveryWindowApi : IRecoveryWindowApi
    {
        public bool TryCapture(IntPtr window, out RecoverySnapshot snapshot, out string error)
        {
            snapshot = new RecoverySnapshot
            {
                Identity = new RecoveryIdentity(window.ToInt64(), 100, DateTime.UtcNow.Ticks, 1, "Test", "Test.exe"),
                WindowTitle = "Test Window"
            };
            error = "";
            return true;
        }

        public bool TryTag(RecoveryRecord record, out string error) { error = ""; return true; }
        public void RemoveTag(RecoveryRecord record) { }
        public RecoveryCheck Validate(RecoveryRecord record) => new(RecoveryValidation.Valid);
        public bool TryRestore(RecoveryRecord record, out string error) { error = ""; return true; }
    }
}
