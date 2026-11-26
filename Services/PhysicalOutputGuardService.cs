using System;
using System.Linq;
using System.Management;
using System.Windows.Forms;

namespace StreamGuard.Services
{
    public sealed class PhysicalOutputStatus
    {
        public bool ExternalOutputActive { get; init; }
        public bool SeparateExternalScreen { get; init; }
        public bool MirrorLikely { get; init; }
        public int ScreenCount { get; init; }
        public int ExternalConnectorCount { get; init; }
        public string Summary { get; init; } = "";
    }

    public sealed class PhysicalOutputGuardService
    {
        private static readonly TimeSpan ConnectorCacheDuration = TimeSpan.FromSeconds(5);
        private DateTime _lastConnectorScan = DateTime.MinValue;
        private int _cachedExternalConnectorCount;

        public PhysicalOutputStatus GetStatus()
        {
            var screens = Screen.AllScreens;
            var screenCount = Math.Max(0, screens.Length);
            var separateExternalScreen = screens.Any(screen => !screen.Primary);
            var externalConnectorCount = GetExternalConnectorCountCached();
            var externalOutputActive = separateExternalScreen || externalConnectorCount > 0 || screenCount > 1;
            var mirrorLikely = externalOutputActive && !separateExternalScreen;

            return new PhysicalOutputStatus
            {
                ExternalOutputActive = externalOutputActive,
                SeparateExternalScreen = separateExternalScreen,
                MirrorLikely = mirrorLikely,
                ScreenCount = screenCount,
                ExternalConnectorCount = externalConnectorCount,
                Summary = $"screens={screenCount}, external-connectors={externalConnectorCount}, mirror-likely={mirrorLikely}"
            };
        }

        private int GetExternalConnectorCountCached()
        {
            if (DateTime.Now - _lastConnectorScan < ConnectorCacheDuration)
                return _cachedExternalConnectorCount;

            _lastConnectorScan = DateTime.Now;
            _cachedExternalConnectorCount = GetExternalConnectorCount();
            return _cachedExternalConnectorCount;
        }

        private static int GetExternalConnectorCount()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    @"root\wmi",
                    "SELECT VideoOutputTechnology FROM WmiMonitorConnectionParams");

                using var connections = searcher.Get();
                var count = 0;
                foreach (ManagementObject connection in connections)
                    using (connection)
                        if (IsExternalOutputTechnology(ReadUInt32(connection["VideoOutputTechnology"]))) count++;
                return count;
            }
            catch
            {
                return 0;
            }
        }

        private static uint ReadUInt32(object? value)
        {
            return value switch
            {
                uint u => u,
                int i => unchecked((uint)i),
                ushort us => us,
                short s => unchecked((uint)s),
                byte b => b,
                _ => 0
            };
        }

        private static bool IsExternalOutputTechnology(uint technology)
        {
            // D3DKMDT_VIDEO_OUTPUT_TECHNOLOGY values commonly used for physical output.
            // 5 = HDMI, 10 = external DisplayPort, 12 = external UDI, 16 = indirect wired.
            return technology is 0 or 3 or 4 or 5 or 8 or 9 or 10 or 12 or 14 or 15 or 16;
        }
    }
}
