#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Input;

namespace NINA.Joko.Plugin.Orbitals.SequenceItems {

    /// <summary>
    /// Interaction logic for DataTemplates.xaml
    /// </summary>
    [Export(typeof(ResourceDictionary))]
    public partial class DataTemplates : ResourceDictionary {

        public DataTemplates() {
            InitializeComponent();
        }

        private void ManualTLEContainerStackPanel_MouseDown(object sender, MouseButtonEventArgs e) {
            // https://stackoverflow.com/questions/6489032/wpf-remove-focus-when-clicking-outside-of-a-textbox
            Keyboard.ClearFocus();
        }
    }
}