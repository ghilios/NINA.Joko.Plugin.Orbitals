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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace NINA.Joko.Plugin.Orbitals.View {

    /// <summary>
    /// Interaction logic for OrbitalsView.xaml
    /// </summary>
    public partial class OrbitalsView : UserControl {

        public OrbitalsView() {
            InitializeComponent();
        }

        private void RefreshEnabled_Toggled(object sender, RoutedEventArgs e) {
            var checkBox = (CheckBox)sender;
            var orbitalsVM = (OrbitalsVM)checkBox.DataContext;
            bool targetUnchecked = e.RoutedEvent == ToggleButton.UncheckedEvent;
            bool targetChecked = e.RoutedEvent == ToggleButton.CheckedEvent;
            if (targetChecked) {
                checkBox.IsChecked = true;
                orbitalsVM.ToggleRefreshEnabled(true);
            }
            if (targetUnchecked) {
                checkBox.IsChecked = false;
                orbitalsVM.ToggleRefreshEnabled(false);
            }
        }
    }
}