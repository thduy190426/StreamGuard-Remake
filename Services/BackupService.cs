using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StreamGuard.Models;

namespace StreamGuard.Services;

public static class BackupService
{
    public const int MaximumBackupBytes = 10 * 1024 * 1024; // 10 MB

    public static BackupBundle CreateBundle(StreamGuardConfig? config, CaptionSettings? captions)
    {
        var bundle = new BackupBundle
        {
            App = "Driver Booster",
            Version = "1.0",
            ExportedAt = DateTime.UtcNow,
            Config = config ?? new StreamGuardConfig(),
            Captions = captions ?? new CaptionSettings()
        };
        bundle.Captions.Normalize();
        return bundle;
    }

    public static string SerializeBundle(BackupBundle bundle)
    {
        bundle.Captions.Normalize();
        return JsonConvert.SerializeObject(bundle, Formatting.Indented);
    }

    public static bool TryExportToFile(string filePath, BackupBundle bundle, out string? errorMessage)
    {
        errorMessage = null;
        string? tempPath = null;
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                errorMessage = "Invalid destination file path.";
                return false;
            }

            var fullPath = Path.GetFullPath(filePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = SerializeBundle(bundle);
            var utf8 = new UTF8Encoding(false);
            if (utf8.GetByteCount(json) > MaximumBackupBytes)
            {
                errorMessage = "Backup configuration is too large to export.";
                return false;
            }

            tempPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tempPath, json, utf8);

            if (File.Exists(fullPath))
            {
                try
                {
                    File.Replace(tempPath, fullPath, null, ignoreMetadataErrors: true);
                }
                catch (IOException)
                {
                    // Fallback for file systems (e.g. FAT32/exFAT USB drives) or volumes that don't support transactional ReplaceFile
                    File.Copy(tempPath, fullPath, overwrite: true);
                }
            }
            else
            {
                File.Move(tempPath, fullPath);
            }

            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
        finally
        {
            if (tempPath != null)
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }
    }

    public static bool TryParseBundle(string json, out BackupBundle? bundle, out string? errorMessage)
    {
        bundle = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            errorMessage = "Configuration data is empty.";
            return false;
        }

        if (Encoding.UTF8.GetByteCount(json) > MaximumBackupBytes)
        {
            errorMessage = "Configuration file exceeds maximum allowed size (10 MB).";
            return false;
        }

        try
        {
            var token = JToken.Parse(json);
            if (token is JArray arr)
            {
                // Legacy apps array format
                var apps = arr.ToObject<List<GuardedAppConfig>>() ?? new();
                bundle = new BackupBundle
                {
                    Config = new StreamGuardConfig { Apps = SanitizeApps(apps) },
                    Captions = new CaptionSettings()
                };
                return true;
            }

            if (token is JObject obj)
            {
                // Full BackupBundle format (contains "Config")
                if (obj.TryGetValue("Config", StringComparison.OrdinalIgnoreCase, out var configToken) && configToken is JObject cfgObj)
                {
                    var config = cfgObj.ToObject<StreamGuardConfig>() ?? new();
                    if (!HasPropertyIgnoreCase(cfgObj, nameof(StreamGuardConfig.BlockRaiaWebsite)))
                    {
                        config.BlockRaiaWebsite = config.BlockKnownWebsites;
                    }
                    config.BlockKnownWebsites = config.BlockRaiaWebsite || config.BlockPortalWebsite;
                    config.Apps = SanitizeApps(config.Apps);

                    CaptionSettings captions = new();
                    if (obj.TryGetValue("Captions", StringComparison.OrdinalIgnoreCase, out var capToken) && capToken is JObject capObj)
                    {
                        captions = capObj.ToObject<CaptionSettings>() ?? new();
                    }
                    captions.Normalize();

                    bundle = new BackupBundle
                    {
                        App = obj.Value<string>("App") ?? "Driver Booster",
                        Version = obj.Value<string>("Version") ?? "1.0",
                        ExportedAt = obj.TryGetValue("ExportedAt", StringComparison.OrdinalIgnoreCase, out var dateToken) && DateTime.TryParse(dateToken.ToString(), out var dt) ? dt : DateTime.UtcNow,
                        Config = config,
                        Captions = captions
                    };
                    return true;
                }

                // Direct StreamGuardConfig format (contains settings keys)
                if (HasPropertyIgnoreCase(obj, "Apps") ||
                    HasPropertyIgnoreCase(obj, "BlockCameraAccess") ||
                    HasPropertyIgnoreCase(obj, "Language") ||
                    HasPropertyIgnoreCase(obj, "AutoStartShield") ||
                    HasPropertyIgnoreCase(obj, "BlockRaiaWebsite") ||
                    HasPropertyIgnoreCase(obj, "BlockKnownWebsites"))
                {
                    var config = obj.ToObject<StreamGuardConfig>() ?? new();
                    if (!HasPropertyIgnoreCase(obj, nameof(StreamGuardConfig.BlockRaiaWebsite)))
                    {
                        config.BlockRaiaWebsite = config.BlockKnownWebsites;
                    }
                    config.BlockKnownWebsites = config.BlockRaiaWebsite || config.BlockPortalWebsite;
                    config.Apps = SanitizeApps(config.Apps);

                    CaptionSettings captions = new();
                    if (obj.TryGetValue("Captions", StringComparison.OrdinalIgnoreCase, out var directCapToken) && directCapToken is JObject directCapObj)
                    {
                        captions = directCapObj.ToObject<CaptionSettings>() ?? new();
                    }
                    captions.Normalize();

                    bundle = new BackupBundle
                    {
                        Config = config,
                        Captions = captions
                    };
                    return true;
                }
            }

            errorMessage = "Unrecognized configuration file schema.";
            return false;
        }
        catch (JsonException ex)
        {
            errorMessage = $"JSON parsing error: {ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    public static bool TryImportFromFile(string filePath, out BackupBundle? bundle, out string? errorMessage)
    {
        bundle = null;
        errorMessage = null;

        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                errorMessage = "Selected file does not exist.";
                return false;
            }

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > MaximumBackupBytes)
            {
                errorMessage = "Configuration file exceeds maximum allowed size (10 MB).";
                return false;
            }

            var json = File.ReadAllText(filePath, Encoding.UTF8);
            return TryParseBundle(json, out bundle, out errorMessage);
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static bool HasPropertyIgnoreCase(JObject obj, string name) =>
        obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out _);

    public static List<GuardedAppConfig> SanitizeApps(IEnumerable<GuardedAppConfig>? apps)
    {
        var result = new List<GuardedAppConfig>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var a in apps ?? Array.Empty<GuardedAppConfig>())
        {
            if (a == null || string.IsNullOrWhiteSpace(a.ProcessName)) continue;
            var rawProc = a.ProcessName.Trim();
            var proc = rawProc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(rawProc)
                : rawProc;
            if (string.IsNullOrWhiteSpace(proc)) continue;

            var exe = a.ExecutablePath?.Trim() ?? "";
            if (ProcessSafetyService.IsProtectedIdentity(proc, exe)) continue;
            if (!seen.Add(proc)) continue;

            result.Add(new GuardedAppConfig
            {
                Name = string.IsNullOrWhiteSpace(a.Name) ? proc : a.Name.Trim(),
                ProcessName = proc,
                ExecutablePath = exe,
                IsEnabled = a.IsEnabled,
                SecureHotkey = a.SecureHotkey?.Trim() ?? "",
                IsHotkeyDisabled = a.IsHotkeyDisabled
            });
        }
        return result;
    }
}
