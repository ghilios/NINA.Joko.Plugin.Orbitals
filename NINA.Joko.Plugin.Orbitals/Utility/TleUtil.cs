#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Interfaces.Mediator;
using SGPdotNET.TLE;
using System;

namespace NINA.Joko.Plugin.Orbitals.Utility {

    public static class TleUtil {

        public static bool ParseTle(string input, out Tle tle) {
            try {
                tle = ParseTle(input);
                return true;
            } catch (Exception) {
                tle = null;
                return false;
            }
        }

        public static Tle ParseTle(string input) {
            string[] splitTle = input.Split(Environment.NewLine);
            if (splitTle.Length < 2 || splitTle.Length > 3) {
                throw new ArgumentException("TLE must have 2 or 3 lines");
            }

            string lineOne = splitTle.Length < 3 ? "No Name" : splitTle[0];
            string lineTwo = splitTle.Length < 3 ? splitTle[0] : splitTle[1];
            string lineThree = splitTle.Length < 3 ? splitTle[1] : splitTle[2];
            return new Tle(lineOne, lineTwo, lineThree);
        }

        public static bool TelescopeSupportsShiftRate(TelescopeInfo telescopeInfo) {
            return telescopeInfo.CanSetDeclinationRate && telescopeInfo.CanSetRightAscensionRate && telescopeInfo.CanSetTrackingEnabled;
        }
    }
}