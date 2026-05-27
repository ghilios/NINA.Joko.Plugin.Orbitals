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
    /// Layer 2 — Captured image centred at 1/BackgroundFovMultiplier scale, with CapturedImageRotation.
    /// Layer 3 — Draggable/wheel-rotatable yellow framing rectangle.
    ///
    /// Pan: left-drag on the rectangle updates RectangleOffsetX / RectangleOffsetY.
    /// Rotate: mouse-wheel on the rectangle updates RectangleRotation.
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

        public static readonly DependencyProperty CapturedImageRotationProperty =
            DependencyProperty.Register(
                nameof(CapturedImageRotation),
                typeof(double),
                typeof(OrbitalFramingCanvas),
                new PropertyMetadata(0.0, OnCapturedImageRotationChanged));

        public static readonly DependencyProperty BackgroundImageSourceProperty =
            DependencyProperty.Register(
                nameof(BackgroundImageSource),
                typeof(BitmapSource),
                typeof(OrbitalFramingCanvas),
                new PropertyMetadata(null, OnBackgroundImageSourceChanged));

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

        // ─── CLR wrappers ────────────────────────────────────────────────────────

        public BitmapSource CapturedImageSource {
            get => (BitmapSource)GetValue(CapturedImageSourceProperty);
            set => SetValue(CapturedImageSourceProperty, value);
        }

        public double CapturedImageRotation {
            get => (double)GetValue(CapturedImageRotationProperty);
            set => SetValue(CapturedImageRotationProperty, value);
        }

        public BitmapSource BackgroundImageSource {
            get => (BitmapSource)GetValue(BackgroundImageSourceProperty);
            set => SetValue(BackgroundImageSourceProperty, value);
        }

        /// <summary>Horizontal canvas-pixel offset of the framing rectangle from centre.</summary>
        public double RectangleOffsetX {
            get => (double)GetValue(RectangleOffsetXProperty);
            set => SetValue(RectangleOffsetXProperty, value);
        }

        /// <summary>Vertical canvas-pixel offset of the framing rectangle from centre.</summary>
        public double RectangleOffsetY {
            get => (double)GetValue(RectangleOffsetYProperty);
            set => SetValue(RectangleOffsetYProperty, value);
        }

        /// <summary>Rotation of the framing rectangle in degrees.</summary>
        public double RectangleRotation {
            get => (double)GetValue(RectangleRotationProperty);
            set => SetValue(RectangleRotationProperty, value);
        }

        // ─── Drag state ──────────────────────────────────────────────────────────

        private bool _isDragging;
        private Point _dragStart;
        private double _offsetXAtDragStart;
        private double _offsetYAtDragStart;

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

        private static void OnCapturedImageRotationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var ctrl = (OrbitalFramingCanvas)d;
            ctrl.CapturedRotation.Angle = (double)e.NewValue;
        }

        private static void OnBackgroundImageSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var ctrl = (OrbitalFramingCanvas)d;
            ctrl.BackgroundLayer.Source = (BitmapSource)e.NewValue;
        }

        private static void OnRectangleOffsetXChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var ctrl = (OrbitalFramingCanvas)d;
            ctrl.RectTranslate.X = (double)e.NewValue;
        }

        private static void OnRectangleOffsetYChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            var ctrl = (OrbitalFramingCanvas)d;
            ctrl.RectTranslate.Y = (double)e.NewValue;
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
        /// Size the captured-image layer to 1/BackgroundFovMultiplier of the control.
        /// The multiplier lives on the VM; we expose it via the UserControl's
        /// DataContext if available, but default to 3.0 so the layer is one-third
        /// of the canvas dimensions if no VM is wired.
        /// </summary>
        private void UpdateCapturedLayerSize() {
            double fovMultiplier = 3.0;
            if (DataContext is ViewModels.OrbitalFramingWizardVM vm)
                fovMultiplier = Math.Max(1.0, vm.BackgroundFovMultiplier);

            double w = RootGrid.ActualWidth;
            double h = RootGrid.ActualHeight;

            if (w <= 0 || h <= 0) return;

            double imgW = w / fovMultiplier;
            double imgH = h / fovMultiplier;

            CapturedLayer.Width = imgW;
            CapturedLayer.Height = imgH;

            // Size the framing rectangle to match the captured-image layer.
            FramingRectangle.Width = imgW;
            FramingRectangle.Height = imgH;
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
            double deltaX = current.X - _dragStart.X;
            double deltaY = current.Y - _dragStart.Y;

            // Update DPs — the callbacks push through to the TranslateTransform.
            SetCurrentValue(RectangleOffsetXProperty, _offsetXAtDragStart + deltaX);
            SetCurrentValue(RectangleOffsetYProperty, _offsetYAtDragStart + deltaY);

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
