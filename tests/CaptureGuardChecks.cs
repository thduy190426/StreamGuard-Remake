using StreamGuard.Services;
using System.IO;
using System.Windows.Interop;

internal static class CaptureGuardChecks
{
    public static void Run(Action<bool, string> check, string artifactDirectory)
    {
        var catalog = CaptureRiskService.Catalog;
        check(catalog.Select(tool => tool.Id).Distinct().Count() == catalog.Count, "Capture tool IDs are unique");
        foreach (var tool in catalog)
        {
            foreach (var alias in tool.ProcessNames)
            {
                var assessment = CaptureRiskService.Assess(new[] { alias.ToUpperInvariant() + ".exe", alias, alias });
                check(assessment.Tools.Count == 1 && assessment.Tools[0].Id == tool.Id, "Capture alias exact, case-insensitive and deduplicated: " + alias);
            }
        }
        var none = CaptureRiskService.Assess(new[] { "notobs64", "obs64-helper", "nvcontainer", "notepad", "chrome-helper" });
        check(none.Tools.Count == 0, "Similar process names and generic driver services do not trigger recognition");
        var recorder = CaptureRiskService.Assess(new[] { "obs64" });
        var browser = CaptureRiskService.Assess(new[] { "chrome" });
        var overlay = CaptureRiskService.Assess(new[] { "GameBar" });
        var sharing = CaptureRiskService.Assess(new[] { "ms-teams", "AnyDesk", "NVIDIA Overlay", "ffmpeg" });
        check(!CaptureRiskService.ShouldCover(true, true, false, true, recorder, false), "Curtain requires an available recovery control");
        foreach (var enabled in new[] { false, true })
        foreach (var protecting in new[] { false, true })
        foreach (var paused in new[] { false, true })
        foreach (var multiPurpose in new[] { false, true })
        {
            var active = enabled && protecting && !paused;
            check(CaptureRiskService.ShouldCover(enabled, protecting, paused, multiPurpose, recorder) == active, "Dedicated-recorder curtain policy");
            check(CaptureRiskService.ShouldCover(enabled, protecting, paused, multiPurpose, browser) == (active && multiPurpose), "A browser alone requires explicit multi-purpose consent");
            check(CaptureRiskService.ShouldCover(enabled, protecting, paused, multiPurpose, sharing) == (active && multiPurpose), "Meeting/remote/GPU tools require explicit multi-purpose consent");
            check(CaptureRiskService.ShouldCover(enabled, protecting, paused, multiPurpose, overlay) == (active && multiPurpose), "Game Bar presence alone is not recording evidence");
            check(!CaptureRiskService.ShouldCover(enabled, protecting, paused, multiPurpose, none), "No recognized process is not a curtain trigger");
            check(CaptureRiskService.ShouldCover(enabled, protecting, paused, multiPurpose, CaptureAssessment.Unknown) == active, "An incomplete scan does not silently remove the enabled curtain");
        }

        var path = Path.Combine(artifactDirectory, "capture-config.json");
        var config = new ConfigService(path);
        config.Load();
        check(config.CaptureGuardEnabled && !config.CaptureGuardIncludeMultiPurpose, "New configurations enable the recorder guard without multi-purpose triggers");
        check(new StreamGuardConfig().CaptureGuardEnabled, "Serialized configuration defaults enable the recorder guard");
        File.WriteAllText(path, "{\"Language\":\"vi\",\"Apps\":[]}");
        var legacyObject = new ConfigService(path); legacyObject.Load();
        check(legacyObject.CaptureGuardEnabled && !legacyObject.CaptureGuardIncludeMultiPurpose, "A legacy object without capture preferences enables the recorder guard");
        File.WriteAllText(path, "[]");
        var legacyArray = new ConfigService(path); legacyArray.Load();
        check(legacyArray.CaptureGuardEnabled && !legacyArray.CaptureGuardIncludeMultiPurpose, "A legacy app array enables the recorder guard");
        check(config.Save(Array.Empty<StreamGuard.Models.GuardedApp>(), captureGuardEnabled: false), "The recorder guard can still be explicitly disabled");
        var disabled = new ConfigService(path); disabled.Load();
        check(!disabled.CaptureGuardEnabled, "An explicit disabled preference survives reload");
        check(config.Save(Array.Empty<StreamGuard.Models.GuardedApp>(), language: "vi", captureGuardEnabled: true, captureGuardIncludeMultiPurpose: true), "Capture guard settings save");
        var read = new ConfigService(path); read.Load();
        check(read.CaptureGuardEnabled && read.CaptureGuardIncludeMultiPurpose && read.Language == "vi", "Capture preferences and language round-trip");
        read.Save(Array.Empty<StreamGuard.Models.GuardedApp>());
        var again = new ConfigService(path); again.Load();
        check(again.CaptureGuardEnabled && again.CaptureGuardIncludeMultiPurpose, "Existing config saves retain new capture settings");

        var fake = new AffinityApi();
        var windows = new WindowCaptureProtectionService(fake, true);
        check(windows.ApplyAndVerify(IntPtr.Zero).Mode == CaptureAffinityMode.Unverified && fake.Sets == 0, "No HWND is never reported as protected");
        check(windows.ApplyAndVerify(new IntPtr(1)).Mode == CaptureAffinityMode.Excluded && fake.Value == 0x11, "Set and read-back verify capture exclusion");
        windows.ApplyAndVerify(new IntPtr(1));
        check(fake.Sets == 1, "Already correct affinity avoids repeated native writes");
        fake.Value = 0; windows.ApplyAndVerify(new IntPtr(1));
        check(fake.Value == 0x11 && fake.Sets == 2, "Affinity drift is reapplied");
        fake = new AffinityApi { SetSucceeds = false };
        check(new WindowCaptureProtectionService(fake, true).ApplyAndVerify(new IntPtr(1)) is { Mode: CaptureAffinityMode.Unverified, ErrorCode: 5 }, "Native failure remains unverified");
        fake = new AffinityApi { SetChangesValue = false };
        check(new WindowCaptureProtectionService(fake, true).ApplyAndVerify(new IntPtr(1)).Mode == CaptureAffinityMode.Unverified, "Set success without read-back is insufficient");
        fake = new AffinityApi { GetSucceeds = false };
        check(new WindowCaptureProtectionService(fake, true).ApplyAndVerify(new IntPtr(1)).Mode == CaptureAffinityMode.Unverified, "Read failure cannot produce a green badge");
        fake = new AffinityApi();
        check(new WindowCaptureProtectionService(fake, false).ApplyAndVerify(new IntPtr(1)).Mode == CaptureAffinityMode.MonitorOnly && fake.Value == 1, "Legacy protection is labeled limited");

        // Only create an invisible test-owned top-level window; never capture or change another app.
        using var source = new HwndSource(new HwndSourceParameters("StreamGuard affinity verification")
        {
            Width = 16, Height = 16, PositionX = -32000, PositionY = -32000, WindowStyle = unchecked((int)0x80000000)
        });
        var nativeResult = new WindowCaptureProtectionService().ApplyAndVerify(source.Handle);
        File.WriteAllText(Path.Combine(artifactDirectory, "native-affinity-result.txt"), nativeResult.ToString());
        Console.WriteLine("Native affinity check on invisible test-owned HWND: " + nativeResult);
        check(nativeResult.Mode == CaptureAffinityMode.Excluded, "Windows confirms exclusion for the test-owned top-level window");

        // Windows background ScreenClippingHost must not trigger a black curtain on launch
        var bgHost = CaptureRiskService.Assess(new[] { "ScreenClippingHost" });
        check(bgHost.Tools.Count == 0, "Windows background ScreenClippingHost does not falsely trigger automatic black curtain");

        // PrivacyCurtainService includes a visible dismiss button & raises DismissRequested
        var curtainSvc = new PrivacyCurtainService();
        int dismissCalls = 0;
        curtainSvc.DismissRequested += () => dismissCalls++;
        var buildContent = typeof(PrivacyCurtainService).GetMethod("BuildContent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var contentGrid = (System.Windows.Controls.Grid)buildContent.Invoke(curtainSvc, null)!;
        var cardBorder = contentGrid.Children.OfType<System.Windows.Controls.Border>().FirstOrDefault();
        var dismissBtn = (cardBorder?.Child as System.Windows.Controls.StackPanel)?.Children.OfType<System.Windows.Controls.Button>().FirstOrDefault();
        check(dismissBtn != null, "PrivacyCurtainService renders an on-screen dismiss button so screen is never an inescapable black screen");
        var reqDismiss = typeof(PrivacyCurtainService).GetMethod("RequestDismiss", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        reqDismiss.Invoke(curtainSvc, null);
        check(dismissCalls == 1, "PrivacyCurtainService dismiss button raises DismissRequested");
    }

    private sealed class AffinityApi : IWindowCaptureApi
    {
        public uint Value;
        public int Sets;
        public bool SetSucceeds = true;
        public bool GetSucceeds = true;
        public bool SetChangesValue = true;
        public bool TryGet(IntPtr window, out uint affinity, out int error)
        {
            affinity = Value; error = GetSucceeds ? 0 : 5; return GetSucceeds;
        }
        public bool TrySet(IntPtr window, uint affinity, out int error)
        {
            Sets++; if (SetSucceeds && SetChangesValue) Value = affinity;
            error = SetSucceeds ? 0 : 5; return SetSucceeds;
        }
    }
}
