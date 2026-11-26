using System;
using System.Runtime.InteropServices;

namespace StreamGuard.Services
{
    // ─── COM Interfaces (undocumented Windows internal, stable từ Win10 1607+) ───

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b")]
    internal interface IVirtualDesktopManager
    {
        bool IsWindowOnCurrentVirtualDesktop(IntPtr hWnd);
        Guid GetWindowDesktopId(IntPtr hWnd);
        void MoveWindowToDesktop(IntPtr hWnd, ref Guid desktopId);
    }

    [ComImport]
    [Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a")]
    internal class VirtualDesktopManagerClass { }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("f31574d6-b682-4cdc-bd56-1827860abec6")]
    internal interface IVirtualDesktopManagerInternal
    {
        int GetCount();
        void MoveViewToDesktop(IntPtr pView, IVirtualDesktop pDesktop);
        bool CanViewMoveDesktops(IntPtr pView);
        IVirtualDesktop GetCurrentDesktop();
        void GetDesktops(out IObjectArray pArray);
        IVirtualDesktop GetAdjacentDesktop(IVirtualDesktop pDesktopReference, int uDirection);
        void SwitchDesktop(IVirtualDesktop pDesktop);
        IVirtualDesktop CreateDesktop();
        void RemoveDesktop(IVirtualDesktop pRemove, IVirtualDesktop pFallbackDesktop);
        IVirtualDesktop FindDesktop(ref Guid desktopId);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("ff72ffdd-be7e-43fc-9c03-ad81681e88e4")]
    internal interface IVirtualDesktop
    {
        bool IsViewVisible(IntPtr pView);
        Guid GetId();
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("92ca9dcd-5622-4bba-a805-5e9f541bd8c9")]
    internal interface IObjectArray
    {
        void GetCount(out int pcObjects);
        void GetAt(int uiIndex, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppvObject);
    }

    [ComImport]
    [Guid("c2f03a33-21f5-47fa-b4bb-156362a2f239")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IImmersiveShell
    {
        [return: MarshalAs(UnmanagedType.Interface)]
        object QueryService(ref Guid guidService, ref Guid riid);
    }

    [ComImport]
    [Guid("c2f03a33-21f5-47fa-b4bb-156362a2f239")]
    internal class ImmersiveShellClass { }

    // ─── Service ───────────────────────────────────────────────────────────────

    public class VirtualDesktopService : IDisposable
    {
        private IVirtualDesktopManager? _manager;
        private IVirtualDesktopManagerInternal? _managerInternal;
        private IVirtualDesktop? _hiddenDesktop;
        private IVirtualDesktop? _initialDesktop;
        private bool _ownsHiddenDesktop;

        private static readonly Guid CLSID_ImmersiveShell = new("c2f03a33-21f5-47fa-b4bb-156362a2f239");
        private static readonly Guid SID_VirtualDesktopManagerInternal = new("c5e0cdca-7b6e-41b2-9fc4-d93975cc467b");
        private static readonly Guid IID_VirtualDesktopManagerInternal = new("f31574d6-b682-4cdc-bd56-1827860abec6");

        public bool IsAvailable { get; private set; }

        internal VirtualDesktopService(IVirtualDesktopManager? manager, IVirtualDesktopManagerInternal? internalManager)
        {
            _manager = manager;
            _managerInternal = internalManager;
            IsAvailable = manager != null && internalManager != null;
        }

        public VirtualDesktopService()
        {
            try
            {
                _manager = (IVirtualDesktopManager)new VirtualDesktopManagerClass();
                IsAvailable = _manager != null;
            }
            catch
            {
                IsAvailable = false;
            }

            if (IsAvailable)
            {
                // Multi-GUID candidate list for internal manager across Win 10 and Win 11 versions
                var guidCandidates = new[]
                {
                    IID_VirtualDesktopManagerInternal,                // Win 10 (f31574d6...)
                    new Guid("b2f925b9-50f4-471a-b6b6-a81d4b684752"), // Win 11 21H2
                    new Guid("a3175f4a-81a6-419b-ab05-5ab9c34a2c07"), // Win 11 22H2/23H2
                    new Guid("53644f70-4b67-462a-ab8b-0477f1d0d95b")  // Win 11 24H2
                };

                try
                {
                    var shell = (IImmersiveShell)new ImmersiveShellClass();
                    var serviceId = SID_VirtualDesktopManagerInternal;
                    foreach (var candidate in guidCandidates)
                    {
                        try
                        {
                            var interfaceId = candidate;
                            var obj = shell.QueryService(ref serviceId, ref interfaceId);
                            if (obj is IVirtualDesktopManagerInternal internalMgr)
                            {
                                _managerInternal = internalMgr;
                                break;
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }
            IsAvailable = _manager != null && _managerInternal != null;
        }

        /// <summary>Lấy hoặc tạo Desktop ẩn (Desktop 2)</summary>
        private IVirtualDesktop? GetOrCreateHiddenDesktop()
        {
            if (_managerInternal == null || !IsAvailable) return null;
            if (_hiddenDesktop != null) return _hiddenDesktop;

            try
            {
                _initialDesktop ??= _managerInternal.GetCurrentDesktop();

                _managerInternal.GetDesktops(out var arr);
                if (arr == null) return null;
                arr.GetCount(out int count);

                if (count >= 2)
                {
                    // Dùng desktop cuối cùng làm hidden desktop
                    var iid = typeof(IVirtualDesktop).GUID;
                    arr.GetAt(count - 1, ref iid, out object obj);
                    _hiddenDesktop = (IVirtualDesktop)obj;
                    _ownsHiddenDesktop = false;
                }
                else
                {
                    _hiddenDesktop = _managerInternal.CreateDesktop();
                    _ownsHiddenDesktop = true;
                }
                return _hiddenDesktop;
            }
            catch
            {
                IsAvailable = false;
                return null;
            }
        }

        /// <summary>Di chuyển cửa sổ sang Desktop ẩn</summary>
        public bool MoveToHidden(IntPtr hWnd, out Guid originalDesktopId)
        {
            originalDesktopId = Guid.Empty;
            if (!ProcessSafetyService.Default.CanManageWindow(hWnd)) return false;
            if (_manager == null || !IsAvailable) return false;

            try
            {
                originalDesktopId = _manager.GetWindowDesktopId(hWnd);
                var hidden = GetOrCreateHiddenDesktop();
                if (hidden == null) return false;

                var hiddenId = hidden.GetId();
                _manager.MoveWindowToDesktop(hWnd, ref hiddenId);
                return true;
            }
            catch
            {
                IsAvailable = false;
                return false;
            }
        }

        /// <summary>Trả cửa sổ về Desktop gốc</summary>
        public bool MoveToDesktop(IntPtr hWnd, Guid desktopId)
        {
            if (!ProcessSafetyService.Default.CanManageWindow(hWnd)) return false;
            if (_manager == null) return false;
            try
            {
                _manager.MoveWindowToDesktop(hWnd, ref desktopId);
                return true;
            }
            catch { return false; }
        }

        public Guid? GetWindowDesktopId(IntPtr window)
        {
            if (_manager == null) return null;
            try { var id = _manager.GetWindowDesktopId(window); return id == Guid.Empty ? null : id; }
            catch { return null; }
        }

        /// <summary>Lấy Desktop hiện tại</summary>
        public bool? IsWindowOnCurrentDesktop(IntPtr hWnd)
        {
            if (_manager == null || !IsAvailable) return null;
            try
            {
                return _manager.IsWindowOnCurrentVirtualDesktop(hWnd);
            }
            catch
            {
                return null;
            }
        }

        public Guid GetCurrentDesktopId()
        {
            if (_managerInternal == null || !IsAvailable) return Guid.Empty;
            try { return _managerInternal.GetCurrentDesktop().GetId(); }
            catch
            {
                IsAvailable = false;
                return Guid.Empty;
            }
        }

        /// <summary>Xóa Desktop ẩn sau khi đã restore tất cả app</summary>
        public void RemoveHiddenDesktop()
        {
            if (_managerInternal == null || _hiddenDesktop == null) return;
            if (!_ownsHiddenDesktop)
            {
                _hiddenDesktop = null;
                _initialDesktop = null;
                return;
            }
            try
            {
                var current = _managerInternal.GetCurrentDesktop();
                var fallback = current != null && current.GetId() == _hiddenDesktop.GetId() ? _initialDesktop : current;
                if (fallback == null || fallback.GetId() == _hiddenDesktop.GetId()) return;
                _managerInternal.RemoveDesktop(_hiddenDesktop, fallback);
                _hiddenDesktop = null;
                _initialDesktop = null;
                _ownsHiddenDesktop = false;
            }
            catch
            {
                IsAvailable = false;
                _hiddenDesktop = null;
                _initialDesktop = null;
                _ownsHiddenDesktop = false;
            }
        }

        /// <summary>Chuyển màn hình người dùng sang Desktop ẩn (Desktop 2)</summary>
        public bool SwitchToHiddenDesktop()
        {
            if (_managerInternal == null || !IsAvailable) return false;
            try
            {
                var hidden = GetOrCreateHiddenDesktop();
                if (hidden == null) return false;
                _initialDesktop ??= _managerInternal.GetCurrentDesktop();
                _managerInternal.SwitchDesktop(hidden);
                return true;
            }
            catch
            {
                IsAvailable = false;
                return false;
            }
        }

        /// <summary>Chuyển màn hình người dùng về Desktop ban đầu (Desktop 1)</summary>
        public bool SwitchToInitialDesktop()
        {
            if (_managerInternal == null || _initialDesktop == null || !IsAvailable) return false;
            try
            {
                _managerInternal.SwitchDesktop(_initialDesktop);
                return true;
            }
            catch
            {
                IsAvailable = false;
                return false;
            }
        }

        /// <summary>Chuyển đổi qua lại giữa Desktop 1 và Desktop ẩn</summary>
        public bool ToggleHiddenDesktop()
        {
            if (_managerInternal == null || !IsAvailable) return false;
            try
            {
                var current = _managerInternal.GetCurrentDesktop();
                var hidden = GetOrCreateHiddenDesktop();
                if (hidden == null) return false;

                if (current != null && current.GetId() == hidden.GetId())
                {
                    if (_initialDesktop != null)
                    {
                        _managerInternal.SwitchDesktop(_initialDesktop);
                        return true;
                    }
                }
                else
                {
                    _initialDesktop = current;
                    _managerInternal.SwitchDesktop(hidden);
                    return true;
                }
                return false;
            }
            catch
            {
                IsAvailable = false;
                return false;
            }
        }

        public void Dispose()
        {
            if (_manager != null && Marshal.IsComObject(_manager)) Marshal.ReleaseComObject(_manager);
            if (_managerInternal != null && Marshal.IsComObject(_managerInternal)) Marshal.ReleaseComObject(_managerInternal);
            _manager = null;
            _managerInternal = null;
            IsAvailable = false;
        }
    }
}
