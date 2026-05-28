#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace NINA.Joko.Plugin.Orbitals.View {

    /// <summary>
    /// Modal for entering RA / Dec offset directly. Used when the user prefers
    /// to think in equatorial terms; the caller converts the entered offset
    /// (anchored at the target body's current position) back to the canonical
    /// Separation + Offset PA representation on OK.
    /// </summary>
    public partial class RADecOffsetDialog : Window, INotifyPropertyChanged {

        public RADecOffsetDialog(double initialRAOffsetHours, double initialDecOffsetDegrees) {
            raOffsetHours = initialRAOffsetHours;
            decOffsetDegrees = initialDecOffsetDegrees;
            DataContext = this;
            InitializeComponent();
        }

        private double raOffsetHours;
        public double RAOffsetHours {
            get => raOffsetHours;
            set { raOffsetHours = value; OnPropertyChanged(); }
        }

        private double decOffsetDegrees;
        public double DecOffsetDegrees {
            get => decOffsetDegrees;
            set { decOffsetDegrees = value; OnPropertyChanged(); }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e) {
            // Commit any pending TextBox edit before reading the bound values.
            RAOffsetTextBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
            DecOffsetTextBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) {
            DialogResult = false;
            Close();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
