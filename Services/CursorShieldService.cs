using System;
using System.Runtime.InteropServices;

namespace StreamGuard.Services
{
    public sealed class CursorShieldService : IDisposable
    {
        public const uint SPI_SETCURSORS = 0x0057;
        public const uint SPIF_UPDATEINIFILE = 0x01;
        public const uint SPIF_SENDCHANGE = 0x02;

        private const int OcrNormal = 32512;

        private static readonly int[] CursorIds =
        {
            32512, // arrow
            32513, // i-beam
            32514, // wait
            32515, // cross
            32516, // up
            32640, // size
            32642, // size nw/se
            32643, // size ne/sw
            32644, // size w/e
            32645, // size n/s
            32646, // size all
            32648, // no
            32649, // hand
            32650, // app starting
            32671, // pin
            32672  // person
        };

        private bool _active;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CopyIcon(IntPtr hIcon);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetSystemCursor(IntPtr hcur, int id);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

        public bool IsActive => _active;

        public void Activate()
        {
            if (_active)
                return;

            var arrow = LoadCursor(IntPtr.Zero, OcrNormal);
            if (arrow == IntPtr.Zero)
                return;

            var changedAny = false;
            foreach (var cursorId in CursorIds)
            {
                var copy = CopyIcon(arrow);
                if (copy == IntPtr.Zero)
                    continue;

                if (SetSystemCursor(copy, cursorId))
                {
                    changedAny = true;
                }
                else
                {
                    DestroyIcon(copy);
                }
            }

            _active = changedAny;
        }

        public void Deactivate(bool restoreSystemCursors = true)
        {
            if (!_active)
                return;

            _active = false;
            if (restoreSystemCursors)
            {
                RestoreAllCursors();
            }
        }

        public void Dispose()
        {
            Deactivate();
        }

        public static bool RestoreAllCursors()
        {
            try
            {
                // Win32 SPI_SETCURSORS (0x0057) reloads cursors from theme into memory.
                // Call SystemParametersInfo with SPIF_SENDCHANGE | SPIF_UPDATEINIFILE as specified.
                // If user32 rejects UPDATEINIFILE (returns false), fallback to SPIF_SENDCHANGE (0x02) then SpifNone (0).
                var success = SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, SPIF_SENDCHANGE | SPIF_UPDATEINIFILE);
                if (!success)
                {
                    success = SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, SPIF_SENDCHANGE);
                }
                if (!success)
                {
                    success = SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, 0);
                }
                return success;
            }
            catch
            {
                return false;
            }
        }
    }
}
