using System.Diagnostics;

namespace StreamGuard.Services;

public enum CaptureToolKind { Recorder, Screenshot, Overlay, Meeting, RemoteAccess, Browser, Utility, Sniffer }
public sealed record CaptureTool(string Id, string Name, CaptureToolKind Kind, params string[] ProcessNames)
{
    public bool DedicatedCaptureTool => Kind is CaptureToolKind.Recorder or CaptureToolKind.Screenshot;
}
public sealed record CaptureAssessment(bool ScanSucceeded, IReadOnlyList<CaptureTool> Tools)
{
    public static CaptureAssessment Unknown { get; } = new(false, Array.Empty<CaptureTool>());
}

/// <summary>Process presence is a risk indicator, never evidence of an active recording.</summary>
public sealed class CaptureRiskService
{
    public static IReadOnlyList<CaptureTool> Catalog { get; } = Array.AsReadOnly(new[]
    {
        new CaptureTool("obs", "OBS Studio", CaptureToolKind.Recorder, "obs64", "obs32", "obs"),
        new CaptureTool("streamlabs", "Streamlabs Desktop", CaptureToolKind.Recorder, "Streamlabs Desktop", "Streamlabs OBS"),
        new CaptureTool("xsplit", "XSplit Broadcaster", CaptureToolKind.Recorder, "XSplit.Core", "XSplitBroadcaster"),
        new CaptureTool("prism", "PRISM Live Studio", CaptureToolKind.Recorder, "PRISMLiveStudio"),
        new CaptureTool("bandicam", "Bandicam", CaptureToolKind.Recorder, "bdcam", "bdcam64"),
        new CaptureTool("camtasia", "Camtasia Recorder", CaptureToolKind.Recorder, "CamRecorder", "CamtasiaRecorder"),
        new CaptureTool("snagit", "Snagit Capture", CaptureToolKind.Screenshot, "SnagitCapture", "Snagit32", "Snagit64"),
        new CaptureTool("sharex", "ShareX", CaptureToolKind.Screenshot, "ShareX"),
        new CaptureTool("screentogif", "ScreenToGif", CaptureToolKind.Recorder, "ScreenToGif"),
        new CaptureTool("faststone", "FastStone Capture", CaptureToolKind.Screenshot, "FSCapture"),
        new CaptureTool("snipping", "Snipping Tool", CaptureToolKind.Screenshot, "SnippingTool", "ScreenSketch"),
        new CaptureTool("gamebar", "Xbox Game Bar", CaptureToolKind.Overlay, "GameBar", "GameBarFTServer", "GameBarPresenceWriter"),
        new CaptureTool("steps", "Steps Recorder", CaptureToolKind.Screenshot, "psr"),
        new CaptureTool("loom", "Loom", CaptureToolKind.Recorder, "Loom", "LoomRecorder"),
        new CaptureTool("fraps", "Fraps", CaptureToolKind.Recorder, "fraps"),
        new CaptureTool("medal", "Medal", CaptureToolKind.Overlay, "Medal", "MedalEncoder"),
        new CaptureTool("nvidia", "NVIDIA recording overlay", CaptureToolKind.Overlay, "NVIDIA Share", "NVIDIA Overlay"),
        new CaptureTool("amd", "AMD Record & Stream", CaptureToolKind.Overlay, "RadeonSoftware", "AMDRSServ", "AMDRSSrcExt"),
        new CaptureTool("discord", "Discord", CaptureToolKind.Meeting, "Discord", "DiscordCanary", "DiscordPTB"),
        new CaptureTool("teams", "Microsoft Teams", CaptureToolKind.Meeting, "Teams", "ms-teams"),
        new CaptureTool("zoom", "Zoom", CaptureToolKind.Meeting, "Zoom"),
        new CaptureTool("webex", "Webex", CaptureToolKind.Meeting, "CiscoCollabHost", "WebexHost"),
        new CaptureTool("anydesk", "AnyDesk", CaptureToolKind.RemoteAccess, "AnyDesk"),
        new CaptureTool("teamviewer", "TeamViewer", CaptureToolKind.RemoteAccess, "TeamViewer", "TeamViewer_Service"),
        new CaptureTool("rustdesk", "RustDesk", CaptureToolKind.RemoteAccess, "rustdesk"),
        new CaptureTool("ultraviewer", "UltraViewer", CaptureToolKind.RemoteAccess, "UltraViewer", "UltraViewer_Desktop"),
        new CaptureTool("rdp", "Remote Desktop client", CaptureToolKind.RemoteAccess, "mstsc", "msrdc"),
        new CaptureTool("chrome", "Google Chrome", CaptureToolKind.Browser, "chrome"),
        new CaptureTool("edge", "Microsoft Edge", CaptureToolKind.Browser, "msedge"),
        new CaptureTool("firefox", "Firefox", CaptureToolKind.Browser, "firefox"),
        new CaptureTool("brave", "Brave", CaptureToolKind.Browser, "brave"),
        new CaptureTool("opera", "Opera", CaptureToolKind.Browser, "opera"),
        new CaptureTool("ffmpeg", "FFmpeg (multi-purpose)", CaptureToolKind.Utility, "ffmpeg"),
        new CaptureTool("wireshark", "Wireshark", CaptureToolKind.Sniffer, "wireshark", "tshark", "dumpcap"),
        new CaptureTool("fiddler", "Fiddler Proxy", CaptureToolKind.Sniffer, "fiddler", "FiddlerViewer", "FiddlerCore"),
        new CaptureTool("charles", "Charles Proxy", CaptureToolKind.Sniffer, "charles"),
        new CaptureTool("mitmproxy", "mitmproxy", CaptureToolKind.Sniffer, "mitmproxy", "mitmdump", "mitmweb"),
        new CaptureTool("httpdebugger", "HTTP Debugger Pro", CaptureToolKind.Sniffer, "HTTPDebuggerPro", "HTTPDebuggerUI", "HTTPDebuggerService"),
        new CaptureTool("proxifier", "Proxifier", CaptureToolKind.Sniffer, "Proxifier"),
        new CaptureTool("burpsuite", "Burp Suite", CaptureToolKind.Sniffer, "burpsuite"),
        new CaptureTool("reqable", "Reqable Proxy", CaptureToolKind.Sniffer, "reqable", "reqable-cli"),
        new CaptureTool("proxyman", "Proxyman", CaptureToolKind.Sniffer, "proxyman"),
        new CaptureTool("whistle", "Whistle Proxy", CaptureToolKind.Sniffer, "whistle"),
        new CaptureTool("networkminer", "NetworkMiner", CaptureToolKind.Sniffer, "NetworkMiner"),
        new CaptureTool("smartsniff", "SmartSniff", CaptureToolKind.Sniffer, "smsniff")
    });

    public static CaptureAssessment Assess(IEnumerable<string> processNames, bool scanSucceeded = true)
    {
        var names = processNames.Select(name => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new CaptureAssessment(scanSucceeded, Catalog.Where(tool => tool.ProcessNames.Any(names.Contains)).ToArray());
    }

    public CaptureAssessment Scan()
    {
        var names = new List<string>();
        var complete = true;
        Process[] processes;
        try { processes = Process.GetProcesses(); }
        catch { return CaptureAssessment.Unknown; }
        foreach (var process in processes)
        {
            using (process)
            {
                try { names.Add(process.ProcessName); }
                catch (InvalidOperationException) { } // A process exiting during enumeration is expected.
                catch (System.ComponentModel.Win32Exception) { } // Protected system/PPL processes cannot be queried.
                catch (UnauthorizedAccessException) { }
                catch { complete = false; }
            }
        }
        return Assess(names, complete);
    }

    public const int ActivePollingIntervalSeconds = 2;
    public const int IdlePollingIntervalSeconds = 5;
    public static TimeSpan ActiveInterval => TimeSpan.FromSeconds(ActivePollingIntervalSeconds);
    public static TimeSpan IdleInterval => TimeSpan.FromSeconds(IdlePollingIntervalSeconds);

    public static TimeSpan DetermineAdaptiveInterval(bool isWindowMinimized, bool hasRisk)
    {
        if (hasRisk || !isWindowMinimized) return ActiveInterval;
        return IdleInterval;
    }

    public static bool ShouldCover(bool enabled, bool protecting, bool paused, bool includeMultiPurpose, CaptureAssessment assessment, bool recoveryAvailable = true) =>
        enabled && protecting && !paused && recoveryAvailable && (!assessment.ScanSucceeded || assessment.Tools.Any(tool => tool.DedicatedCaptureTool || includeMultiPurpose));
}
