#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Equipment.Interfaces.ViewModel;
using NINA.Joko.Plugin.Orbitals.Interfaces;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.ViewModel;
using System;
using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Input;

namespace NINA.Joko.Plugin.Orbitals.ViewModels {

    [Export(typeof(IDockableVM))]
    public class OrbitalsVM : DockableVM {
        private readonly IApplicationStatusMediator applicationStatusMediator;
        private readonly IOrbitalsOptions orbitalsOptions;

        [ImportingConstructor]
        public OrbitalsVM(
            IProfileService profileService,
            IApplicationStatusMediator applicationStatusMediator)
            : this(profileService, applicationStatusMediator, OrbitalsPlugin.OrbitalsOptions) {
        }

        public OrbitalsVM(
            IProfileService profileService,
            IApplicationStatusMediator applicationStatusMediator,
            IOrbitalsOptions orbitalsOptions) : base(profileService) {
            this.Title = "Orbitals";

            /*
            var dict = new ResourceDictionary();
            dict.Source = new Uri("NINA.Joko.Plugin.Orbitals;component/Resources/SVGDataTemplates.xaml", UriKind.RelativeOrAbsolute);
            ImageGeometry = (System.Windows.Media.GeometryGroup)dict["TenMicronSVG"];
            ImageGeometry.Freeze();
            */

            this.applicationStatusMediator = applicationStatusMediator;
            this.orbitalsOptions = orbitalsOptions;
        }

        private DateTime? cometLastUpdated;

        public DateTime? CometLastUpdated {
            get => cometLastUpdated;
            private set {
                cometLastUpdated = value;
                RaisePropertyChanged();
            }
        }

        public ICommand CancelUpdateCometElementsComment { get; private set; }

        public ICommand UpdateCometElementsCommand { get; private set; }
    }
}