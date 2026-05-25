using FluentAssertions;
using NINA.Joko.Plugin.Orbitals.Calculations;
using NINA.Joko.Plugin.Orbitals.Utility;
using NUnit.Framework;
using System.ComponentModel;

namespace NINA.Joko.Plugin.Orbitals.Tests.Utility {

    [TestFixture]
    public class DistanceTests {

        [Test]
        public void DisplayUnits_BelowZeroPointOneAU_IsKm() {
            var d = new Distance(0.05);
            d.DisplayUnits.Should().Be("km");
            d.DisplayDistance.Should().BeApproximately(0.05 * AstrometricConstants.KM_PER_AU, 1e-6);
        }

        [Test]
        public void DisplayUnits_AtBoundary_IsAU() {
            // Threshold is `au < 0.1`. Exactly 0.1 should display in AU (not km).
            var d = new Distance(0.1);
            d.DisplayUnits.Should().Be("au");
            d.DisplayDistance.Should().Be(0.1);
        }

        [Test]
        public void DisplayUnits_AboveBoundary_IsAU() {
            var d = new Distance(0.5);
            d.DisplayUnits.Should().Be("au");
            d.DisplayDistance.Should().Be(0.5);
        }

        [Test]
        public void AUSetter_RaisesPropertyChanged() {
            var d = new Distance(1.0);
            var notifications = 0;
            ((INotifyPropertyChanged)d).PropertyChanged += (_, _) => notifications++;

            d.AU = 2.0;

            notifications.Should().BeGreaterThan(0);
            d.AU.Should().Be(2.0);
        }

        [Test]
        public void AUSetter_SameValue_DoesNotRaisePropertyChanged() {
            var d = new Distance(1.0);
            var notifications = 0;
            ((INotifyPropertyChanged)d).PropertyChanged += (_, _) => notifications++;

            d.AU = 1.0;

            notifications.Should().Be(0);
        }
    }
}
