using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

namespace StreamGuard.Models;

public enum RecoveryState { Pending, Protected, Failed, Recovered, Closed, Unverified }
public enum RecoveryMethod { Preparing, HiddenWindow, VirtualDesktop, WindowHost }
public sealed record RecoveryIdentity(long Window, int ProcessId, long ProcessStartedUtcTicks,
    int SessionId, string ProcessName, string ExecutablePath);

public sealed record RecoverySnapshot
{
    public RecoveryIdentity Identity { get; init; } = new(0, 0, 0, 0, "", "");
    public RecoveryIdentity? Parent { get; init; }
    public RecoveryIdentity? Owner { get; init; }
    public string WindowTitle { get; init; } = "";
    public long Style { get; init; }
    public long ExStyle { get; init; }
    public int Left { get; init; }
    public int Top { get; init; }
    public int Right { get; init; }
    public int Bottom { get; init; }
    public int ShowCommand { get; init; }
    public Guid? DesktopId { get; init; }
}

public partial class RecoveryRecord : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerInstance { get; set; }
    public long Token { get; set; }
    public string ApplicationName { get; set; } = "";
    public RecoverySnapshot Snapshot { get; set; } = new();
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    [JsonIgnore] public bool WindowReleasedInMemory { get; set; }
    [ObservableProperty] private RecoveryState _state = RecoveryState.Pending;
    [ObservableProperty] private RecoveryMethod _method;
    [ObservableProperty] private string _detail = "";
    [property: JsonIgnore]
    [ObservableProperty] private bool _isPreviousSession;

    [JsonIgnore] public bool CanRetry => State is not (RecoveryState.Recovered or RecoveryState.Closed);
    [JsonIgnore] public bool CanDelete => !(State == RecoveryState.Protected && !IsPreviousSession);
    [JsonIgnore] public string DeleteTooltipText => CanDelete
        ? "Remove this session record from the recovery list"
        : "Cannot delete a currently protected window session. Stop protection first.";
    [JsonIgnore] public string ExecutablePath => Snapshot.Identity.ExecutablePath;
    [JsonIgnore] public string WindowTitle => string.IsNullOrWhiteSpace(Snapshot.WindowTitle) ? ApplicationName : Snapshot.WindowTitle;
    [JsonIgnore] public string StatusText => State switch
    {
        RecoveryState.Pending => "Recovery pending", RecoveryState.Protected => "Managed window",
        RecoveryState.Failed => "Recovery failed", RecoveryState.Recovered => "Recovered",
        RecoveryState.Closed => "Window closed", _ => "Verification required"
    };
    [JsonIgnore] public string MethodText => Method switch
    {
        RecoveryMethod.HiddenWindow => "Hidden window", RecoveryMethod.VirtualDesktop => "Virtual desktop",
        RecoveryMethod.WindowHost => "Window host", _ => "Preparing window"
    };
    [JsonIgnore] public string SessionText => IsPreviousSession ? "Previous session" : "Current session";
    partial void OnStateChanged(RecoveryState value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(DeleteTooltipText));
    }
    partial void OnMethodChanged(RecoveryMethod value) => OnPropertyChanged(nameof(MethodText));
    partial void OnIsPreviousSessionChanged(bool value)
    {
        OnPropertyChanged(nameof(SessionText));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(DeleteTooltipText));
    }
}
