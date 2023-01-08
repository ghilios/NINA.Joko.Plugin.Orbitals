#region "copyright"

/*
    Copyright © 2021 - 2021 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Reflection;
using System.Runtime.InteropServices;

// [MANDATORY] The following GUID is used as a unique identifier of the plugin
[assembly: Guid("AFA13A89-8AE3-4975-A953-683C6B6E2BBE")]

// [MANDATORY] The assembly versioning
//Should be incremented for each new release build of a plugin
[assembly: AssemblyVersion("3.0.0.2")]
[assembly: AssemblyFileVersion("3.0.0.2")]

// [MANDATORY] The name of your plugin
[assembly: AssemblyTitle("Orbitals")]
// [MANDATORY] A short description of your plugin
[assembly: AssemblyDescription("Downloads publically available orbital data to target and track Comets, Asteroids, Planets, the Moon, the Sun, and other objects")]

// The following attributes are not required for the plugin per se, but are required by the official manifest meta data

// Your name
[assembly: AssemblyCompany("George Hilios (jokogeo)")]
// The product name that this plugin is part of
[assembly: AssemblyProduct("Orbitals")]
[assembly: AssemblyCopyright("Copyright ©  2022")]

// The minimum Version of N.I.N.A. that this plugin is compatible with
[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.0.0.1008")]

// The license your plugin code is using
[assembly: AssemblyMetadata("License", "MPL-2.0")]
// The url to the license
[assembly: AssemblyMetadata("LicenseURL", "https://www.mozilla.org/en-US/MPL/2.0/")]
// The repository where your pluggin is hosted
[assembly: AssemblyMetadata("Repository", "https://github.com/ghilios/NINA.Joko.Plugin.Orbitals")]

// The following attributes are optional for the official manifest meta data

//[Optional] Your plugin homepage URL - omit if not applicaple
[assembly: AssemblyMetadata("Homepage", "")]

//[Optional] Common tags that quickly describe your plugin
[assembly: AssemblyMetadata("Tags", "Asteroid,Comet,Orbital,Planet,Moon,Sun")]

//[Optional] A link that will show a log of all changes in between your plugin's versions
[assembly: AssemblyMetadata("ChangelogURL", "https://github.com/ghilios/NINA.Joko.Plugin.Orbitals/commits/develop")]

//[Optional] The url to a featured logo that will be displayed in the plugin list next to the name
[assembly: AssemblyMetadata("FeaturedImageURL", "https://github.com/ghilios/NINA.Joko.Plugin.Orbitals/releases/download/resources/orbit_path.png")]
//[Optional] A url to an example screenshot of your plugin in action
/*
[assembly: AssemblyMetadata("ScreenshotURL", "https://github.com/ghilios/NINA.Joko.Plugin.Orbitals/releases/download/resources/TenMicronToolsScreenshot.JPG")]
//[Optional] An additional url to an example example screenshot of your plugin in action
[assembly: AssemblyMetadata("AltScreenshotURL", "https://github.com/ghilios/NINA.Joko.Plugin.Orbitals/releases/download/resources/TenMicronToolsAltScreenshot.JPG")]
//[Optional] An in-depth description of your plugin
*/
[assembly: AssemblyMetadata("LongDescription", @"This plugin enables slewing and tracking for planets, the sun, the moon, comets, asteroids, and other bodies that are close enough to the solar system that they don't move at the sidereal tracking rate in the sky.

To get started, go to the Orbitals pane in the Imaging tab and click ""Update"" for each type of orbital object you're interested in working with. I don't recommend downloading more than you need, because they will all be loaded when NINA starts up. An internet connection is required when you download the data, but not again until you want to do another update for the latest data.

Solar system bodies - such as the planets, the Sun, and the Moon - don't require updating as they use the JPL Ephemeris that is already included with NINA.

# Features #

* Solar System Object Sequencer container for setting a solar system body as an Advanced Sequencer target
* Orbital Object Sequencer container for setting any orbital object found in the downloaded elements data as an Advanced Sequencer target
* Setting and regularly updating the track shift rate in PHD2 to match the custom tracking rate for the parent target. PHD2 does this by moving the expected position for the lock star at the specified rate, thereby relying on guiding to follow the non-sidereal object.
* Setting and regularly updating the tracking rate of your telescope to match the custom tracking rate for the parent target. This works only if your mount supports it. If it does, you can use it together with PHD2 track shifting

# Object Types #

* Planets, the Sun, and the Moon. No internet data are required for this.
* Comets. Data are provided by JPL Horizon
* Numbered Asteroids. Data are provided by JPL Horizon
* Un-numbered Asteroids. Data are provided by JPL Horizon
* James-Webb Space Telescope. Data are provided by JPL Horizon

# Object Types Coming Soon #

* Curated list of satellites that move slowly in the sky, such as JWST

# Getting Help #

If you have questions, come ask in the **#plugin-discussions** channel on the NINA [Discord chat server](https://discord.com/invite/rWRbVbw).
* Orbitals is provided 'as is' under the terms of the [Mozilla Public License 2.0](https://github.com/ghilios/NINA.Joko.Plugin.Orbitals/blob/develop/LICENSE.txt)
* Source code for this plugin is available at this plugin's [source code repository](https://github.com/ghilios/NINA.Joko.Plugin.Orbitals)
")]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]
// [Unused]
[assembly: AssemblyConfiguration("")]
// [Unused]
[assembly: AssemblyTrademark("")]
// [Unused]
[assembly: AssemblyCulture("")]
[assembly: CLSCompliant(false)]