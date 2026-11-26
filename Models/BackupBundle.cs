using System;
using StreamGuard.Services;

namespace StreamGuard.Models;

public sealed class BackupBundle
{
    public string App { get; set; } = "Driver Booster";
    public string Version { get; set; } = "1.0";
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
    public StreamGuardConfig Config { get; set; } = new();
    public CaptionSettings Captions { get; set; } = new();
}
