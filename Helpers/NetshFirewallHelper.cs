using System;
using System.Diagnostics;
using StreamGuard.Services;

namespace StreamGuard.Helpers
{
    public static class NetshFirewallHelper
    {
        public static string FormatAddProgramRuleArgs(string ruleName, string programPath, string direction)
        {
            var dir = !string.IsNullOrWhiteSpace(direction) &&
                (direction.Equals("Inbound", StringComparison.OrdinalIgnoreCase) || direction.Equals("in", StringComparison.OrdinalIgnoreCase))
                ? "in" : "out";
            return $"advfirewall firewall add rule name=\"{ruleName}\" dir={dir} action=block program=\"{programPath}\" profile=any enable=yes";
        }

        public static string FormatAddRemoteIpRuleArgs(string ruleName, string remoteAddress, string direction = "out")
        {
            var dir = !string.IsNullOrWhiteSpace(direction) &&
                (direction.Equals("Inbound", StringComparison.OrdinalIgnoreCase) || direction.Equals("in", StringComparison.OrdinalIgnoreCase))
                ? "in" : "out";
            return $"advfirewall firewall add rule name=\"{ruleName}\" dir={dir} action=block remoteip=\"{remoteAddress}\" profile=any enable=yes";
        }

        public static string FormatDeleteRuleByNameArgs(string ruleName)
        {
            return $"advfirewall firewall delete rule name=\"{ruleName}\"";
        }

        public static string FormatDeleteRulesByProgramArgs(string programPath)
        {
            return $"advfirewall firewall delete rule name=all program=\"{programPath}\"";
        }

        public static int RunNetsh(string arguments, int timeoutMs = 8000)
        {
            try
            {
                var info = new ProcessStartInfo("netsh.exe", arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                return ChildProcessRunner.Run(info, timeoutMs).ExitCode;
            }
            catch
            {
                return -1;
            }
        }
    }
}
