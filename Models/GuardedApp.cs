using CommunityToolkit.Mvvm.ComponentModel;
using StreamGuard.Helpers;
using System;
using System.Collections.Generic;
using System.Windows;

namespace StreamGuard.Models
{
    public sealed class ProtectedWindowState
    {
        public IntPtr Handle { get; set; } = IntPtr.Zero;
        public int ProcessId { get; set; }
        public Guid? RecoveryId { get; set; }
        public Guid? DesktopId { get; set; }
        public bool HiddenByStreamGuard { get; set; }
        public bool WrappedByStreamGuard { get; set; }
        public bool ShieldedByStreamGuard { get; set; }
        public IntPtr OriginalParent { get; set; } = IntPtr.Zero;
        public nint OriginalStyle { get; set; }
        public nint OriginalExStyle { get; set; }
        public int OriginalLeft { get; set; }
        public int OriginalTop { get; set; }
        public int OriginalWidth { get; set; }
        public int OriginalHeight { get; set; }
        public Window? WrapperWindow { get; set; }
        public System.Windows.Forms.Form? WrapperForm { get; set; }
    }

    public partial class GuardedApp : ObservableObject
    {
        [ObservableProperty] private string _name = "";
        [ObservableProperty] private string _processName = "";
        [ObservableProperty] private string _executablePath = "";
        [ObservableProperty] private bool _isEnabled = true;
        [ObservableProperty] private bool _isProtected = false;
        [ObservableProperty] private string _status = "Visible";
        [ObservableProperty] private string _secureHotkey = "";
        [ObservableProperty] private bool _isHotkeyDisabled;
        [ObservableProperty] private System.Windows.Media.ImageSource? _appIcon;

        public System.Windows.Media.ImageSource? Icon => AppIcon ?? IconHelper.GetIconForApp(ExecutablePath, ProcessName, WindowHandle);

        public IntPtr WindowHandle { get; set; } = IntPtr.Zero;
        public Guid? DesktopId { get; set; } = null; // Desktop gốc để restore
        public bool HiddenByStreamGuard { get; set; } = false;
        public List<ProtectedWindowState> ProtectedWindows { get; } = new();

        public string DisplayStatus => Status;
        public string DisplayHotkey => string.IsNullOrWhiteSpace(SecureHotkey)
            ? "Secure hotkey: not set"
            : $"Secure hotkey: {SecureHotkey}";

        partial void OnStatusChanged(string value)
        {
            OnPropertyChanged(nameof(DisplayStatus));
        }

        partial void OnSecureHotkeyChanged(string value)
        {
            OnPropertyChanged(nameof(DisplayHotkey));
        }

        partial void OnExecutablePathChanged(string value)
        {
            OnPropertyChanged(nameof(Icon));
        }

        partial void OnAppIconChanged(System.Windows.Media.ImageSource? value)
        {
            OnPropertyChanged(nameof(Icon));
        }
    }
}
