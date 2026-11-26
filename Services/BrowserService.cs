using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;

namespace StreamGuard.Services;

public static class BrowserService
{
    public const string DefaultHomeUrl = "https://www.google.com";
    private static readonly Regex DomainRegex = new(@"^[a-zA-Z0-9]([a-zA-Z0-9\-]*[a-zA-Z0-9])?(\.[a-zA-Z0-9]([a-zA-Z0-9\-]*[a-zA-Z0-9])?)+(:\d+)?([/?#].*)?$", RegexOptions.Compiled);
    private static readonly Regex IpRegex = new(@"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}(:\d+)?([/?#].*)?$", RegexOptions.Compiled);
    private static readonly Regex WindowsPathRegex = new(@"^[a-zA-Z]:[\\/]", RegexOptions.Compiled);

    public static string GetUserDataFolderPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StreamGuard",
            "BrowserProfile");
    }

    public static string ResolveNavigationUrl(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return DefaultHomeUrl;
        }

        var trimmed = input.Trim();

        // Already has scheme
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("file:///", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("edge://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("chrome://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        // Local Windows file path e.g. C:\path\file.html
        if (WindowsPathRegex.IsMatch(trimmed))
        {
            try
            {
                return new Uri(trimmed).AbsoluteUri;
            }
            catch { }
        }

        // If it contains whitespace, treat as Google search query
        if (trimmed.Any(char.IsWhiteSpace))
        {
            return BuildSearchUrl(trimmed);
        }

        // Localhost special case
        if (trimmed.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) &&
            (trimmed.Length == 9 || trimmed[9] == ':' || trimmed[9] == '/' || trimmed[9] == '?' || trimmed[9] == '#'))
        {
            return "http://" + trimmed;
        }

        // IP address
        if (IpRegex.IsMatch(trimmed))
        {
            return "http://" + trimmed;
        }

        // Standard domain syntax (e.g. google.com, sub.example.org/path)
        if (DomainRegex.IsMatch(trimmed))
        {
            return "https://" + trimmed;
        }

        // Single word or non-domain text -> Google search
        return BuildSearchUrl(trimmed);
    }

    public static string BuildSearchUrl(string query)
    {
        return "https://www.google.com/search?q=" + Uri.EscapeDataString(query);
    }

    public static bool IsWebView2Available()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return !string.IsNullOrWhiteSpace(version);
        }
        catch
        {
            return false;
        }
    }

    public static string? FindChromeExecutable()
    {
        // 1. Check Windows App Paths registry
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe")
                         ?? Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe");
            var regPath = key?.GetValue(null) as string;
            if (!string.IsNullOrWhiteSpace(regPath) && File.Exists(regPath)) return regPath;
        }
        catch { }

        // 2. Standard candidate paths
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }

        return null;
    }

    public static bool OpenInExternalBrowser(string url, bool preferChrome = false)
    {
        try
        {
            var safeUrl = url.Replace("\"", "%22");
            if (preferChrome)
            {
                var chromePath = FindChromeExecutable();
                if (chromePath != null)
                {
                    using var proc = Process.Start(new ProcessStartInfo(chromePath, $"\"{safeUrl}\"") { UseShellExecute = true });
                    return true;
                }
            }

            using var fallback = Process.Start(new ProcessStartInfo(safeUrl) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
