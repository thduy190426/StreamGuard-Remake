using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StreamGuard.Services
{
    public sealed record AntigravityNotificationsStatus(
        bool IsMuted,
        bool SettingsApplied,
        bool RegistryApplied,
        string Message);

    public sealed class AntigravityNotificationsService : IDisposable
    {
        public const string NotificationsSettingsRootKey = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";

        public static readonly IReadOnlyList<string> TargetAppUserModelIds = new[]
        {
            "electron.app.Antigravity",
            "Google.AntigravityIDE",
            "Google.Antigravity",
            "Microsoft.VisualStudioCode"
        };

        public const string EnableNotificationsKey = "enableNotificationsForSpecialEvents";
        public const string EnableSoundsKey = "enableSoundsForSpecialEvents";
        public const string DoNotDisturbKey = "workbench.notifications.doNotDisturbMode";

        private readonly object _gate = new();
        private readonly Func<bool, bool>? _customApplyHandler;
        private readonly string _geminiConfigPath;
        private readonly IReadOnlyList<string> _ideSettingsPaths;
        private volatile bool _isMuted;
        private volatile bool _disposed;

        public bool IsMuted => _isMuted;

        public AntigravityNotificationsService(
            Func<bool, bool>? customApplyHandler = null,
            string? geminiConfigPath = null,
            IEnumerable<string>? ideSettingsPaths = null)
        {
            _customApplyHandler = customApplyHandler;
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            _geminiConfigPath = geminiConfigPath ?? Path.Combine(userProfile, ".gemini", "config", "config.json");
            _ideSettingsPaths = (ideSettingsPaths ?? new[]
            {
                Path.Combine(appData, "Antigravity IDE", "User", "settings.json"),
                Path.Combine(appData, "Antigravity", "User", "settings.json")
            }).ToArray();
        }

        public AntigravityNotificationsStatus SetMuted(bool mute)
        {
            lock (_gate)
            {
                if (_disposed)
                    return new AntigravityNotificationsStatus(false, false, false, "Antigravity notification guard stopped");

                _isMuted = mute;

                if (_customApplyHandler != null)
                {
                    var customOk = _customApplyHandler(mute);
                    return new AntigravityNotificationsStatus(
                        mute,
                        customOk,
                        customOk,
                        mute
                            ? "Muted task completion and approval notifications for Antigravity & Antigravity IDE"
                            : "Restored task completion and approval notifications for Antigravity & Antigravity IDE");
                }

                var settingsOk = ApplyConfigFiles(mute);
                var registryOk = ApplyWindowsNotificationRegistry(mute);

                var message = mute
                    ? (settingsOk && registryOk
                        ? "Muted task completion and approval notifications for Antigravity & Antigravity IDE"
                        : "Antigravity notification mute enabled (some settings or registry keys could not be updated)")
                    : "Restored task completion and approval notifications for Antigravity & Antigravity IDE";

                return new AntigravityNotificationsStatus(mute, settingsOk, registryOk, message);
            }
        }

        internal bool ApplyConfigFiles(bool mute)
        {
            var geminiOk = UpdateGeminiSharedConfig(_geminiConfigPath, mute);
            var ideOk = true;
            foreach (var settingsPath in _ideSettingsPaths)
            {
                if (!UpdateIdeSettingsJson(settingsPath, mute))
                    ideOk = false;
            }
            return geminiOk && ideOk;
        }

        internal static bool UpdateGeminiSharedConfig(string path, bool mute)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (string.IsNullOrWhiteSpace(dir)) return false;
                Directory.CreateDirectory(dir);

                JObject root;
                if (File.Exists(path))
                {
                    var existing = File.ReadAllText(path, Encoding.UTF8);
                    root = string.IsNullOrWhiteSpace(existing) ? new JObject() : JObject.Parse(existing);
                }
                else
                {
                    root = new JObject();
                }

                if (root["userSettings"] is not JObject userSettings)
                {
                    userSettings = new JObject();
                    root["userSettings"] = userSettings;
                }

                userSettings[EnableNotificationsKey] = !mute;
                userSettings[EnableSoundsKey] = !mute;

                WriteJsonAtomically(path, root.ToString(Formatting.Indented));
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static bool UpdateIdeSettingsJson(string path, bool mute)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (string.IsNullOrWhiteSpace(dir)) return false;

                // Only create/update if the parent application profile directory exists or the settings file already exists.
                var appProfileDir = Path.GetDirectoryName(dir);
                if (!File.Exists(path) && (string.IsNullOrWhiteSpace(appProfileDir) || !Directory.Exists(appProfileDir)))
                    return true;

                Directory.CreateDirectory(dir);

                JObject root;
                if (File.Exists(path))
                {
                    var existing = File.ReadAllText(path, Encoding.UTF8);
                    root = string.IsNullOrWhiteSpace(existing) ? new JObject() : JObject.Parse(existing);
                }
                else
                {
                    root = new JObject();
                }

                if (mute)
                {
                    root[DoNotDisturbKey] = true;
                }
                else
                {
                    root.Remove(DoNotDisturbKey);
                }

                WriteJsonAtomically(path, root.ToString(Formatting.Indented));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool ApplyWindowsNotificationRegistry(bool mute)
        {
            var ok = true;
            var dwordValue = mute ? 0 : 1;

            foreach (var appId in TargetAppUserModelIds)
            {
                try
                {
                    var subKeyPath = $@"{NotificationsSettingsRootKey}\{appId}";
                    using var key = Registry.CurrentUser.CreateSubKey(subKeyPath, writable: true);
                    if (key != null)
                    {
                        key.SetValue("Enabled", dwordValue, RegistryValueKind.DWord);
                        key.SetValue("ShowInActionCenter", dwordValue, RegistryValueKind.DWord);
                        key.SetValue("AllowSound", dwordValue, RegistryValueKind.DWord);
                    }
                    else
                    {
                        ok = false;
                    }
                }
                catch
                {
                    ok = false;
                }
            }

            return ok;
        }

        private static void WriteJsonAtomically(string path, string content)
        {
            var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true))
                {
                    writer.Write(content);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(path))
                    File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
                else
                    File.Move(tempPath, path);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
            }
        }
    }
}
