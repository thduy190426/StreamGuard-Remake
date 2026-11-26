using System;
using System.Drawing;
using System.Windows;

namespace StreamGuard.Services
{
    /// <summary>
    /// Helper utilities for synchronizing DPI matrix and converting coordinates between
    /// WPF Device Independent Pixels (DIPs) and physical device pixels for multi-DPI screens.
    /// </summary>
    public static class SnippingCoordinateHelper
    {
        /// <summary>
        /// Calculates the overlay window bounds in DIPs to fully cover the virtual desktop across all monitors.
        /// </summary>
        public static Rect CalculateOverlayBounds(
            int virtualLeft,
            int virtualTop,
            int virtualWidth,
            int virtualHeight,
            double dpiScaleX,
            double dpiScaleY)
        {
            var scaleX = dpiScaleX > 0 ? dpiScaleX : 1.0;
            var scaleY = dpiScaleY > 0 ? dpiScaleY : 1.0;

            return new Rect(
                virtualLeft / scaleX,
                virtualTop / scaleY,
                Math.Max(0.0, virtualWidth / scaleX),
                Math.Max(0.0, virtualHeight / scaleY));
        }

        /// <summary>
        /// Converts a selection rectangle measured in DIPs (on a WPF Canvas/Window)
        /// to physical pixels matching the screen snapshot bitmap.
        /// Formula:
        /// cropX = (int)Math.Round(selectionInDips.X * dpi.DpiScaleX)
        /// cropY = (int)Math.Round(selectionInDips.Y * dpi.DpiScaleY)
        /// cropW = (int)Math.Round(selectionInDips.Width * dpi.DpiScaleX)
        /// cropH = (int)Math.Round(selectionInDips.Height * dpi.DpiScaleY)
        /// </summary>
        public static Rectangle ConvertDipsToPhysicalPixels(
            Rect selectionInDips,
            double dpiScaleX,
            double dpiScaleY)
        {
            var scaleX = dpiScaleX > 0 ? dpiScaleX : 1.0;
            var scaleY = dpiScaleY > 0 ? dpiScaleY : 1.0;

            int cropX = (int)Math.Round(selectionInDips.X * scaleX);
            int cropY = (int)Math.Round(selectionInDips.Y * scaleY);
            int cropW = (int)Math.Round(Math.Max(0.0, selectionInDips.Width) * scaleX);
            int cropH = (int)Math.Round(Math.Max(0.0, selectionInDips.Height) * scaleY);

            return new Rectangle(cropX, cropY, cropW, cropH);
        }

        /// <summary>
        /// Converts a physical pixel rectangle (e.g. from screen dimensions or bitmap coordinates) to DIPs.
        /// </summary>
        public static Rect ConvertPhysicalPixelsToDips(
            Rectangle physicalRect,
            double dpiScaleX,
            double dpiScaleY)
        {
            var scaleX = dpiScaleX > 0 ? dpiScaleX : 1.0;
            var scaleY = dpiScaleY > 0 ? dpiScaleY : 1.0;

            return new Rect(
                physicalRect.X / scaleX,
                physicalRect.Y / scaleY,
                Math.Max(0.0, physicalRect.Width / scaleX),
                Math.Max(0.0, physicalRect.Height / scaleY));
        }
    }
}
