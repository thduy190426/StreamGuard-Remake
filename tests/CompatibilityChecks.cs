using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;
using StreamGuard.Helpers;
using StreamGuard.Services;

namespace StreamGuard.Tests
{
    internal static class CompatibilityChecks
    {
        internal static void Run(Action<bool, string> check)
        {
            TestSingleInstanceFallback(check);
            TestNetshRuleFormatting(check);
            TestAdaptivePolling(check);
            TestVirtualDesktopCOMResilience(check);
        }

        private static void TestSingleInstanceFallback(Action<bool, string> check)
        {
            // 1. Normal single instance acquisition (Global)
            var globalAcquired = SingleInstanceHelper.AcquireSingleInstance(name =>
            {
                check(name == SingleInstanceHelper.GlobalMutexName, "Acquire attempts GlobalMutexName first");
                return (null, true);
            });
            check(!globalAcquired.IsFallback, "Normal acquisition is not fallback");
            check(globalAcquired.MutexName == SingleInstanceHelper.GlobalMutexName, "Normal acquisition uses GlobalMutexName");
            check(globalAcquired.ShowEventName == SingleInstanceHelper.GlobalShowEventName, "Normal acquisition uses GlobalShowEventName");
            check(globalAcquired.CreatedNew, "CreatedNew is preserved");

            // 2. Fallback when Global throws UnauthorizedAccessException (e.g. non-admin, restricted privilege)
            var attempted = new System.Collections.Generic.List<string>();
            var fallbackAcquired = SingleInstanceHelper.AcquireSingleInstance(name =>
            {
                attempted.Add(name);
                if (name == SingleInstanceHelper.GlobalMutexName)
                    throw new UnauthorizedAccessException("SeCreateGlobalPrivilege denied");
                return (null, true);
            });
            check(fallbackAcquired.IsFallback, "Fallback acquisition marks IsFallback true");
            check(fallbackAcquired.MutexName == SingleInstanceHelper.LocalMutexName, "Fallback acquisition uses LocalMutexName");
            check(fallbackAcquired.ShowEventName == SingleInstanceHelper.LocalShowEventName, "Fallback acquisition uses LocalShowEventName");
            check(attempted.Count == 2 && attempted[0] == SingleInstanceHelper.GlobalMutexName && attempted[1] == SingleInstanceHelper.LocalMutexName,
                "Acquisition tries GlobalMutexName then LocalMutexName on access denial");

            // 3. ShowExistingEvent fallback on UnauthorizedAccessException
            var eventCreated = SingleInstanceHelper.CreateShowExistingEvent(
                SingleInstanceHelper.GlobalShowEventName,
                name =>
                {
                    if (name == SingleInstanceHelper.GlobalShowEventName)
                        throw new UnauthorizedAccessException("Access denied to global event");
                    return new EventWaitHandle(false, EventResetMode.AutoReset, name);
                });
            using (eventCreated)
            {
                check(eventCreated != null, "Fallback show existing event is successfully created");
            }

            // 4. ShowExistingEvent handles null/empty safely
            var defaultEvent = SingleInstanceHelper.CreateShowExistingEvent(
                null,
                name => new EventWaitHandle(false, EventResetMode.AutoReset, name));
            using (defaultEvent)
            {
                check(defaultEvent != null, "CreateShowExistingEvent handles null safely");
            }

            // 5. SignalExistingInstance fallback
            var signaledTargets = new System.Collections.Generic.List<string>();
            var signaled = SingleInstanceHelper.SignalExistingInstance(
                SingleInstanceHelper.GlobalShowEventName,
                target =>
                {
                    signaledTargets.Add(target);
                    if (target == SingleInstanceHelper.GlobalShowEventName)
                        throw new UnauthorizedAccessException("Access denied to global event");
                });
            check(signaled, "SignalExistingInstance succeeds via fallback");
            check(signaledTargets.Contains(SingleInstanceHelper.LocalShowEventName),
                "SignalExistingInstance falls back to LocalShowEventName when global is restricted");

            // 6. SignalExistingInstance returns false safely when all signal attempts fail
            var allFailed = SingleInstanceHelper.SignalExistingInstance(
                SingleInstanceHelper.GlobalShowEventName,
                _ => throw new InvalidOperationException("Simulated failure"));
            check(!allFailed, "SignalExistingInstance returns false safely when all targets fail");
        }

        private static void TestNetshRuleFormatting(Action<bool, string> check)
        {
            // Program rule formatting (Outbound)
            var outRule = NetshFirewallHelper.FormatAddProgramRuleArgs(
                "StreamGuard Block RAIA Out",
                @"C:\Program Files\TestApp\app.exe",
                "Outbound");
            check(outRule.Contains("advfirewall firewall add rule"), "Netsh program rule contains add rule command");
            check(outRule.Contains("name=\"StreamGuard Block RAIA Out\""), "Netsh program rule contains rule name");
            check(outRule.Contains("dir=out"), "Netsh outbound program rule has dir=out");
            check(outRule.Contains("action=block"), "Netsh program rule has action=block");
            check(outRule.Contains("program=\"C:\\Program Files\\TestApp\\app.exe\""), "Netsh program rule has program path quoted");
            check(outRule.Contains("profile=any"), "Netsh program rule has profile=any");
            check(outRule.Contains("enable=yes"), "Netsh program rule has enable=yes");

            // Program rule formatting (Inbound)
            var inRule = NetshFirewallHelper.FormatAddProgramRuleArgs(
                "StreamGuard Block RAIA In",
                @"C:\Program Files\TestApp\app.exe",
                "Inbound");
            check(inRule.Contains("dir=in"), "Netsh inbound program rule has dir=in");

            // Edge cases: short "in", "OUT", and null direction
            var inShort = NetshFirewallHelper.FormatAddProgramRuleArgs("RuleIn", "app.exe", "in");
            check(inShort.Contains("dir=in"), "Netsh accepts short 'in'");
            var outUpper = NetshFirewallHelper.FormatAddProgramRuleArgs("RuleOut", "app.exe", "OUT");
            check(outUpper.Contains("dir=out"), "Netsh accepts uppercase 'OUT'");
            var defaultDir = NetshFirewallHelper.FormatAddProgramRuleArgs("RuleDef", "app.exe", "");
            check(defaultDir.Contains("dir=out"), "Netsh defaults empty direction to 'out'");

            // Remote IP rule formatting
            var ipRule = NetshFirewallHelper.FormatAddRemoteIpRuleArgs(
                "StreamGuard Block Remote 123",
                "203.0.113.50",
                "Outbound");
            check(ipRule.Contains("advfirewall firewall add rule"), "Netsh remote ip rule contains add rule command");
            check(ipRule.Contains("name=\"StreamGuard Block Remote 123\""), "Netsh remote ip rule has rule name");
            check(ipRule.Contains("remoteip=\"203.0.113.50\""), "Netsh remote ip rule has remoteip quoted");
            check(ipRule.Contains("dir=out"), "Netsh remote ip rule has dir=out");
            check(ipRule.Contains("action=block"), "Netsh remote ip rule has action=block");

            // Delete rule by name formatting
            var delRule = NetshFirewallHelper.FormatDeleteRuleByNameArgs("StreamGuard Block Remote 123");
            check(delRule == "advfirewall firewall delete rule name=\"StreamGuard Block Remote 123\"",
                "Netsh delete rule formats exact syntax");

            // Delete rules by program formatting
            var delProg = NetshFirewallHelper.FormatDeleteRulesByProgramArgs(@"C:\Program Files\TestApp\app.exe");
            check(delProg == "advfirewall firewall delete rule name=all program=\"C:\\Program Files\\TestApp\\app.exe\"",
                "Netsh delete program rules formats exact syntax");
        }

        private static void TestAdaptivePolling(Action<bool, string> check)
        {
            // Verify intervals
            check(CaptureRiskService.ActivePollingIntervalSeconds == 2, "CaptureRiskService active polling interval is 2s");
            check(CaptureRiskService.IdlePollingIntervalSeconds == 5, "CaptureRiskService idle polling interval is 5s");
            check(ThreatBlockerService.ActiveScanIntervalMs == 2000, "ThreatBlockerService active scan interval is 2000ms");
            check(ThreatBlockerService.IdleScanIntervalMs == 5000, "ThreatBlockerService idle scan interval is 5000ms");

            // DetermineAdaptiveInterval logic
            check(CaptureRiskService.DetermineAdaptiveInterval(isWindowMinimized: false, hasRisk: true) == TimeSpan.FromSeconds(2),
                "Active risk forces 2s polling even if window open");
            check(CaptureRiskService.DetermineAdaptiveInterval(isWindowMinimized: true, hasRisk: true) == TimeSpan.FromSeconds(2),
                "Active risk forces 2s polling even when minimized to tray");
            check(CaptureRiskService.DetermineAdaptiveInterval(isWindowMinimized: true, hasRisk: false) == TimeSpan.FromSeconds(5),
                "No risk while minimized to tray stretches polling to 5s for CPU & battery saving");
            check(CaptureRiskService.DetermineAdaptiveInterval(isWindowMinimized: false, hasRisk: false) == TimeSpan.FromSeconds(2),
                "Window restored/open maintains 2s responsive polling even with no active risk");

            // ThreatBlockerService adaptive polling intervals
            using var threatBlocker = new ThreatBlockerService();
            threatBlocker.SetWindowMinimized(true);
            check(threatBlocker.CurrentScanIntervalMs == 5000, "ThreatBlockerService minimized without threats stretches interval to 5000ms");
            threatBlocker.RestoreActivePolling();
            check(threatBlocker.CurrentScanIntervalMs == 2000, "ThreatBlockerService restore active polling restores interval to 2000ms");
        }

        private static void TestVirtualDesktopCOMResilience(Action<bool, string> check)
        {
            using var source = new HwndSource(new HwndSourceParameters("COM resilience test")
            { Width = 16, Height = 16, PositionX = -32000, PositionY = -32000, WindowStyle = unchecked((int)0x80000000) });

            var throwingShell = new ThrowingShell();
            var manager = new DummyManager();
            using var service = new VirtualDesktopService(manager, throwingShell);

            check(service.IsAvailable, "Initially marked available with non-null interfaces");

            // 1. MoveToHidden should not throw when COM throws, return false, and mark IsAvailable = false
            var moveOk = service.MoveToHidden(source.Handle, out var origId);
            check(!moveOk, "MoveToHidden returns false when COM interface throws");
            check(!service.IsAvailable, "VirtualDesktopService sets IsAvailable to false on COM exception to fallback to WDA/Hide");

            // 2. Subsequent operations gracefully return false / empty
            check(!service.SwitchToHiddenDesktop(), "SwitchToHiddenDesktop returns false safely");
            check(!service.SwitchToInitialDesktop(), "SwitchToInitialDesktop returns false safely");
            check(!service.ToggleHiddenDesktop(), "ToggleHiddenDesktop returns false safely");
            check(service.GetCurrentDesktopId() == Guid.Empty, "GetCurrentDesktopId returns Guid.Empty safely");

            // 3. RemoveHiddenDesktop resilience
            service.RemoveHiddenDesktop();
            check(!service.IsAvailable, "RemoveHiddenDesktop executes safely under COM errors");
        }

        private sealed class ThrowingShell : IVirtualDesktopManagerInternal
        {
            public int GetCount() => throw new COMException("Simulated COM vtable error 0x80004002 (E_NOINTERFACE)");
            public void MoveViewToDesktop(IntPtr view, IVirtualDesktop desktop) => throw new COMException();
            public bool CanViewMoveDesktops(IntPtr view) => throw new COMException();
            public IVirtualDesktop GetCurrentDesktop() => throw new COMException("Vtable layout mismatch on Windows 11 build");
            public void GetDesktops(out IObjectArray array) => throw new COMException("Vtable layout mismatch on Windows 11 build");
            public IVirtualDesktop GetAdjacentDesktop(IVirtualDesktop reference, int direction) => throw new COMException();
            public void SwitchDesktop(IVirtualDesktop desktop) => throw new COMException("Vtable layout mismatch on Windows 11 build");
            public IVirtualDesktop CreateDesktop() => throw new COMException("Vtable layout mismatch on Windows 11 build");
            public void RemoveDesktop(IVirtualDesktop remove, IVirtualDesktop fallback) => throw new COMException();
            public IVirtualDesktop FindDesktop(ref Guid id) => throw new COMException();
        }

        private sealed class DummyManager : IVirtualDesktopManager
        {
            public bool IsWindowOnCurrentVirtualDesktop(IntPtr window) => true;
            public Guid GetWindowDesktopId(IntPtr window) => Guid.NewGuid();
            public void MoveWindowToDesktop(IntPtr window, ref Guid desktop) { }
        }
    }
}
