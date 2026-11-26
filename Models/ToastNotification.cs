using System;

namespace StreamGuard.Models;

public enum ToastType
{
    Success,
    Error,
    Warning,
    Info
}

public class ToastNotification
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Message { get; set; } = string.Empty;
    public ToastType Type { get; set; } = ToastType.Info;
    public int DurationMs { get; set; } = 2000;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public int RepeatCount { get; set; } = 1;

    public ToastNotification() { }

    public ToastNotification(string message, ToastType type = ToastType.Info, int durationMs = 2000)
    {
        Message = message ?? string.Empty;
        Type = type;
        DurationMs = durationMs;
        Timestamp = DateTime.UtcNow;
        RepeatCount = 1;
    }
}
