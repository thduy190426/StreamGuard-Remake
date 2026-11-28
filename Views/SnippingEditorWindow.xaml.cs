using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using StreamGuard.Localization;
using StreamGuard.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace StreamGuard.Views
{
    public partial class SnippingEditorWindow : Window
    {
        private enum EditorToolMode
        {
            Crop,
            BlurRegion
        }

        private sealed record EditorHistoryEntry(Bitmap Image, bool IsFullBlur);

        private const int MaxHistoryDepth = 15;
        public const int MaxFullBlurApplications = 3;
        public const long FullBlurCooldownMs = 800;
        public const double MinZoomFactor = 0.25;
        public const double MaxZoomFactor = 5.0;
        public const double LoupeMagnification = 2.5;

        private readonly Bitmap _originalBitmap;
        private readonly bool _excludeFromCapture;
        private readonly Func<long> _nowProvider;
        private readonly List<EditorHistoryEntry> _undoStack = new();
        private readonly List<EditorHistoryEntry> _redoStack = new();
        private readonly DispatcherTimer _warningTimer = new() { Interval = TimeSpan.FromSeconds(3) };

        private Bitmap _currentBitmap;
        private bool _currentIsFullBlur;
        private long _lastFullBlurTick;
        private EditorToolMode _toolMode = EditorToolMode.Crop;
        private double _zoomFactor = 1.0;
        private bool _isDragging;
        private bool _isPanning;
        private bool _hasPendingCrop;
        private Point _dragStart;
        private Point _panStartPoint;
        private double _panStartHOffset;
        private double _panStartVOffset;
        private Rectangle _pendingCropPixelRect;
        private bool _isUpdatingResizeInputs;
        private bool _isFinished;

        public event Action<Bitmap>? SaveRequested;
        public event Action<Bitmap>? CopyOnlyRequested;
        public event Action? Canceled;

        public int CurrentFullBlurCount => _undoStack.Count(e => e.IsFullBlur) + (_currentIsFullBlur ? 1 : 0);
        public double CurrentZoomFactor => _zoomFactor;

        public SnippingEditorWindow(Bitmap initialBitmap, bool excludeFromCapture)
            : this(initialBitmap, excludeFromCapture, null)
        {
        }

        internal SnippingEditorWindow(Bitmap initialBitmap, bool excludeFromCapture, Func<long>? nowProvider)
        {
            InitializeComponent();

            _originalBitmap = ScreenshotImageProcessor.CloneAsArgb32(initialBitmap ?? throw new ArgumentNullException(nameof(initialBitmap)));
            _currentBitmap = ScreenshotImageProcessor.CloneAsArgb32(_originalBitmap);
            _excludeFromCapture = excludeFromCapture;
            _nowProvider = nowProvider ?? (() => Environment.TickCount64);

            LoupeVisualBrush.Visual = PreviewImage;
            _warningTimer.Tick += (_, _) =>
            {
                _warningTimer.Stop();
                if (TxtEditorWarning != null)
                    TxtEditorWarning.Visibility = Visibility.Collapsed;
            };

            SourceInitialized += OnSourceInitialized;
            Loaded += (_, _) => UpdateZoomLayout();
            PreviewKeyDown += OnPreviewKeyDown;
            Closed += OnWindowClosed;

            RefreshPreviewAndInfo();
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            if (!_excludeFromCapture) return;
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                new WindowCaptureProtectionService().ApplyAndVerify(hwnd);
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (_hasPendingCrop)
                {
                    ClearSelectionVisual();
                    e.Handled = true;
                    return;
                }
                CancelEditor();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter && _hasPendingCrop && _toolMode == EditorToolMode.Crop)
            {
                ApplyPendingCrop();
                e.Handled = true;
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (e.Key == Key.Z) { PerformUndo(); e.Handled = true; }
                else if (e.Key == Key.Y) { PerformRedo(); e.Handled = true; }
                else if (e.Key == Key.S) { FinishWithSave(); e.Handled = true; }
                else if (e.Key == Key.C) { FinishWithCopyOnly(); e.Handled = true; }
                else if (e.Key == Key.Add || e.Key == Key.OemPlus) { SetZoomFactor(_zoomFactor * 1.2); e.Handled = true; }
                else if (e.Key == Key.Subtract || e.Key == Key.OemMinus) { SetZoomFactor(_zoomFactor / 1.2); e.Handled = true; }
                else if (e.Key == Key.D0 || e.Key == Key.NumPad0) { SetZoomFactor(1.0); e.Handled = true; }
            }
        }

        private void RefreshPreviewAndInfo()
        {
            PreviewImage.Source = SnippingToolService.ConvertBitmapToBitmapSource(_currentBitmap);

            _isUpdatingResizeInputs = true;
            TxtResizeWidth.Text = _currentBitmap.Width.ToString();
            TxtResizeHeight.Text = _currentBitmap.Height.ToString();
            _isUpdatingResizeInputs = false;

            BtnUndo.IsEnabled = _undoStack.Count > 0;
            BtnRedo.IsEnabled = _redoStack.Count > 0;
            UpdateBlurAllButtonUi();
            UpdateZoomLayout();
            UpdateInfoText();
        }

        private void UpdateBlurAllButtonUi()
        {
            if (TxtBlurAllLabel == null || BtnBlurAll == null) return;
            var baseLabel = LocalizationService.GetString("SnippingEditor.BlurAll", "Làm mờ toàn ảnh");
            var count = CurrentFullBlurCount;
            TxtBlurAllLabel.Text = count > 0
                ? $"{baseLabel} ({count}/{MaxFullBlurApplications})"
                : baseLabel;
            BtnBlurAll.Opacity = count >= MaxFullBlurApplications ? 0.6 : 1.0;
        }

        private void ShowWarning(string message)
        {
            if (TxtEditorWarning == null) return;
            TxtEditorWarning.Text = message;
            TxtEditorWarning.Visibility = Visibility.Visible;
            _warningTimer.Stop();
            _warningTimer.Start();
        }

        private void ClearWarning()
        {
            _warningTimer.Stop();
            if (TxtEditorWarning != null)
                TxtEditorWarning.Visibility = Visibility.Collapsed;
        }

        private void UpdateInfoText()
        {
            if (TxtEditorInfo == null) return;
            var zoomPct = (int)Math.Round(_zoomFactor * 100);
            var baseInfo = $"{_currentBitmap.Width} × {_currentBitmap.Height} px  |  {zoomPct}%";
            if (_hasPendingCrop)
            {
                var selText = LocalizationService.GetString("SnippingEditor.SelectionInfo", "Vùng chọn");
                TxtEditorInfo.Text = $"{baseInfo}  |  {selText}: {_pendingCropPixelRect.Width} × {_pendingCropPixelRect.Height} px";
            }
            else
            {
                TxtEditorInfo.Text = baseInfo;
            }
        }

        private void PushHistoryAndReplaceCurrent(Bitmap nextBitmap, bool isFullBlur = false)
        {
            _undoStack.Add(new EditorHistoryEntry(_currentBitmap, _currentIsFullBlur));
            if (_undoStack.Count > MaxHistoryDepth)
            {
                _undoStack[0].Image.Dispose();
                _undoStack.RemoveAt(0);
            }

            foreach (var redo in _redoStack)
                redo.Image.Dispose();
            _redoStack.Clear();

            _currentBitmap = nextBitmap;
            _currentIsFullBlur = isFullBlur;
            ClearSelectionVisual();
            RefreshPreviewAndInfo();
        }

        internal void PerformUndo()
        {
            if (_undoStack.Count == 0) return;
            _redoStack.Add(new EditorHistoryEntry(_currentBitmap, _currentIsFullBlur));
            var prev = _undoStack[^1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            _currentBitmap = prev.Image;
            _currentIsFullBlur = prev.IsFullBlur;
            ClearWarning();
            ClearSelectionVisual();
            RefreshPreviewAndInfo();
        }

        internal void PerformRedo()
        {
            if (_redoStack.Count == 0) return;
            _undoStack.Add(new EditorHistoryEntry(_currentBitmap, _currentIsFullBlur));
            var next = _redoStack[^1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            _currentBitmap = next.Image;
            _currentIsFullBlur = next.IsFullBlur;
            ClearSelectionVisual();
            RefreshPreviewAndInfo();
        }

        private void ToolMode_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            _toolMode = RadioBlurMode.IsChecked == true ? EditorToolMode.BlurRegion : EditorToolMode.Crop;
            ClearSelectionVisual();
        }

        private void ClearSelectionVisual()
        {
            _hasPendingCrop = false;
            _pendingCropPixelRect = Rectangle.Empty;
            if (SelectionRectVisual != null) SelectionRectVisual.Visibility = Visibility.Collapsed;
            if (BtnApplyCrop != null) BtnApplyCrop.IsEnabled = false;
            UpdateInfoText();
        }

        internal static double ClampZoomFactor(double factor) => Math.Clamp(factor, MinZoomFactor, MaxZoomFactor);

        private void SetZoomFactor(double newFactor, Point? anchorInViewport = null)
        {
            var clamped = ClampZoomFactor(newFactor);
            if (Math.Abs(clamped - _zoomFactor) < 0.001) return;

            double relX = 0.5;
            double relY = 0.5;
            if (anchorInViewport.HasValue && ZoomContainer.ActualWidth > 0 && ZoomContainer.ActualHeight > 0)
            {
                var ptOnZoom = ImageScrollViewer.TranslatePoint(anchorInViewport.Value, ZoomContainer);
                relX = Math.Clamp(ptOnZoom.X / ZoomContainer.ActualWidth, 0, 1);
                relY = Math.Clamp(ptOnZoom.Y / ZoomContainer.ActualHeight, 0, 1);
            }

            _zoomFactor = clamped;
            UpdateZoomLayout();

            if (anchorInViewport.HasValue)
            {
                ImageScrollViewer.UpdateLayout();
                double targetH = relX * ZoomContainer.Width - anchorInViewport.Value.X;
                double targetV = relY * ZoomContainer.Height - anchorInViewport.Value.Y;
                ImageScrollViewer.ScrollToHorizontalOffset(Math.Max(0, targetH));
                ImageScrollViewer.ScrollToVerticalOffset(Math.Max(0, targetV));
            }

            UpdateInfoText();
        }

        private void UpdateZoomLayout()
        {
            if (ZoomContainer == null || ImageScrollViewer == null || _currentBitmap == null) return;

            double vpW = ImageScrollViewer.ViewportWidth > 20 ? ImageScrollViewer.ViewportWidth - 8 : 880;
            double vpH = ImageScrollViewer.ViewportHeight > 20 ? ImageScrollViewer.ViewportHeight - 8 : 460;

            double fitScale = Math.Min(vpW / _currentBitmap.Width, vpH / _currentBitmap.Height);
            if (fitScale <= 0 || double.IsNaN(fitScale) || double.IsInfinity(fitScale))
                fitScale = 1.0;

            double baseW = Math.Max(16, _currentBitmap.Width * fitScale);
            double baseH = Math.Max(16, _currentBitmap.Height * fitScale);

            ZoomContainer.Width = baseW * _zoomFactor;
            ZoomContainer.Height = baseH * _zoomFactor;

            if (TxtZoomPercent != null)
                TxtZoomPercent.Text = $"{(int)Math.Round(_zoomFactor * 100)}%";

            ClearSelectionVisual();
        }

        private void ImageScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateZoomLayout();

        private void ImageScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            double step = e.Delta > 0 ? 1.15 : (1.0 / 1.15);
            var anchor = e.GetPosition(ImageScrollViewer);
            SetZoomFactor(_zoomFactor * step, anchor);
        }

        private void BtnZoomOut_Click(object sender, RoutedEventArgs e) => SetZoomFactor(_zoomFactor / 1.25);
        private void BtnZoomIn_Click(object sender, RoutedEventArgs e) => SetZoomFactor(_zoomFactor * 1.25);
        private void BtnZoomReset_Click(object sender, RoutedEventArgs e) => SetZoomFactor(1.0);

        private void ToggleMagnifier_Changed(object sender, RoutedEventArgs e)
        {
            if (LoupeBorder != null && ToggleMagnifier?.IsChecked != true)
                LoupeBorder.Visibility = Visibility.Collapsed;
        }

        private void UpdateLoupeAt(Point posOnImage)
        {
            if (ToggleMagnifier?.IsChecked != true || LoupeBorder == null || LoupeCanvas == null)
                return;

            double imgW = PreviewImage.ActualWidth;
            double imgH = PreviewImage.ActualHeight;
            if (imgW <= 0 || imgH <= 0)
            {
                LoupeBorder.Visibility = Visibility.Collapsed;
                return;
            }

            double lensSize = LoupeBorder.Width;
            double viewSize = lensSize / LoupeMagnification;
            LoupeVisualBrush.Viewbox = new Rect(
                posOnImage.X - viewSize / 2.0,
                posOnImage.Y - viewSize / 2.0,
                viewSize,
                viewSize);

            var posInCanvas = PreviewImage.TranslatePoint(posOnImage, LoupeCanvas);
            double left = Math.Clamp(posInCanvas.X + 18, 0, Math.Max(0, LoupeCanvas.ActualWidth - lensSize));
            double top = Math.Clamp(posInCanvas.Y - lensSize - 12, 0, Math.Max(0, LoupeCanvas.ActualHeight - lensSize));
            if (posInCanvas.Y - lensSize - 12 < 0)
            {
                top = Math.Clamp(posInCanvas.Y + 18, 0, Math.Max(0, LoupeCanvas.ActualHeight - lensSize));
            }

            Canvas.SetLeft(LoupeBorder, left);
            Canvas.SetTop(LoupeBorder, top);
            LoupeBorder.Visibility = Visibility.Visible;
        }

        private Point ClampToImageBounds(Point pt)
        {
            double w = PreviewImage.ActualWidth;
            double h = PreviewImage.ActualHeight;
            return new Point(Math.Clamp(pt.X, 0, Math.Max(0, w)), Math.Clamp(pt.Y, 0, Math.Max(0, h)));
        }

        private Rectangle ConvertImageControlRectToPixelRect(Point p1, Point p2)
        {
            double dispW = PreviewImage.ActualWidth;
            double dispH = PreviewImage.ActualHeight;
            if (dispW <= 0 || dispH <= 0) return Rectangle.Empty;

            double x = Math.Min(p1.X, p2.X);
            double y = Math.Min(p1.Y, p2.Y);
            double w = Math.Abs(p2.X - p1.X);
            double h = Math.Abs(p2.Y - p1.Y);

            double scaleX = _currentBitmap.Width / dispW;
            double scaleY = _currentBitmap.Height / dispH;

            int px = (int)Math.Round(x * scaleX);
            int py = (int)Math.Round(y * scaleY);
            int pw = (int)Math.Round(w * scaleX);
            int ph = (int)Math.Round(h * scaleY);

            var rect = new Rectangle(px, py, pw, ph);
            rect.Intersect(new Rectangle(0, 0, _currentBitmap.Width, _currentBitmap.Height));
            return rect;
        }

        private void UpdateSelectionVisual(Point p1, Point p2)
        {
            var topLeft = PreviewImage.TranslatePoint(new Point(Math.Min(p1.X, p2.X), Math.Min(p1.Y, p2.Y)), ZoomContainer);
            Canvas.SetLeft(SelectionRectVisual, topLeft.X);
            Canvas.SetTop(SelectionRectVisual, topLeft.Y);
            SelectionRectVisual.Width = Math.Abs(p2.X - p1.X);
            SelectionRectVisual.Height = Math.Abs(p2.Y - p1.Y);
            SelectionRectVisual.Visibility = Visibility.Visible;
        }

        private void PreviewImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _dragStart = ClampToImageBounds(e.GetPosition(PreviewImage));
            PreviewImage.CaptureMouse();
            UpdateSelectionVisual(_dragStart, _dragStart);
        }

        private void PreviewImage_MouseMove(object sender, MouseEventArgs e)
        {
            var current = ClampToImageBounds(e.GetPosition(PreviewImage));
            UpdateLoupeAt(current);

            if (_isPanning)
            {
                var pt = e.GetPosition(ImageScrollViewer);
                ImageScrollViewer.ScrollToHorizontalOffset(_panStartHOffset - (pt.X - _panStartPoint.X));
                ImageScrollViewer.ScrollToVerticalOffset(_panStartVOffset - (pt.Y - _panStartPoint.Y));
                return;
            }

            if (!_isDragging) return;
            UpdateSelectionVisual(_dragStart, current);
            _pendingCropPixelRect = ConvertImageControlRectToPixelRect(_dragStart, current);
            _hasPendingCrop = _pendingCropPixelRect.Width >= ScreenshotImageProcessor.MinDimension &&
                              _pendingCropPixelRect.Height >= ScreenshotImageProcessor.MinDimension;
            UpdateInfoText();
        }

        private void PreviewImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;
            _isDragging = false;
            PreviewImage.ReleaseMouseCapture();

            var end = ClampToImageBounds(e.GetPosition(PreviewImage));
            var pixelRect = ConvertImageControlRectToPixelRect(_dragStart, end);

            if (pixelRect.Width < ScreenshotImageProcessor.MinDimension || pixelRect.Height < ScreenshotImageProcessor.MinDimension)
            {
                ClearSelectionVisual();
                return;
            }

            if (_toolMode == EditorToolMode.BlurRegion)
            {
                int strength = (int)Math.Round(SliderBlurStrength.Value);
                bool mosaic = ChkMosaicBlur.IsChecked == true;
                var blurred = ScreenshotImageProcessor.ApplyRegionBlur(_currentBitmap, pixelRect, strength, mosaic);
                PushHistoryAndReplaceCurrent(blurred);
            }
            else
            {
                _pendingCropPixelRect = pixelRect;
                _hasPendingCrop = true;
                BtnApplyCrop.IsEnabled = true;
                UpdateInfoText();
            }
        }

        private void PreviewImage_MouseLeave(object sender, MouseEventArgs e)
        {
            if (LoupeBorder != null)
                LoupeBorder.Visibility = Visibility.Collapsed;
        }

        private void PreviewImage_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_zoomFactor <= 1.0) return;
            _isPanning = true;
            _panStartPoint = e.GetPosition(ImageScrollViewer);
            _panStartHOffset = ImageScrollViewer.HorizontalOffset;
            _panStartVOffset = ImageScrollViewer.VerticalOffset;
            PreviewImage.CaptureMouse();
            PreviewImage.Cursor = System.Windows.Input.Cursors.Hand;
        }

        private void PreviewImage_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isPanning) return;
            _isPanning = false;
            PreviewImage.ReleaseMouseCapture();
            PreviewImage.Cursor = System.Windows.Input.Cursors.Cross;
        }

        private void ApplyPendingCrop()
        {
            if (!_hasPendingCrop) return;
            if (_pendingCropPixelRect.Width < ScreenshotImageProcessor.MinDimension ||
                _pendingCropPixelRect.Height < ScreenshotImageProcessor.MinDimension)
                return;

            var cropped = ScreenshotImageProcessor.Crop(_currentBitmap, _pendingCropPixelRect);
            PushHistoryAndReplaceCurrent(cropped);
        }

        private void BtnApplyCrop_Click(object sender, RoutedEventArgs e) => ApplyPendingCrop();
        private void BtnRotateLeft_Click(object sender, RoutedEventArgs e) => PushHistoryAndReplaceCurrent(ScreenshotImageProcessor.RotateFlip(_currentBitmap, RotateFlipType.Rotate270FlipNone));
        private void BtnRotateRight_Click(object sender, RoutedEventArgs e) => PushHistoryAndReplaceCurrent(ScreenshotImageProcessor.RotateFlip(_currentBitmap, RotateFlipType.Rotate90FlipNone));
        private void BtnFlipH_Click(object sender, RoutedEventArgs e) => PushHistoryAndReplaceCurrent(ScreenshotImageProcessor.RotateFlip(_currentBitmap, RotateFlipType.RotateNoneFlipX));
        private void BtnFlipV_Click(object sender, RoutedEventArgs e) => PushHistoryAndReplaceCurrent(ScreenshotImageProcessor.RotateFlip(_currentBitmap, RotateFlipType.RotateNoneFlipY));
        private void BtnUndo_Click(object sender, RoutedEventArgs e) => PerformUndo();
        private void BtnRedo_Click(object sender, RoutedEventArgs e) => PerformRedo();

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            ClearWarning();
            var reset = ScreenshotImageProcessor.CloneAsArgb32(_originalBitmap);
            PushHistoryAndReplaceCurrent(reset, isFullBlur: false);
        }

        private void TxtResizeWidth_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingResizeInputs || ChkLockAspect?.IsChecked != true || _currentBitmap == null) return;
            if (int.TryParse(TxtResizeWidth.Text, out int w) && w > 0)
            {
                _isUpdatingResizeInputs = true;
                int h = Math.Max(ScreenshotImageProcessor.MinDimension, (int)Math.Round(w * (_currentBitmap.Height / (double)_currentBitmap.Width)));
                TxtResizeHeight.Text = h.ToString();
                _isUpdatingResizeInputs = false;
            }
        }

        private void TxtResizeHeight_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingResizeInputs || ChkLockAspect?.IsChecked != true || _currentBitmap == null) return;
            if (int.TryParse(TxtResizeHeight.Text, out int h) && h > 0)
            {
                _isUpdatingResizeInputs = true;
                int w = Math.Max(ScreenshotImageProcessor.MinDimension, (int)Math.Round(h * (_currentBitmap.Width / (double)_currentBitmap.Height)));
                TxtResizeWidth.Text = w.ToString();
                _isUpdatingResizeInputs = false;
            }
        }

        private void ApplyScalePreset(double factor)
        {
            int w = Math.Max(ScreenshotImageProcessor.MinDimension, (int)Math.Round(_currentBitmap.Width * factor));
            int h = Math.Max(ScreenshotImageProcessor.MinDimension, (int)Math.Round(_currentBitmap.Height * factor));
            PushHistoryAndReplaceCurrent(ScreenshotImageProcessor.Resize(_currentBitmap, w, h));
        }

        private void BtnScale75_Click(object sender, RoutedEventArgs e) => ApplyScalePreset(0.75);
        private void BtnScale50_Click(object sender, RoutedEventArgs e) => ApplyScalePreset(0.50);

        private void BtnApplyResize_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(TxtResizeWidth.Text, out int w) || !int.TryParse(TxtResizeHeight.Text, out int h))
                return;

            w = Math.Clamp(w, ScreenshotImageProcessor.MinDimension, ScreenshotImageProcessor.MaxDimension);
            h = Math.Clamp(h, ScreenshotImageProcessor.MinDimension, ScreenshotImageProcessor.MaxDimension);
            if (w == _currentBitmap.Width && h == _currentBitmap.Height) return;

            PushHistoryAndReplaceCurrent(ScreenshotImageProcessor.Resize(_currentBitmap, w, h));
        }

        internal bool TryApplyFullBlur(int? strengthOverride = null, bool? mosaicOverride = null)
        {
            if (CurrentFullBlurCount >= MaxFullBlurApplications)
            {
                var fmt = LocalizationService.GetString(
                    "SnippingEditor.BlurLimitReached",
                    "Đã đạt mức làm mờ toàn ảnh tối đa ({0} lần). Hãy bấm Hoàn tác hoặc Ảnh gốc để tiếp tục.");
                ShowWarning(string.Format(fmt, MaxFullBlurApplications));
                return false;
            }

            var now = _nowProvider();
            if (_lastFullBlurTick != 0 && (now - _lastFullBlurTick) < FullBlurCooldownMs)
            {
                ShowWarning(LocalizationService.GetString(
                    "SnippingEditor.BlurCooldownWarning",
                    "Thao tác quá nhanh! Vui lòng đợi giây lát trước khi làm mờ tiếp."));
                return false;
            }

            _lastFullBlurTick = now;
            ClearWarning();
            int strength = strengthOverride ?? (int)Math.Round(SliderBlurStrength.Value);
            bool mosaic = mosaicOverride ?? (ChkMosaicBlur.IsChecked == true);
            PushHistoryAndReplaceCurrent(ScreenshotImageProcessor.ApplyFullBlur(_currentBitmap, strength, mosaic), isFullBlur: true);
            return true;
        }

        private void BtnBlurAll_Click(object sender, RoutedEventArgs e) => TryApplyFullBlur();

        private void FinishWithSave()
        {
            if (_isFinished) return;
            _isFinished = true;
            var export = ScreenshotImageProcessor.CloneAsArgb32(_currentBitmap);
            SaveRequested?.Invoke(export);
            Close();
        }

        private void FinishWithCopyOnly()
        {
            if (_isFinished) return;
            _isFinished = true;
            var export = ScreenshotImageProcessor.CloneAsArgb32(_currentBitmap);
            CopyOnlyRequested?.Invoke(export);
            Close();
        }

        private void CancelEditor()
        {
            if (_isFinished) return;
            _isFinished = true;
            Canceled?.Invoke();
            Close();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e) => FinishWithSave();
        private void BtnCopyOnly_Click(object sender, RoutedEventArgs e) => FinishWithCopyOnly();
        private void BtnCancel_Click(object sender, RoutedEventArgs e) => CancelEditor();

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            _warningTimer.Stop();
            if (!_isFinished)
            {
                _isFinished = true;
                Canceled?.Invoke();
            }

            foreach (var item in _undoStack) item.Image.Dispose();
            _undoStack.Clear();
            foreach (var item in _redoStack) item.Image.Dispose();
            _redoStack.Clear();
            if (PreviewImage != null) PreviewImage.Source = null;
            if (LoupeVisualBrush != null) LoupeVisualBrush.Visual = null;
            _currentBitmap.Dispose();
            _originalBitmap.Dispose();
        }
    }
}
