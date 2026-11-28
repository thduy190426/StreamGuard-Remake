using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using StreamGuard.Localization;

namespace StreamGuard.Services;

public enum ReadinessTier
{
    Optimal,       // 85-100 pts, no fatal bottlenecks
    Good,          // 70-84 pts
    Warning,       // 50-69 pts
    Insufficient   // < 50 pts
}

public sealed record CpuDiagnostics(
    string Name,
    int Cores,
    int LogicalProcessors,
    string Architecture,
    bool HasAvx,
    bool HasAvx2
);

public sealed record RamDiagnostics(
    ulong TotalBytes,
    ulong AvailableBytes,
    uint MemoryLoadPercentage
)
{
    public double TotalGigabytes => TotalBytes / (1024.0 * 1024 * 1024);
    public double AvailableGigabytes => AvailableBytes / (1024.0 * 1024 * 1024);
    public double UsedGigabytes => TotalGigabytes - AvailableGigabytes;
    public double UsedPercentage => TotalBytes > 0 ? (100.0 - (AvailableBytes * 100.0 / TotalBytes)) : 0;
}

public sealed record GpuDiagnostics(
    string Name,
    ulong VramBytes,
    string DriverVersion
)
{
    public double VramGigabytes => VramBytes / (1024.0 * 1024 * 1024);
}

public sealed record OsDiagnostics(
    string ProductName,
    string DisplayVersion,
    int BuildNumber,
    bool Is64Bit,
    bool IsAdministrator
);

public sealed record StorageDiagnostics(
    string DriveName,
    ulong TotalBytes,
    ulong FreeBytes
)
{
    public double TotalGigabytes => TotalBytes / (1024.0 * 1024 * 1024);
    public double FreeGigabytes => FreeBytes / (1024.0 * 1024 * 1024);
    public double FreePercentage => TotalBytes > 0 ? (FreeBytes * 100.0 / TotalBytes) : 0;
}

public sealed record AudioDiagnostics(
    bool HasInputDevice,
    bool HasOutputDevice,
    int InputDeviceCount,
    int OutputDeviceCount
);

public sealed record FeatureReadinessItem(
    string FeatureKey,
    string FeatureName,
    ReadinessTier Tier,
    string StatusSummary,
    string Detail
);

public sealed record HardwareDiagnosticsResult(
    DateTime Timestamp,
    CpuDiagnostics Cpu,
    RamDiagnostics Ram,
    IReadOnlyList<GpuDiagnostics> Gpus,
    OsDiagnostics Os,
    StorageDiagnostics Storage,
    AudioDiagnostics Audio,
    string? WebView2Version,
    int CompatibilityScore,
    ReadinessTier OverallTier,
    IReadOnlyList<FeatureReadinessItem> FeatureMatrix,
    IReadOnlyList<string> Recommendations
);

public sealed class HardwareDiagnosticsService
{
    private static readonly Lazy<HardwareDiagnosticsService> _instance =
        new(() => new HardwareDiagnosticsService());
    public static HardwareDiagnosticsService Instance => _instance.Value;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public static MEMORYSTATUSEX Create()
        {
            var result = new MEMORYSTATUSEX();
            result.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
            return result;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern int waveInGetNumDevs();

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern int waveOutGetNumDevs();

    public Task<HardwareDiagnosticsResult> CollectHardwareInfoAsync()
    {
        return Task.Run(CollectHardwareInfo);
    }

    public HardwareDiagnosticsResult CollectHardwareInfo()
    {
        var timestamp = DateTime.Now;
        var cpu = CollectCpu();
        var ram = CollectRam();
        var gpus = CollectGpus();
        var os = CollectOs();
        var storage = CollectStorage();
        var audio = CollectAudio();
        var webView2Version = CollectWebView2Version();

        bool vdAvailable = false;
        try
        {
            using var vdService = new VirtualDesktopService();
            vdAvailable = vdService.IsAvailable;
        }
        catch { }

        return EvaluateReadiness(
            timestamp,
            cpu,
            ram,
            gpus,
            os,
            storage,
            audio,
            webView2Version,
            vdAvailable);
    }

    private CpuDiagnostics CollectCpu()
    {
        string name = "Unknown Processor";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (key?.GetValue("ProcessorNameString") is string regName && !string.IsNullOrWhiteSpace(regName))
            {
                name = Regex.Replace(regName.Trim(), @"\s+", " ");
            }
        }
        catch { }

        if (name == "Unknown Processor")
        {
            var envId = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
            if (!string.IsNullOrWhiteSpace(envId)) name = envId.Trim();
        }

        int logicalProcessors = Environment.ProcessorCount;
        int cores = 0;
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT NumberOfCores FROM Win32_Processor");
            foreach (ManagementObject mo in searcher.Get())
            {
                if (mo["NumberOfCores"] != null)
                {
                    try { cores += Convert.ToInt32(mo["NumberOfCores"]); } catch { }
                }
            }
        }
        catch { }
        if (cores <= 0) cores = logicalProcessors;

        string arch = RuntimeInformation.ProcessArchitecture.ToString();

        bool hasAvx = false;
        bool hasAvx2 = false;
        try
        {
            if (RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.X86)
            {
                hasAvx = Avx.IsSupported;
                hasAvx2 = Avx2.IsSupported;
            }
        }
        catch { }

        return new CpuDiagnostics(name, cores, logicalProcessors, arch, hasAvx, hasAvx2);
    }

    private RamDiagnostics CollectRam()
    {
        try
        {
            var mem = MEMORYSTATUSEX.Create();
            if (GlobalMemoryStatusEx(ref mem))
            {
                return new RamDiagnostics(mem.ullTotalPhys, mem.ullAvailPhys, mem.dwMemoryLoad);
            }
        }
        catch { }

        // Fallback: estimation
        return new RamDiagnostics(8UL * 1024 * 1024 * 1024, 4UL * 1024 * 1024 * 1024, 50);
    }

    private IReadOnlyList<GpuDiagnostics> CollectGpus()
    {
        var list = new List<GpuDiagnostics>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM, DriverVersion FROM Win32_VideoController");
            foreach (ManagementObject mo in searcher.Get())
            {
                var name = mo["Name"]?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;

                ulong vram = 0;
                if (mo["AdapterRAM"] != null)
                {
                    try { vram = Convert.ToUInt64(mo["AdapterRAM"]); } catch { }
                }

                var driver = mo["DriverVersion"]?.ToString()?.Trim() ?? "Unknown";
                list.Add(new GpuDiagnostics(name, vram, driver));
            }
        }
        catch { }

        // Fallback or enrich with Registry
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (classKey != null)
            {
                var regGpus = new List<GpuDiagnostics>();
                foreach (var subKeyName in classKey.GetSubKeyNames())
                {
                    if (subKeyName.Length == 4 && int.TryParse(subKeyName, out _))
                    {
                        using var subKey = classKey.OpenSubKey(subKeyName);
                        if (subKey == null) continue;
                        var name = (subKey.GetValue("DriverDesc") as string ?? subKey.GetValue("HardwareInformation.AdapterString") as string)?.Trim();
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        var driver = (subKey.GetValue("DriverVersion") as string)?.Trim() ?? "Unknown";
                        ulong vram = 0;
                        var qwMem = subKey.GetValue("HardwareInformation.qwMemorySize");
                        if (qwMem != null)
                        {
                            try { vram = Convert.ToUInt64(qwMem); } catch { }
                        }
                        if (vram == 0)
                        {
                            var dMem = subKey.GetValue("HardwareInformation.MemorySize");
                            if (dMem is byte[] bytes && bytes.Length >= 4)
                                vram = BitConverter.ToUInt32(bytes, 0);
                            else if (dMem != null)
                                try { vram = Convert.ToUInt64(dMem); } catch { }
                        }

                        regGpus.Add(new GpuDiagnostics(name, vram, driver));
                    }
                }

                if (regGpus.Count > 0)
                {
                    if (list.Count == 0)
                    {
                        list.AddRange(regGpus);
                    }
                    else
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            if (list[i].VramBytes == 0)
                            {
                                var match = regGpus.FirstOrDefault(r => r.Name.Equals(list[i].Name, StringComparison.OrdinalIgnoreCase));
                                if (match != null && match.VramBytes > 0)
                                {
                                    list[i] = new GpuDiagnostics(
                                        list[i].Name,
                                        match.VramBytes,
                                        list[i].DriverVersion != "Unknown" ? list[i].DriverVersion : match.DriverVersion);
                                }
                            }
                        }
                    }
                }
            }
        }
        catch { }

        if (list.Count == 0)
        {
            list.Add(new GpuDiagnostics("Standard Video Adapter", 0, "Unknown"));
        }

        return list;
    }

    private OsDiagnostics CollectOs()
    {
        string productName = "Windows";
        string displayVersion = "";
        int build = Environment.OSVersion.Version.Build;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key != null)
            {
                if (key.GetValue("ProductName") is string pName && !string.IsNullOrWhiteSpace(pName))
                    productName = pName.Trim();

                if (key.GetValue("DisplayVersion") is string dispVer && !string.IsNullOrWhiteSpace(dispVer))
                    displayVersion = dispVer.Trim();
                else if (key.GetValue("ReleaseId") is string relId && !string.IsNullOrWhiteSpace(relId))
                    displayVersion = relId.Trim();

                if (key.GetValue("CurrentBuildNumber") is string buildStr && int.TryParse(buildStr, out var b) && b > 0)
                    build = b;
                else if (key.GetValue("CurrentBuild") is string curBuild && int.TryParse(curBuild, out var cb) && cb > 0)
                    build = cb;
            }
        }
        catch { }

        // Normalize Windows 11 branding (Windows 11 builds are 22000+)
        if (build >= 22000 && productName.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase))
        {
            productName = "Windows 11" + productName.Substring("Windows 10".Length);
        }

        bool is64Bit = Environment.Is64BitOperatingSystem;
        bool isAdmin = false;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { }

        return new OsDiagnostics(productName, displayVersion, build, is64Bit, isAdmin);
    }

    private StorageDiagnostics CollectStorage()
    {
        try
        {
            string root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var drive = new DriveInfo(root);
            if (drive.IsReady && drive.TotalSize > 0)
            {
                return new StorageDiagnostics(drive.Name, (ulong)drive.TotalSize, (ulong)drive.AvailableFreeSpace);
            }
        }
        catch { }

        return new StorageDiagnostics("C:\\", 500UL * 1024 * 1024 * 1024, 100UL * 1024 * 1024 * 1024);
    }

    private AudioDiagnostics CollectAudio()
    {
        int inputCount = 0;
        int outputCount = 0;
        try
        {
            inputCount = waveInGetNumDevs();
            outputCount = waveOutGetNumDevs();
        }
        catch { }

        return new AudioDiagnostics(inputCount > 0, outputCount > 0, inputCount, outputCount);
    }

    private string? CollectWebView2Version()
    {
        try
        {
            return CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch
        {
            return null;
        }
    }

    public HardwareDiagnosticsResult EvaluateReadiness(
        DateTime timestamp,
        CpuDiagnostics cpu,
        RamDiagnostics ram,
        IReadOnlyList<GpuDiagnostics> gpus,
        OsDiagnostics os,
        StorageDiagnostics storage,
        AudioDiagnostics audio,
        string? webView2Version,
        bool virtualDesktopAvailable)
    {
        int score = 0;

        // 1. OS & Architecture (max 20 pts)
        if (os.Is64Bit && os.BuildNumber >= 19041)
            score += 20;
        else if (os.Is64Bit && os.BuildNumber >= 10240)
            score += 15;
        else
            score += 10;

        // 2. CPU & SIMD Acceleration (max 25 pts)
        if (cpu.HasAvx2 && cpu.LogicalProcessors >= 4)
            score += 25;
        else if (cpu.HasAvx2)
            score += 20;
        else if (cpu.HasAvx && cpu.LogicalProcessors >= 4)
            score += 18;
        else if (cpu.HasAvx)
            score += 15;
        else
            score += 10;

        // 3. RAM (max 25 pts)
        if (ram.TotalGigabytes >= 15.0)
            score += 25;
        else if (ram.TotalGigabytes >= 7.5)
            score += 22;
        else if (ram.TotalGigabytes >= 3.5)
            score += 15;
        else
            score += 5;

        // 4. Storage & Audio (max 15 pts)
        if (storage.FreeGigabytes >= 10.0)
            score += 5;
        else if (storage.FreeGigabytes >= 2.0)
            score += 3;

        if (audio.HasOutputDevice)
            score += 5;

        if (audio.HasInputDevice)
            score += 5;

        // 5. Software Environment (max 15 pts)
        bool hasWebView2 = !string.IsNullOrWhiteSpace(webView2Version);
        if (hasWebView2)
            score += 10;

        if (os.IsAdministrator)
            score += 5;
        else
            score += 2;

        score = Math.Clamp(score, 0, 100);

        // Feature Matrix Items
        bool isVi = LocalizationService.Current.Language == "vi";
        var featureMatrix = new List<FeatureReadinessItem>();

        // Feature 1: Window Capture Protection (WDA Stealth)
        string wdaName = isVi
            ? "Bảo vệ cửa sổ chống quay chụp (WDA)"
            : "Window Stealth Capture Protection (WDA)";
        if (os.Is64Bit && os.BuildNumber >= 19041)
        {
            featureMatrix.Add(new FeatureReadinessItem(
                "Diagnostics.FeatureWda",
                wdaName,
                ReadinessTier.Optimal,
                isVi ? "Hỗ trợ WDA_EXCLUDEFROMCAPTURE (Tàng hình 100%)" : "WDA_EXCLUDEFROMCAPTURE Supported (100% Stealth)",
                isVi
                    ? $"Windows 10/11 Build {os.BuildNumber} (64-bit) hỗ trợ loại trừ ghi hình hoàn toàn. Hệ thống sẽ ẩn triệt để các cửa sổ được bảo vệ khỏi phần mềm quay màn hình, Discord, OBS và công cụ chụp."
                    : $"Windows 10/11 Build {os.BuildNumber} (64-bit) provides full stealth capture exclusion. Windows will completely hide protected windows from screen recorders, Discord, OBS, and capture tools."
            ));
        }
        else if (os.BuildNumber >= 10240)
        {
            featureMatrix.Add(new FeatureReadinessItem(
                "Diagnostics.FeatureWda",
                wdaName,
                ReadinessTier.Good,
                isVi ? "Chỉ hỗ trợ WDA_MONITOR (Chế độ màn đen)" : "WDA_MONITOR Only (Blackout Mode)",
                isVi
                    ? $"Bản dựng Windows hiện tại ({os.BuildNumber}) chưa hỗ trợ ẩn hoàn toàn. Hệ thống sẽ hiển thị khung đen thay vì tàng hình trong suốt."
                    : $"Current Windows build ({os.BuildNumber}) does not support complete exclusion. Windows will render a blackout window instead of transparent stealth."
            ));
        }
        else
        {
            featureMatrix.Add(new FeatureReadinessItem(
                "Diagnostics.FeatureWda",
                wdaName,
                ReadinessTier.Warning,
                isVi ? "Giới hạn khả năng chống ghi hình" : "Limited Capture Affinity",
                isVi
                    ? "Phiên bản hệ điều hành quá cũ để đảm bảo bảo vệ chống quay chụp ở mức phần cứng."
                    : "Operating system build is too old to guarantee hardware-level capture protection."
            ));
        }

        // Feature 2: Live Captions & Whisper AI
        string captionsName = isVi
            ? "Phụ đề trực tiếp & Dịch AI thời gian thực (Whisper)"
            : "Live Captions & Real-time AI Translation (Whisper)";
        if (cpu.HasAvx2 && ram.TotalGigabytes >= 7.5 && audio.HasInputDevice)
        {
            featureMatrix.Add(new FeatureReadinessItem(
                "Diagnostics.FeatureCaptions",
                captionsName,
                ReadinessTier.Optimal,
                isVi ? "Tăng tốc AI thời gian thực tối ưu" : "Optimal Real-time AI Acceleration",
                isVi
                    ? $"CPU hỗ trợ tập lệnh vector AVX2 cùng {ram.TotalGigabytes:F1} GB RAM và micro đang hoạt động. Whisper AI nhận dạng thời gian thực với độ trễ thấp và độ chính xác cao."
                    : $"CPU AVX2 SIMD vector extensions enabled with {ram.TotalGigabytes:F1} GB RAM and active microphone. Real-time Whisper AI operates with low latency and high accuracy."
            ));
        }
        else if ((cpu.HasAvx || cpu.HasAvx2) && ram.TotalGigabytes >= 3.5 && audio.HasInputDevice)
        {
            featureMatrix.Add(new FeatureReadinessItem(
                "Diagnostics.FeatureCaptions",
                captionsName,
                ReadinessTier.Good,
                isVi ? "Hiệu năng AI đáp ứng tốt" : "Capable AI Performance",
                isVi
                    ? "Phần cứng đáp ứng đầy đủ yêu cầu cốt lõi để Whisper AI nhận dạng giọng nói thành văn bản ổn định."
                    : "Hardware meets core Whisper AI transcription requirements. Voice-to-text operates reliably."
            ));
        }
        else
        {
            string summary = !audio.HasInputDevice
                ? (isVi ? "Cần kết nối Micro" : "Microphone Required")
                : (isVi ? "Giới hạn CPU / RAM" : "CPU/Memory Limited");
            string detail = !audio.HasInputDevice
                ? (isVi
                    ? "Chưa phát hiện thiết bị micro. Tính năng phụ đề giọng nói trực tiếp cần thiết bị thu âm đang hoạt động."
                    : "No microphone detected. Real-time voice captions require an active audio input device.")
                : (isVi
                    ? "CPU thiếu tập lệnh AVX2 hoặc RAM hệ thống dưới 4 GB. Tốc độ tạo phụ đề có thể chậm hơn khi tải cao."
                    : "CPU lacks AVX2 instruction support or system RAM is below 4 GB. Transcription may experience latency under high load.");
            featureMatrix.Add(new FeatureReadinessItem(
                "Diagnostics.FeatureCaptions",
                captionsName,
                ReadinessTier.Warning,
                summary,
                detail
            ));
        }

        // Feature 3: Virtual Desktop Cloak
        string vdName = isVi
            ? "Khiên Desktop ảo & Ẩn không gian làm việc"
            : "Virtual Desktop Shield & Workspace Cloaking";
        if (virtualDesktopAvailable)
        {
            featureMatrix.Add(new FeatureReadinessItem(
                "Diagnostics.FeatureVd",
                vdName,
                ReadinessTier.Optimal,
                isVi ? "COM Desktop Manager đang hoạt động" : "COM Desktop Manager Active",
                isVi
                    ? "API IVirtualDesktopManager hoạt động đầy đủ. Tính năng ẩn không gian làm việc tức thì và khóa phím tắt đã sẵn sàng."
                    : "IVirtualDesktopManager API is fully functional. Instant workspace cloaking and hotkey shielding are enabled."
            ));
        }
        else
        {
            featureMatrix.Add(new FeatureReadinessItem(
                "Diagnostics.FeatureVd",
                vdName,
                ReadinessTier.Good,
                isVi ? "Chế độ ẩn dự phòng" : "Fallback Hide Mode",
                isVi
                    ? "COM API quản lý Desktop ảo chưa khả dụng hoặc bị hạn chế. Đã tự động chuyển sang cơ chế ẩn cửa sổ dự phòng."
                    : "Virtual desktop COM API unavailable or restricted. Fallback stealth hide mechanism is active."
            ));
        }

        // Feature 4: AI Traffic Shield & Proxy Purge
        string aiTrafficName = isVi
            ? "Khiên lưu lượng AI & Chống chặn bắt Proxy"
            : "AI Traffic Shield & Proxy Anti-Interception";
        if (os.IsAdministrator)
        {
            featureMatrix.Add(new FeatureReadinessItem(
                "Diagnostics.FeatureAiTraffic",
                aiTrafficName,
                ReadinessTier.Optimal,
                isVi ? "Toàn quyền bảo vệ Administrator" : "Full Administrator Shield",
                isVi
                    ? "Ứng dụng đang chạy với quyền Quản trị viên. Đủ thẩm quyền tự động xóa proxy loopback, phát hiện sniffer và chứng chỉ MITM hệ thống."
                    : "Elevated process execution active. Full authority to purge rogue loopback proxies, sniffers, and system MITM certificates."
            ));
        }
        else
        {
            featureMatrix.Add(new FeatureReadinessItem(
                "Diagnostics.FeatureAiTraffic",
                aiTrafficName,
                ReadinessTier.Warning,
                isVi ? "Chế độ người dùng thường (Giám sát & Cảnh báo)" : "User Mode (Monitoring & Alerting)",
                isVi
                    ? "Đang chạy với quyền người dùng chuẩn. Driver Booster vẫn phát hiện và xóa proxy cấp User, nhưng dọn proxy cấp hệ thống cần quyền Administrator."
                    : "Running as standard user. StreamGuard can detect rogue proxies and alert you, but clearing system-level proxies requires Administrator elevation."
            ));
        }

        // Feature 5: Embedded Stealth Browser (WebView2)
        string browserName = isVi
            ? "Trình duyệt bảo vệ nhúng sẵn (WebView2)"
            : "Stealth Embedded Web Browser (WebView2)";
        if (hasWebView2)
        {
            featureMatrix.Add(new FeatureReadinessItem(
                "Diagnostics.FeatureBrowser",
                browserName,
                ReadinessTier.Optimal,
                isVi ? "WebView2 Runtime đang hoạt động" : "WebView2 Runtime Active",
                isVi
                    ? $"Đã nhận diện Microsoft Edge WebView2 Runtime phiên bản {webView2Version}. Trình duyệt bảo vệ nhúng sẵn hoạt động đầy đủ."
                    : $"Microsoft Edge WebView2 Runtime version {webView2Version} detected. Embedded stealth browser is fully operational."
            ));
        }
        else
        {
            featureMatrix.Add(new FeatureReadinessItem(
                "Diagnostics.FeatureBrowser",
                browserName,
                ReadinessTier.Warning,
                isVi ? "Chưa cài đặt Runtime" : "Runtime Not Installed",
                isVi
                    ? "Thiếu Microsoft Edge WebView2 Runtime. Hãy cài đặt bản Evergreen Runtime để bật trình duyệt bảo vệ nhúng sẵn."
                    : "Microsoft Edge WebView2 Runtime is missing. Install the Evergreen runtime to enable the embedded stealth browser."
            ));
        }

        // Overall Tier
        ReadinessTier tier;
        bool hasWarningOrInsufficient = featureMatrix.Any(f => f.Tier is ReadinessTier.Warning or ReadinessTier.Insufficient);
        if (score >= 85 && hasWebView2 && ram.TotalGigabytes >= 7.5 && !hasWarningOrInsufficient)
            tier = ReadinessTier.Optimal;
        else if (score >= 70)
            tier = ReadinessTier.Good;
        else if (score >= 50)
            tier = ReadinessTier.Warning;
        else
            tier = ReadinessTier.Insufficient;

        // Recommendations
        var recommendations = new List<string>();

        if (!os.IsAdministrator)
        {
            recommendations.Add(LocalizationService.Current.Language == "vi"
                ? "Khởi chạy ứng dụng với quyền Administrator ('Run as administrator') để kích hoạt toàn quyền dọn dẹp proxy độc hại và bảo vệ hệ thống tuyệt đối."
                : "Run the application as Administrator to enable full system proxy purging and comprehensive traffic shielding.");
        }

        if (!hasWebView2)
        {
            recommendations.Add(LocalizationService.Current.Language == "vi"
                ? "Cài đặt Microsoft Edge WebView2 Runtime để kích hoạt tính năng Trình duyệt bảo vệ tàng hình."
                : "Install Microsoft Edge WebView2 Runtime to enable the embedded stealth browser.");
        }

        if (!audio.HasInputDevice)
        {
            recommendations.Add(LocalizationService.Current.Language == "vi"
                ? "Kết nối thiết bị ghi âm (Microphone) để sử dụng tính năng Phụ đề trực tiếp & Phiên dịch giọng nói thời gian thực."
                : "Connect a microphone to utilize Live Captions and real-time voice translation.");
        }

        if (!cpu.HasAvx2)
        {
            recommendations.Add(LocalizationService.Current.Language == "vi"
                ? "CPU không hỗ trợ lệnh AVX2; phụ đề AI Whisper sẽ sử dụng chế độ tương thích CPU thường."
                : "CPU lacks AVX2 instruction support; Whisper AI will run in fallback compatibility mode.");
        }

        if (ram.TotalGigabytes < 7.5)
        {
            recommendations.Add(LocalizationService.Current.Language == "vi"
                ? "Hệ thống có ít hơn 8 GB RAM; khuyến nghị đóng các ứng dụng nặng khi sử dụng tính năng phụ đề AI."
                : "System has less than 8 GB RAM; consider closing resource-heavy apps when running AI captions.");
        }

        if (ram.MemoryLoadPercentage > 85)
        {
            recommendations.Add(LocalizationService.Current.Language == "vi"
                ? $"Bộ nhớ RAM đang chịu tải cao ({ram.MemoryLoadPercentage}%); nên giải phóng RAM để ứng dụng hoạt động mượt mà nhất."
                : $"System memory load is high ({ram.MemoryLoadPercentage}%); free up memory for smoothest performance.");
        }

        if (storage.FreeGigabytes < 5.0)
        {
            recommendations.Add(LocalizationService.Current.Language == "vi"
                ? $"Ổ đĩa C: còn dưới {storage.FreeGigabytes:F1} GB trống; hãy dọn dẹp ổ đĩa để tránh ảnh hưởng bộ nhớ ảo."
                : $"Drive C: has less than {storage.FreeGigabytes:F1} GB free; free up disk space to ensure optimal virtual memory.");
        }

        if (recommendations.Count == 0)
        {
            recommendations.Add(LocalizationService.Current.Language == "vi"
                ? "Cấu hình phần cứng và hệ điều hành hoàn hảo! Ứng dụng hoạt động với 100% hiệu năng và đầy đủ các tính năng bảo vệ."
                : "Hardware and operating system are in optimal condition! All features operate smoothly at 100% performance.");
        }

        return new HardwareDiagnosticsResult(
            timestamp,
            cpu,
            ram,
            gpus,
            os,
            storage,
            audio,
            webView2Version,
            score,
            tier,
            featureMatrix,
            recommendations
        );
    }

    public string GenerateReportText(HardwareDiagnosticsResult result, string? lang = null)
    {
        var isVi = (lang ?? LocalizationService.Current.Language) == "vi";
        var sb = new StringBuilder();

        sb.AppendLine("======================================================================");
        sb.AppendLine(isVi
            ? "  DRIVER BOOSTER - BÁO CÁO PHẦN CỨNG & ĐÁNH GIÁ TƯƠNG THÍCH HỆ THỐNG"
            : "  DRIVER BOOSTER - HARDWARE & SYSTEM COMPATIBILITY REPORT");
        sb.AppendLine("======================================================================");
        sb.AppendLine(isVi
            ? $"Thời gian khởi tạo: {result.Timestamp:yyyy-MM-dd HH:mm:ss}"
            : $"Generated at: {result.Timestamp:yyyy-MM-dd HH:mm:ss}");

        string tierText = result.OverallTier switch
        {
            ReadinessTier.Optimal => isVi ? "XUẤT SẮC (Mượt mà 100%)" : "OPTIMAL (100% Smooth)",
            ReadinessTier.Good => isVi ? "ĐỦ ĐIỀU KIỆN (Rất tốt)" : "GOOD (Fully Capable)",
            ReadinessTier.Warning => isVi ? "CẦN LƯU Ý (Có giới hạn phụ)" : "NOTICE (Minor Bottlenecks)",
            _ => isVi ? "KHÔNG KHUYẾN NGHỊ (Cần nâng cấp)" : "INSUFFICIENT (Upgrade Recommended)"
        };

        sb.AppendLine(isVi
            ? $"Điểm tương thích : {result.CompatibilityScore} / 100 [{tierText}]"
            : $"Readiness Score  : {result.CompatibilityScore} / 100 [{tierText}]");
        sb.AppendLine();

        sb.AppendLine(isVi ? "[THÔNG SỐ PHẦN CỨNG VÀ HỆ THỐNG]" : "[SYSTEM SPECIFICATIONS]");
        sb.AppendLine(isVi
            ? $"- Hệ điều hành     : {result.Os.ProductName} {result.Os.DisplayVersion} (Build {result.Os.BuildNumber}, {(result.Os.Is64Bit ? "64-bit" : "32-bit")})"
            : $"- Operating System : {result.Os.ProductName} {result.Os.DisplayVersion} (Build {result.Os.BuildNumber}, {(result.Os.Is64Bit ? "64-bit" : "32-bit")})");
        sb.AppendLine(isVi
            ? $"- Quyền thực thi   : {(result.Os.IsAdministrator ? "Administrator (Đầy đủ quyền quản trị)" : "Standard User (Người dùng thường)")}"
            : $"- Privilege Level  : {(result.Os.IsAdministrator ? "Administrator (Elevated)" : "Standard User (Non-elevated)")}");
        sb.AppendLine(isVi
            ? $"- Vi xử lý (CPU)   : {result.Cpu.Name} ({result.Cpu.Cores} nhân / {result.Cpu.LogicalProcessors} luồng, {result.Cpu.Architecture})"
            : $"- Processor (CPU)  : {result.Cpu.Name} ({result.Cpu.Cores} cores / {result.Cpu.LogicalProcessors} threads, {result.Cpu.Architecture})");
        sb.AppendLine(isVi
            ? $"- Tăng tốc AI SIMD : AVX2: {(result.Cpu.HasAvx2 ? "Có" : "Không")} | AVX: {(result.Cpu.HasAvx ? "Có" : "Không")}"
            : $"- AI Acceleration  : AVX2: {(result.Cpu.HasAvx2 ? "Supported" : "No")} | AVX: {(result.Cpu.HasAvx ? "Supported" : "No")}");
        sb.AppendLine(isVi
            ? $"- Bộ nhớ RAM       : {result.Ram.TotalGigabytes:F1} GB Tổng | {result.Ram.AvailableGigabytes:F1} GB Trống (Đang dùng {result.Ram.MemoryLoadPercentage}%)"
            : $"- System Memory    : {result.Ram.TotalGigabytes:F1} GB Total | {result.Ram.AvailableGigabytes:F1} GB Available ({result.Ram.MemoryLoadPercentage}% Used)");

        if (result.Gpus.Count > 0)
        {
            var gpuSummary = string.Join("; ", result.Gpus.Select(g =>
                $"{g.Name}{(g.VramBytes > 0 ? $" ({g.VramGigabytes:F1} GB VRAM)" : "")}"));
            sb.AppendLine(isVi
                ? $"- Card đồ họa GPU  : {gpuSummary}"
                : $"- Graphics (GPU)   : {gpuSummary}");
        }

        sb.AppendLine(isVi
            ? $"- Ổ đĩa hệ thống   : Ổ {result.Storage.DriveName} {result.Storage.FreeGigabytes:F1} GB trống / {result.Storage.TotalGigabytes:F1} GB ({result.Storage.FreePercentage:F1}% trống)"
            : $"- System Storage   : Drive {result.Storage.DriveName} {result.Storage.FreeGigabytes:F1} GB free / {result.Storage.TotalGigabytes:F1} GB ({result.Storage.FreePercentage:F1}% free)");
        sb.AppendLine(isVi
            ? $"- Thiết bị Âm thanh: Micro: {(result.Audio.HasInputDevice ? "Sẵn sàng" : "Chưa kết nối")} | Loa/Tai nghe: {(result.Audio.HasOutputDevice ? "Sẵn sàng" : "Chưa phát hiện")}"
            : $"- Audio Devices    : Microphone: {(result.Audio.HasInputDevice ? "Available" : "Not detected")} | Speakers: {(result.Audio.HasOutputDevice ? "Available" : "Not detected")}");
        sb.AppendLine(isVi
            ? $"- WebView2 Runtime : {(!string.IsNullOrWhiteSpace(result.WebView2Version) ? result.WebView2Version + " (Đã cài đặt)" : "Chưa cài đặt")}"
            : $"- WebView2 Runtime : {(!string.IsNullOrWhiteSpace(result.WebView2Version) ? result.WebView2Version + " (Installed)" : "Not installed")}");
        sb.AppendLine();

        sb.AppendLine(isVi ? "[MA TRẬN ĐÁNH GIÁ TÍNH NĂNG]" : "[FEATURE READINESS MATRIX]");
        foreach (var item in result.FeatureMatrix)
        {
            string itemTier = item.Tier switch
            {
                ReadinessTier.Optimal => isVi ? "[TỐI ƯU]" : "[OPTIMAL]",
                ReadinessTier.Good => isVi ? "[TỐT]" : "[GOOD]",
                ReadinessTier.Warning => isVi ? "[LƯU Ý]" : "[NOTICE]",
                _ => isVi ? "[CHƯA ĐẠT]" : "[UNSUPPORTED]"
            };
            sb.AppendLine($"- {item.FeatureName}: {itemTier} {item.StatusSummary}");
            sb.AppendLine($"  {item.Detail}");
        }
        sb.AppendLine();

        sb.AppendLine(isVi ? "[KHUYẾN NGHỊ TỐI ƯU HỆ THỐNG]" : "[SMART OPTIMIZATION RECOMMENDATIONS]");
        foreach (var rec in result.Recommendations)
        {
            sb.AppendLine($"- {rec}");
        }
        sb.AppendLine("======================================================================");

        return sb.ToString();
    }
}
