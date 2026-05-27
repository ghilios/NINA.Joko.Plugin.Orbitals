#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Joko.Plugin.Orbitals.ViewModels;
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace NINA.Joko.Plugin.Orbitals.View {

    /// <summary>
    /// Code-behind for the Orbital Framing Wizard placeholder window.
    /// Phase B: minimal shell; full layout is Phase C.
    /// </summary>
    public partial class OrbitalFramingWizardView : Window {

        public OrbitalFramingWizardView() {
            InitializeComponent();
            DataContextChanged += OrbitalFramingWizardView_DataContextChanged;
            Closed += OrbitalFramingWizardView_Closed;
        }

        private void OrbitalFramingWizardView_Closed(object sender, EventArgs e) {
            (DataContext as OrbitalFramingWizardVM)?.Dispose();
        }

        private void OrbitalFramingWizardView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {
            if (e.OldValue is OrbitalFramingWizardVM oldVm) {
                oldVm.CloseRequested -= Vm_CloseRequested;
            }
            if (e.NewValue is OrbitalFramingWizardVM newVm) {
                newVm.CloseRequested += Vm_CloseRequested;
            }
        }

        private void Vm_CloseRequested(object sender, EventArgs e) {
            Close();
        }

        // ─── Zoom: center-anchored, Ctrl+wheel, top-left buttons ───────────────

        private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomByFactor(OrbitalFramingWizardVM.ZoomStep);
        private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomByFactor(1.0 / OrbitalFramingWizardVM.ZoomStep);
        private void ZoomReset_Click(object sender, RoutedEventArgs e) => ZoomTo(1.0);

        private void CanvasScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control) return;
            ZoomByFactor(e.Delta > 0 ? OrbitalFramingWizardVM.ZoomStep : 1.0 / OrbitalFramingWizardVM.ZoomStep);
            e.Handled = true;
        }

        private void ZoomByFactor(double factor) {
            if (DataContext is not OrbitalFramingWizardVM vm) return;
            ZoomTo(vm.CanvasZoom * factor);
        }

        /// <summary>
        /// Sets <see cref="OrbitalFramingWizardVM.CanvasZoom"/> while preserving the
        /// viewport's visible center. We capture the fractional center of the viewport
        /// within the current extent, change zoom (which re-measures the canvas via the
        /// LayoutTransform), then re-apply the same fractional center on the new extent
        /// once the layout pass completes.
        /// </summary>
        private void ZoomTo(double newZoom) {
            if (DataContext is not OrbitalFramingWizardVM vm) return;
            if (CanvasScroll == null) return;

            var sv = CanvasScroll;
            double vpW = sv.ViewportWidth;
            double vpH = sv.ViewportHeight;
            double oldExtentW = sv.ExtentWidth;
            double oldExtentH = sv.ExtentHeight;

            double fx = oldExtentW > 0 ? (sv.HorizontalOffset + vpW / 2.0) / oldExtentW : 0.5;
            double fy = oldExtentH > 0 ? (sv.VerticalOffset + vpH / 2.0) / oldExtentH : 0.5;

            vm.CanvasZoom = newZoom;

            // Re-anchor after the LayoutTransform has been applied. DispatcherPriority.Loaded
            // runs after Layout/Render so ExtentWidth/Height reflect the post-zoom canvas.
            Dispatcher.BeginInvoke(new Action(() => {
                double newExtentW = sv.ExtentWidth;
                double newExtentH = sv.ExtentHeight;
                double vpW2 = sv.ViewportWidth;
                double vpH2 = sv.ViewportHeight;
                sv.ScrollToHorizontalOffset(Math.Max(0, fx * newExtentW - vpW2 / 2.0));
                sv.ScrollToVerticalOffset(Math.Max(0, fy * newExtentH - vpH2 / 2.0));
            }), DispatcherPriority.Loaded);
        }
    }
}
