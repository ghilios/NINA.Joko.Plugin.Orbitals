#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using CommunityToolkit.Mvvm.Input;
using System.ComponentModel;
using System.Windows;

namespace NINA.Joko.Plugin.Orbitals.Utility {

    public static class AsyncRelayCommandExtensions {

        public static void RegisterPropertyChangeNotification(this IRelayCommand value, INotifyPropertyChanged observable, params string[] propertyNames) {
            observable.PropertyChanged += (object sender, PropertyChangedEventArgs e) => {
                foreach (var propertyName in propertyNames) {
                    if (e.PropertyName == propertyName) {
                        Application.Current.Dispatcher.Invoke(value.NotifyCanExecuteChanged);
                        return;
                    }
                }
            };
        }
    }
}