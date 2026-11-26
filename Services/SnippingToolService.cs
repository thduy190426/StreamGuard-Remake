using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace StreamGuard.Services
{
    public sealed record SnippingResult(
        bool Success,
        string? FilePath,
        int Width,
        int Height,
        string Message);

    public sealed class SnippingToolService
    {
        public static string DefaultScreenshotDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "DriverBooster_Screenshots");

        public event Action<SnippingResult>? SnippingCompleted;

        private volatile bool _isSnippingActive;
        public bool IsSnippingActive => _isSnippingActive;

        public bool StartSnipping(
            string? targetDirectory = null,
            bool autoCopyClipboard = true,
            bool excludeFromCapture = true)
        {
            if (_isSnippingActive)
                return false;

            _isSnippingActive = true;

            var directory = string.IsNullOrWhiteSpace(targetDirectory)
                ? DefaultScreenshotDirectory
                : targetDirectory.Trim();

            Bitmap? desktopSnapshot = null;
            try
            {
                // 1. Capture the entire virtual desktop before showing the overlay window.
                // This ensures the captured background contains ZERO overlay artifacts and is completely crisp.
                var vLeft = System.Windows.Forms.SystemInformation.VirtualScreen.Left;
                var vTop = System.Windows.Forms.SystemInformation.VirtualScreen.Top;
                var vWidth = System.Windows.Forms.SystemInformation.VirtualScreen.Width;
                var vHeight = System.Windows.Forms.SystemInformation.VirtualScreen.Height;

                if (vWidth <= 0 || vHeight <= 0)
                {
                    _isSnippingActive = false;
                    SnippingCompleted?.Invoke(new SnippingResult(false, null, 0, 0, "No screen available to capture"));
                    return false;
                }

                desktopSnapshot = new Bitmap(vWidth, vHeight, PixelFormat.Format32bppRgb);
                using (var g = Graphics.FromImage(desktopSnapshot))
                {
                    g.CopyFromScreen(vLeft, vTop, 0, 0, new System.Drawing.Size(vWidth, vHeight), CopyPixelOperation.SourceCopy);
                }

                // 2. Create and display the overlay window.
                var overlay = new Views.SnippingOverlayWindow(desktopSnapshot, vLeft, vTop, vWidth, vHeight, excludeFromCapture);

                overlay.RegionSelected += (cropRect) =>
                {
                    try
                    {
                        var result = ProcessSelectedRegion(desktopSnapshot, cropRect, directory, autoCopyClipboard);
                        SnippingCompleted?.Invoke(result);
                    }
                    catch (Exception ex)
                    {
                        SnippingCompleted?.Invoke(new SnippingResult(false, null, 0, 0, $"Error saving screenshot: {ex.Message}"));
                    }
                    finally
                    {
                        desktopSnapshot?.Dispose();
                        _isSnippingActive = false;
                    }
                };

                overlay.Canceled += () =>
                {
                    desktopSnapshot?.Dispose();
                    _isSnippingActive = false;
                    SnippingCompleted?.Invoke(new SnippingResult(false, null, 0, 0, "Screenshot capture canceled"));
                };

                overlay.Show();
                return true;
            }
            catch (Exception ex)
            {
                desktopSnapshot?.Dispose();
                _isSnippingActive = false;
                SnippingCompleted?.Invoke(new SnippingResult(false, null, 0, 0, $"Could not start snipping: {ex.Message}"));
                return false;
            }
        }

        internal static SnippingResult ProcessSelectedRegion(
            Bitmap fullSnapshot,
            System.Drawing.Rectangle cropRect,
            string targetDirectory,
            bool autoCopyClipboard)
        {
            // Calculate exact mathematical intersection with bitmap boundaries
            var fullBounds = new System.Drawing.Rectangle(0, 0, fullSnapshot.Width, fullSnapshot.Height);
            cropRect.Intersect(fullBounds);

            if (cropRect.Width < 4 || cropRect.Height < 4)
                return new SnippingResult(false, null, 0, 0, "Selected area was too small");

            using var cropped = fullSnapshot.Clone(cropRect, fullSnapshot.PixelFormat);

            // Ensure destination directory exists
            Directory.CreateDirectory(targetDirectory);

            var fileName = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            var fullPath = Path.Combine(targetDirectory, fileName);

            // Avoid collision if rapidly captured within same second
            var counter = 1;
            while (File.Exists(fullPath))
            {
                fileName = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}_{counter++}.png";
                fullPath = Path.Combine(targetDirectory, fileName);
            }

            cropped.Save(fullPath, ImageFormat.Png);

            if (autoCopyClipboard)
            {
                TrySetClipboardImage(cropped);
            }

            return new SnippingResult(true, fullPath, cropRect.Width, cropRect.Height, fileName);
        }

        public static BitmapSource? ConvertBitmapToBitmapSource(Bitmap? bitmap)
        {
            if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0)
                return null;

            var hBitmap = IntPtr.Zero;
            try
            {
                hBitmap = bitmap.GetHbitmap();
                if (hBitmap == IntPtr.Zero)
                    return null;

                var wpfBitmap = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                wpfBitmap.Freeze();
                return wpfBitmap;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (hBitmap != IntPtr.Zero)
                {
                    DeleteObject(hBitmap);
                }
            }
        }

        private static void TrySetClipboardImage(Bitmap image)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var wpfBitmap = ConvertBitmapToBitmapSource(image);
                    if (wpfBitmap != null)
                    {
                        System.Windows.Clipboard.SetImage(wpfBitmap);
                        return;
                    }
                }
                catch (ExternalException)
                {
                    if (attempt < 2)
                        System.Threading.Thread.Sleep(25);
                }
                catch
                {
                    // Non-fatal if clipboard is unavailable
                    break;
                }
            }
        }

        [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}
