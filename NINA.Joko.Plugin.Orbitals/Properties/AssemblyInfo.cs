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
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]

// [MANDATORY] The name of your plugin
[assembly: AssemblyTitle("Orbitals")]
// [MANDATORY] A short description of your plugin
[assembly: AssemblyDescription("Downloads publically available orbital data for near-earth objects to target and track Comets, Asteroids, and other objects")]

// The following attributes are not required for the plugin per se, but are required by the official manifest meta data

// Your name
[assembly: AssemblyCompany("George Hilios (jokogeo)")]
// The product name that this plugin is part of
[assembly: AssemblyProduct("Orbitals")]
[assembly: AssemblyCopyright("Copyright ©  2021")]

// The minimum Version of N.I.N.A. that this plugin is compatible with
[assembly: AssemblyMetadata("MinimumApplicationVersion", "2.0.0.2050")]

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
[assembly: AssemblyMetadata("Tags", "Asteroids,Comets,Orbital")]

//[Optional] A link that will show a log of all changes in between your plugin's versions
[assembly: AssemblyMetadata("ChangelogURL", "https://github.com/ghilios/NINA.Joko.Plugin.Orbitals/commits/develop")]

//[Optional] The url to a featured logo that will be displayed in the plugin list next to the name
/*
[assembly: AssemblyMetadata("FeaturedImageURL", "https://github.com/ghilios/NINA.Joko.Plugin.Orbitals/releases/download/resources/GM1000HPS.jpg")]
//[Optional] A url to an example screenshot of your plugin in action
[assembly: AssemblyMetadata("ScreenshotURL", "https://github.com/ghilios/NINA.Joko.Plugin.Orbitals/releases/download/resources/TenMicronToolsScreenshot.JPG")]
//[Optional] An additional url to an example example screenshot of your plugin in action
[assembly: AssemblyMetadata("AltScreenshotURL", "https://github.com/ghilios/NINA.Joko.Plugin.Orbitals/releases/download/resources/TenMicronToolsAltScreenshot.JPG")]
//[Optional] An in-depth description of your plugin
*/
[assembly: AssemblyMetadata("LongDescription", @"TODO: Description

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