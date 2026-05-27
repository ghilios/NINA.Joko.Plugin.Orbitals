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
    }
}
