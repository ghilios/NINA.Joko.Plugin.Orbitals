#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Newtonsoft.Json;
using NINA.Astrometry;
using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace NINA.Joko.Plugin.Orbitals.Utility {

    [JsonObject(MemberSerialization.OptIn)]
    public class InputCoordinatesEx : BaseINPC {
        private bool deserializing = false;

        [OnDeserializing]
        public void OnDeserializing(StreamingContext context) {
            deserializing = true;
        }

        [OnDeserialized]
        public void OnDeserialized(StreamingContext context) {
            deserializing = false;
            if (NegativeDec && Coordinates.Dec > 0) {
                Coordinates.Dec = Coordinates.Dec * -1d;
            }
            if (NegativeRA && Coordinates.RA > 0) {
                Coordinates.RA = Coordinates.RA * -1d;
            }
            RaiseCoordinatesChanged();
        }

        public InputCoordinatesEx() {
            Coordinates = new Coordinates(Angle.Zero, Angle.Zero, Epoch.J2000);
        }

        public InputCoordinatesEx(Coordinates coordinates) {
            Coordinates = coordinates;
        }

        private Coordinates coordinates;

        public Coordinates Coordinates {
            get => coordinates;
            set {
                coordinates = value;
                RaiseCoordinatesChanged();
            }
        }

        [JsonProperty]
        public int RAHours {
            get {
                return (int)Math.Truncate(coordinates.RA);
            }
            set {
                if (NegativeRA) {
                    coordinates.RA = value - RAMinutes / 60.0d - RASeconds / (60.0d * 60.0d);
                } else {
                    coordinates.RA = value + RAMinutes / 60.0d + RASeconds / (60.0d * 60.0d);
                }
                RaiseCoordinatesChanged();
            }
        }

        [JsonProperty]
        public int RAMinutes {
            get {
                var minutes = (Math.Abs(coordinates.RA * 60.0d) % 60);

                var seconds = (int)Math.Round((Math.Abs(coordinates.RA * 60.0d * 60.0d) % 60));
                if (seconds > 59) {
                    minutes += 1;
                }

                return (int)Math.Floor(minutes);
            }
            set {
                if (NegativeRA) {
                    coordinates.RA = coordinates.RA + RAMinutes / 60.0d - value / 60.0d;
                } else {
                    coordinates.RA = coordinates.RA - RAMinutes / 60.0d + value / 60.0d;
                }

                RaiseCoordinatesChanged();
            }
        }

        [JsonProperty]
        public double RASeconds {
            get {
                var seconds = Math.Round((Math.Abs(coordinates.RA * 60.0d * 60.0d) % 60), 5);
                if (seconds >= 60.0) {
                    seconds = 0;
                }
                return seconds;
            }
            set {
                if (NegativeRA) {
                    coordinates.RA = coordinates.RA + RASeconds / (60.0d * 60.0d) - value / (60.0d * 60.0d);
                } else {
                    coordinates.RA = coordinates.RA - RASeconds / (60.0d * 60.0d) + value / (60.0d * 60.0d);
                }

                RaiseCoordinatesChanged();
            }
        }

        private bool negativeDec;

        [JsonProperty]
        public bool NegativeDec {
            get => negativeDec;
            set {
                negativeDec = value;
                RaisePropertyChanged();
            }
        }

        private bool negativeRA;

        [JsonProperty]
        public bool NegativeRA {
            get => negativeRA;
            set {
                negativeRA = value;
                RaisePropertyChanged();
            }
        }

        [JsonProperty]
        public int DecDegrees {
            get {
                return (int)Math.Truncate(coordinates.Dec);
            }
            set {
                if (NegativeDec) {
                    coordinates.Dec = value - DecMinutes / 60.0d - DecSeconds / (60.0d * 60.0d);
                } else {
                    coordinates.Dec = value + DecMinutes / 60.0d + DecSeconds / (60.0d * 60.0d);
                }
                RaiseCoordinatesChanged();
            }
        }

        [JsonProperty]
        public int DecMinutes {
            get {
                var minutes = (Math.Abs(coordinates.Dec * 60.0d) % 60);

                var seconds = (int)Math.Round((Math.Abs(coordinates.Dec * 60.0d * 60.0d) % 60));
                if (seconds > 59) {
                    minutes += 1;
                }

                return (int)Math.Floor(minutes);
            }
            set {
                if (NegativeDec) {
                    coordinates.Dec = coordinates.Dec + DecMinutes / 60.0d - value / 60.0d;
                } else {
                    coordinates.Dec = coordinates.Dec - DecMinutes / 60.0d + value / 60.0d;
                }

                RaiseCoordinatesChanged();
            }
        }

        [JsonProperty]
        public double DecSeconds {
            get {
                var seconds = Math.Round((Math.Abs(coordinates.Dec * 60.0d * 60.0d) % 60), 5);
                if (seconds >= 60.0) {
                    seconds = 0;
                }
                return seconds;
            }
            set {
                if (NegativeDec) {
                    coordinates.Dec = coordinates.Dec + DecSeconds / (60.0d * 60.0d) - value / (60.0d * 60.0d);
                } else {
                    coordinates.Dec = coordinates.Dec - DecSeconds / (60.0d * 60.0d) + value / (60.0d * 60.0d);
                }

                RaiseCoordinatesChanged();
            }
        }

        private void RaiseCoordinatesChanged() {
            if (!deserializing) {
                if (Coordinates?.RA != 0 || Coordinates?.Dec != 0) {
                    NegativeDec = Coordinates?.Dec < 0;
                    NegativeRA = Coordinates?.RA < 0;
                    RaisePropertyChanged(nameof(Coordinates));
                    RaisePropertyChanged(nameof(RAHours));
                    RaisePropertyChanged(nameof(RAMinutes));
                    RaisePropertyChanged(nameof(RASeconds));
                    RaisePropertyChanged(nameof(DecDegrees));
                    RaisePropertyChanged(nameof(DecMinutes));
                    RaisePropertyChanged(nameof(DecSeconds));

                    this.CoordinatesChanged?.Invoke(this, new EventArgs());
                }
            }
        }

        public event EventHandler CoordinatesChanged;

        public InputCoordinates Clone() =>
            new InputCoordinates(coordinates.Clone());

        public override string ToString() {
            return Coordinates.ToString();
        }
    }
}