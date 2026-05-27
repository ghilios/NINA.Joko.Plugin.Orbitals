#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NINA.Joko.Plugin.Orbitals.View {

    /// <summary>
    /// Three-layer canvas for the Orbital Framing Wizard.
    ///
    /// Layer 1 — Background sky survey (BackgroundImageSource).
    /// Layer 2 — Captured image displayed in its native sensor frame (no rotation applied).
    ///           The plate-solved CapturedImageRotation is exposed as a DP pass-through for
    ///           the VM's final-PA math but is intentionally NOT applied to the visual.
    ///           Layer is sized from CapturedImagePixelWidth/Height so its on-canvas
    ///           footprint matches the source aspect exactly.
    /// Layer 3 — Draggable/wheel-rotatable yellow framing rectangle. Sized to the same
    ///           on-canvas footprint as the captured-image layer.
    ///
    /// Pan: left-drag on the rectangle updates RectangleOffsetX / RectangleOffsetY,
    /// expressed in captured-image pixels (sensor frame).
    /// Rotate: mouse-wheel on the rectangle updates RectangleRotation, interpreted by
    /// the VM as a delta relative to the plate-solved PA.
    /// Right-click on rectangle: resets offset to centre (does NOT reset rotation).
    /// </summary>
    public partial class OrbitalFramingCanvas : UserControl {

        // ─── DependencyProperties ────────────────────────────────────────────────

        public static readonly DependencyProperty CapturedImageSourceProperty =
            DependencyProperty.Register(
                nameof(CapturedImageSource),
                typeof(BitmapSource),
                typeof(OrbitalFramingCanvas),
                new PropertyMetadata(null, OnCapturedImageSourceChanged));

        // Pass-through only: VM binds the plate-solved PA here so it can read it back
        // for the final-PA math. The canvas does NOT apply this to the captured image
        // visual — the image always renders in its native sensor frame.
        public static readonly DependencyProperty CapturedImageRotationProperty =
            DependencyProperty.Register(
                nameof(CapturedImageRotation),
                typeof(double),
                typeof(OrbitalFramingCanvas),
                new PropertyMetadata(0.0));

        public static readonly DependencyProperty CapturedImagePixelWidthProperty =
            DependencyProperty.Register(
                nameof(CapturedImagePixelWidth),
                typeof(double),
                typeof(OrbitalFramingCanvas),
                new PropertyMetadata(0.0, OnCapturedImagePixelDimensionChanged));

        public static readonly DependencyProperty CapturedImagePixelHeightProperty =
            DependencyProperty.Register(
                nameof(CapturedImagePixelHeight),
                typeof(double),
                typeof(OrbitalFramingCanvas),
                new PropertyMetadata(0.0, OnCapturedImagePixelDimensionChanged));

        public static readonly DependencyProperty BackgroundImageSourceProperty =
            DependencyProperty.Register(
                nameof(BackgroundImageSource),
                typeof(BitmapSource),
                typeof(OrbitalFramingCanvas),
                new PropertyMetadata(null, OnBackgroundImageSourceChanged));

        public static readonly DependencyProperty AnnotationImageSourceProperty =
            DependencyProperty.Register(
                nameof(AnnotationImageSource),
                typeof(BitmapSource),
                typeof(OrbitalFramingCanvas),
                new PropertyMetadata(null, OnAnnotationImageSourceChanged));

        public static readonly DependencyProperty RectangleOffsetXProperty =
            DependencyProperty.Register(
                nameof(RectangleOffsetX),
                typeof(double),
                typeof(OrbitalFramingCanvas),
                new FrameworkPropertyMetadata(
                    0.0,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnRectangleOffsetXChanged));

        public static readonly DependencyProperty RectangleOffsetYProperty =
            DependencyProperty.Register(
                nameof(RectangleOffsetY),
                typeof(double),
                typeof(OrbitalFramingCanvas),
                new FrameworkPropertyMetadata(
                    0.0,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnRectangleOffsetYChanged));

        public static readonly DependencyProperty RectangleRotationProperty =
            DependencyProperty.Register(
                nameof(RectangleRotation),
                typeof(double),
                typeof(OrbitalFramingCanvas),
                new FrameworkPropertyMetadata(
                    0.0,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnRectangleRotationChanged));

        public static readonly DependencyProperty BackgroundFovMultiplierProperty =
            DependencyProperty.Register(
                nameof(BackgroundFovMultiplier),
                typeof(double),
                typeof(OrbitalFramingCanvas),
                new FrameworkPropertyMetadata(3.0, OnBackgroundFovMultiplierChanged));

        public double BackgroundFovMultiplier {
            get => (double)GetValue(BackgroundFovMultiplierProperty);
            set => SetValue(BackgroundFovMultiplierProperty, value);
        }

        private static void OnBackgroundFovMultiplierChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            ((OrbitalFramingCanvas)d).UpdateCapturedLayerSize();
        }

        // ─── CLR wrappers ────────────────────────────────────────────────────────

        public BitmapSource CapturedImageSource {
            get => (BitmapSource)GetValue(CapturedImageSourceProperty);
            set => SetValue(CapturedImageSourceProperty, value);
        }

        public double CapturedImageRotation {
            get => (double)GetValue(CapturedImageRotationProperty);
            set => SetValue(CapturedImageRotationProperty, value);
        }

        /// <summary>
        /// Native pixel width of the captured frame (sensor frame). Drives the captured-image
        /// layer footprint and the framing rectangle so they share the source aspect ratio.
        /// </summary>
        public double CapturedImagePixelWidth {
            get => (double)GetValue(CapturedImagePixelWidthProperty);
            set => SetValue(CapturedImagePixelWidthProperty, value);
        }

        /// <summary>
        /// Native pixel height of the captured frame (sensor frame). Drives the captured-image
        /// layer footprint and the framing rectangle so they share the source aspect ratio.
        /// </summary>
        public double CapturedImagePixelHeight {
            get => (double)GetValue(CapturedImagePixelHeightProperty);
            set => SetValue(CapturedImagePixelHeightProperty, value);
        }

        public BitmapSource BackgroundImageSource {
            get => (BitmapSource)GetValue(BackgroundImageSourceProperty);
            set => SetValue(BackgroundImageSourceProperty, value);
        }

        public BitmapSource AnnotationImageSource {
            get => (BitmapSource)GetValue(AnnotationImageSourceProperty);
            set => SetValue(AnnotationImageSourceProperty, value);
        }

        /// <summary>
        /// Horizontal offset of the framing rectangle from centre, expressed in
        /// captured-image pixels (sensor frame). The canvas converts to its own pixel
        /// space when positioning the rectangle visually.
        /// </summary>
        public double RectangleOffsetX {
            get => (double)GetValue(RectangleOffsetXProperty);
            set => SetValue(RectangleOffsetXProperty, value);
        }

        /// <summary>
        /// Vertical offset of the framing rectangle from centre, expressed in
        /// captured-image pixels (sensor frame). The canvas converts to its own pixel
        /// space when positioning the rectangle visually.
        /// </summary>
        public double RectangleOffsetY {
            get => (double)GetValue(RectangleOffsetYProperty);
            set => SetValue(RectangleOffsetYProperty, value);
        }

        /// <summary>
        /// Rotation of the framing rectangle in degrees, interpreted by the VM as a
        /// delta relative to the plate-solved <see cref="CapturedImageRotation"/>.
        /// </summary>
        public double RectangleRotation {
            get => (double)GetValue(RectangleRotationProperty);
            set => SetValue(RectangleRotationProperty, value);
        }

        // ─── Drag state ──────────────────────────────────────────────────────────

        private bool _isDragging;
        private Point _dragStart;
        private double _offsetXAtDragStart;
        private double _offsetYAtDragStart;

        // Canvas pixels per captured-image pixel. Set in UpdateCapturedLayerSize();
        // 1.0 until a captured-image dimension is known. Used to convert between
        // image-pixel DP values (RectangleOffsetX/Y) and the visual canvas-pixel
        // positions of the TranslateTransform.
        private double _canvasPxPerImagePx = 1.0;

        // ─── Constructor ─────────────────────────────────────────────────────────

        public OrbitalFramingCanvas() {
            InitializeComponent();
        }

        // ─── Property-change callbacks ───────────────────────────────────────────

        private static void OnCapturedImageSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var ctrl = (OrbitalFramingCanvas)d;
            ctrl.CapturedLayer.Source = (BitmapSource)e.NewValue;
            ctrl.UpdateCapturedLayerSize();
        }

        private static void OnCapturedImagePixelDimensionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            ((OrbitalFramingCanvas)d).UpdateCapturedLayerSize();
        }

        private static void OnBackgroundImageSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var ctrl = (OrbitalFramingCanvas)d;
            ctrl.BackgroundLayer.Source = (BitmapSource)e.NewValue;
        }

        private static void OnAnnotationImageSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var ctrl = (OrbitalFramingCanvas)d;
            ctrl.AnnotationLayer.Source = (BitmapSource)e.NewValue;
        }

        private static void OnRectangleOffsetXChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var ctrl = (OrbitalFramingCanvas)d;
            ctrl.RectTranslate.X = (double)e.NewValue * ctrl._canvasPxPerImagePx;
        }

        private static void OnRectangleOffsetYChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var ctrl = (OrbitalFramingCanvas)d;
            ctrl.RectTranslate.Y = (double)e.NewValue * ctrl._canvasPxPerImagePx;
        }

        private static void OnRectangleRotationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var ctrl = (OrbitalFramingCanvas)d;
            ctrl.RectRotation.Angle = (double)e.NewValue;
        }

        // ─── Layout events ───────────────────────────────────────────────────────

        private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e) {
            UpdateCapturedLayerSize();
            UpdateCrosshair();
        }

        /// <summary>
        /// Size the captured-image layer and the framing rectangle so the visible
        /// vertical extent equals BackgroundFovMultiplier × the captured frame's
        /// height (the user wants the height dimension to drive the FOV ratio).
        /// Width follows from the source aspect, so on a wide sensor the captured
        /// image may extend horizontally beyond the viewport; the ScrollViewer
        /// surfaces horizontal scrollbars once content exceeds the viewport.
        /// Pre-capture (pixel dimensions unset), falls back to a square layout
        /// 1/BackgroundFovMultiplier on each side.
        /// </summary>
        private void UpdateCapturedLayerSize() {
            double fovMultiplier = Math.Max(1.0, BackgroundFovMultiplier);

            double w = RootGrid.ActualWidth;
            double h = RootGrid.ActualHeight;

            if (w <= 0 || h <= 0) return;

            double pxW = CapturedImagePixelWidth;
            double pxH = CapturedImagePixelHeight;

            if (pxW > 0 && pxH > 0) {
                // Height-driven scale: visible vertical extent = fovMultiplier × captured height.
                double scale = h / (pxH * fovMultiplier);
                double imgW = pxW * scale;
                double imgH = pxH * scale;
                _canvasPxPerImagePx = scale;

                CapturedLayer.Width = imgW;
                CapturedLayer.Height = imgH;
                FramingRectangle.Width = imgW;
                FramingRectangle.Height = imgH;
            } else {
                // Pre-capture: hide the captured-image layer and the framing rectangle.
                // The background sky-survey layer remains visible so the user can see the
                // sky context before capturing.
                _canvasPxPerImagePx = 1.0;
                CapturedLayer.Width = 0;
                CapturedLayer.Height = 0;
                FramingRectangle.Width = 0;
                FramingRectangle.Height = 0;
            }

            // Re-apply existing image-pixel offset DP values through the new scale so
            // a window resize doesn't visually displace the rectangle.
            RectTranslate.X = RectangleOffsetX * _canvasPxPerImagePx;
            RectTranslate.Y = RectangleOffsetY * _canvasPxPerImagePx;
        }

        private void UpdateCrosshair() {
            double w = RootGrid.ActualWidth;
            double h = RootGrid.ActualHeight;
            double cx = w / 2.0;
            double cy = h / 2.0;

            CrosshairH.X1 = 0;
            CrosshairH.Y1 = cy;
            CrosshairH.X2 = w;
            CrosshairH.Y2 = cy;

            CrosshairV.X1 = cx;
            CrosshairV.Y1 = 0;
            CrosshairV.X2 = cx;
            CrosshairV.Y2 = h;
        }

        // ─── Mouse drag (pan) ────────────────────────────────────────────────────

        private void FramingRectangle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            _isDragging = true;
            _dragStart = e.GetPosition(RootGrid);
            _offsetXAtDragStart = RectangleOffsetX;
            _offsetYAtDragStart = RectangleOffsetY;
            ((UIElement)sender).CaptureMouse();
            e.Handled = true;
        }

        private void FramingRectangle_MouseMove(object sender, MouseEventArgs e) {
            if (!_isDragging) return;

            Point current = e.GetPosition(RootGrid);
            double deltaCanvasX = current.X - _dragStart.X;
            double deltaCanvasY = current.Y - _dragStart.Y;

            // Convert canvas-pixel drag deltas to captured-image-pixel deltas so the DPs
            // remain in sensor-frame units. The DP callbacks multiply back by
            // _canvasPxPerImagePx when positioning the rectangle visually.
            double scale = _canvasPxPerImagePx > 0 ? _canvasPxPerImagePx : 1.0;
            double deltaImgX = deltaCanvasX / scale;
            double deltaImgY = deltaCanvasY / scale;

            SetCurrentValue(RectangleOffsetXProperty, _offsetXAtDragStart + deltaImgX);
            SetCurrentValue(RectangleOffsetYProperty, _offsetYAtDragStart + deltaImgY);

            e.Handled = true;
        }

        private void FramingRectangle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
            if (_isDragging) {
                _isDragging = false;
                ((UIElement)sender).ReleaseMouseCapture();
            }
            e.Handled = true;
        }

        // ─── Mouse wheel (rotation) ──────────────────────────────────────────────

        private void FramingRectangle_MouseWheel(object sender, MouseWheelEventArgs e) {
            // Each wheel notch = 1°.  Shift held = 0.1° fine-adjust.
            double step = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)
                ? 0.1
                : 1.0;

            double delta = e.Delta > 0 ? step : -step;
            double newRotation = (((RectangleRotation + delta) % 360.0) + 360.0) % 360.0;
            SetCurrentValue(RectangleRotationProperty, newRotation);

            e.Handled = true;
        }

        // ─── Right-click: reset pan to centre ───────────────────────────────────

        private void FramingRectangle_MouseRightButtonDown(object sender, MouseButtonEventArgs e) {
            SetCurrentValue(RectangleOffsetXProperty, 0.0);
            SetCurrentValue(RectangleOffsetYProperty, 0.0);
            e.Handled = true;
        }
    }
}
