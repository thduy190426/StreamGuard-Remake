using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StreamGuard.Helpers
{
    public static class IconHelper
    {
        private static readonly ConcurrentDictionary<string, ImageSource?> _cacheByPath = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, ImageSource?> _cacheByName = new(StringComparer.OrdinalIgnoreCase);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam,
            uint flags, uint timeout, out IntPtr result);

        private static IntPtr ReadWindowIcon(IntPtr window, int kind) =>
            SendMessageTimeout(window, WM_GETICON, new IntPtr(kind), IntPtr.Zero, 0x0002, 150, out var result) != IntPtr.Zero
                ? result : IntPtr.Zero;

        [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW", SetLastError = true)]
        private static extern IntPtr GetClassLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetClassLongW", SetLastError = true)]
        private static extern int GetClassLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private const uint WM_GETICON = 0x007F;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;
        private const int ICON_SMALL2 = 2;
        private const int GCLP_HICON = -14;
        private const int GCLP_HICONSM = -34;

        private static IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8 ? GetClassLongPtr64(hWnd, nIndex) : new IntPtr(GetClassLong32(hWnd, nIndex));
        }

        public static ImageSource? GetIconForApp(string exePath, string processName, IntPtr hWnd = default)
        {
            if (!string.IsNullOrWhiteSpace(exePath) && _cacheByPath.TryGetValue(exePath, out var cached))
                return cached;

            if (string.IsNullOrWhiteSpace(exePath) && !string.IsNullOrWhiteSpace(processName) && _cacheByName.TryGetValue(processName, out cached))
                return cached;

            ImageSource? iconSource = null;

            // 1. Try extracting from disk if path exists
            if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
            {
                try
                {
                    using var sysIcon = Icon.ExtractAssociatedIcon(exePath);
                    if (sysIcon != null)
                    {
                        iconSource = ToBitmapSource(sysIcon);
                    }
                }
                catch
                {
                    // Ignore disk read/permission errors
                }
            }

            // 2. Try window handle if available and disk extraction didn't work
            if (iconSource == null && hWnd != IntPtr.Zero)
            {
                iconSource = GetIconFromWindowHandle(hWnd);
            }

            // Store in cache
            if (iconSource != null && !string.IsNullOrWhiteSpace(exePath))
                _cacheByPath[exePath] = iconSource;

            if (iconSource != null && !string.IsNullOrWhiteSpace(processName))
                _cacheByName[processName] = iconSource;

            return iconSource;
        }

        private static ImageSource? GetIconFromWindowHandle(IntPtr hWnd)
        {
            IntPtr hIcon = IntPtr.Zero;
            try
            {
                hIcon = ReadWindowIcon(hWnd, ICON_SMALL2);
                if (hIcon == IntPtr.Zero)
                    hIcon = ReadWindowIcon(hWnd, ICON_SMALL);
                if (hIcon == IntPtr.Zero)
                    hIcon = ReadWindowIcon(hWnd, ICON_BIG);
                if (hIcon == IntPtr.Zero)
                    hIcon = GetClassLongPtr(hWnd, GCLP_HICONSM);
                if (hIcon == IntPtr.Zero)
                    hIcon = GetClassLongPtr(hWnd, GCLP_HICON);

                if (hIcon != IntPtr.Zero)
                {
                    var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                        hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    if (bitmap.CanFreeze) bitmap.Freeze();
                    return bitmap;
                }
            }
            catch
            {
                // Ignore COM/Win32 errors
            }
            finally
            {
                // Note: hIcon from WM_GETICON or GetClassLong should generally not be destroyed
                // if it belongs to the window class, but ToBitmapSource copies the bytes so we're safe.
            }

            return null;
        }

        private static BitmapSource? ToBitmapSource(Icon icon)
        {
            IntPtr hIcon = icon.Handle;
            try
            {
                var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                    hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                if (bitmapSource.CanFreeze)
                    bitmapSource.Freeze();

                return bitmapSource;
            }
            catch
            {
                return null;
            }
        }
    }
}
