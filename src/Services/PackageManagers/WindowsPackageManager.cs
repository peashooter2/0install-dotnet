﻿/*
 * Copyright 2010-2016 Bastian Eicher
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU Lesser Public License for more details.
 *
 * You should have received a copy of the GNU Lesser Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NanoByte.Common;
using NanoByte.Common.Collections;
using NanoByte.Common.Native;
using ZeroInstall.Store.Model;

namespace ZeroInstall.Services.PackageManagers
{
    /// <summary>
    /// Detects common Windows software packages (such as Java and .NET) that are installed natively.
    /// </summary>
    public class WindowsPackageManager : PackageManagerBase
    {
        public WindowsPackageManager()
        {
            if (!WindowsUtils.IsWindows) throw new NotSupportedException("Windows Package Manager can only be used on the Windows platform.");
        }

        /// <inheritdoc/>
        protected override string DistributionName => "Windows";

        /// <inheritdoc/>
        protected override IEnumerable<ExternalImplementation> GetImplementations(string packageName)
        {
            #region Sanity checks
            if (packageName == null) throw new ArgumentNullException(nameof(packageName));
            #endregion

            switch (packageName)
            {
                case "openjdk-6-jre":
                    return FindJre(6);
                case "openjdk-7-jre":
                    return FindJre(7);
                case "openjdk-8-jre":
                    return FindJre(8);

                case "openjdk-6-jdk":
                    return FindJdk(6);
                case "openjdk-7-jdk":
                    return FindJdk(7);
                case "openjdk-8-jdk":
                    return FindJdk(8);

                case "netfx":
                    return new[]
                    {
                        // See: https://msdn.microsoft.com/en-us/library/hh925568
                        FindNetFx(new ImplementationVersion("2.0"), WindowsUtils.NetFx20, WindowsUtils.NetFx20),
                        FindNetFx(new ImplementationVersion("3.0"), WindowsUtils.NetFx20, WindowsUtils.NetFx30),
                        FindNetFx(new ImplementationVersion("3.5"), WindowsUtils.NetFx20, WindowsUtils.NetFx35),
                        FindNetFx(new ImplementationVersion("4.0"), WindowsUtils.NetFx40, @"v4\Full"),
                        FindNetFx(new ImplementationVersion("4.5"), WindowsUtils.NetFx40, @"v4\Full", 378389),
                        FindNetFx(new ImplementationVersion("4.5.1"), WindowsUtils.NetFx40, @"v4\Full", 378675), // also covers 378758
                        FindNetFx(new ImplementationVersion("4.5.2"), WindowsUtils.NetFx40, @"v4\Full", 379893),
                        FindNetFx(new ImplementationVersion("4.6"), WindowsUtils.NetFx40, @"v4\Full", 393295), // also covers 393297
                        FindNetFx(new ImplementationVersion("4.6.1"), WindowsUtils.NetFx40, @"v4\Full", 394254),
                        FindNetFx(new ImplementationVersion("4.6.2"), WindowsUtils.NetFx40, @"v4\Full", 394802) // also covers 394806
                    }.Flatten();
                case "netfx-client":
                    return FindNetFx(new ImplementationVersion("4.0"), WindowsUtils.NetFx40, @"v4\Client");

                default:
                    return Enumerable.Empty<ExternalImplementation>();
            }
        }

        private IEnumerable<ExternalImplementation> FindJre(int version) => FindJava(version,
            typeShort: "jre",
            typeLong: "Java Runtime Environment",
            mainExe: "java",
            secondaryCommand: Command.NameRunGui,
            secondaryExe: "javaw");

        private IEnumerable<ExternalImplementation> FindJdk(int version) => FindJava(version,
            typeShort: "jdk",
            typeLong: "Java Development Kit",
            mainExe: "javac",
            secondaryCommand: "java",
            secondaryExe: "java");

        private IEnumerable<ExternalImplementation> FindJava(int version, string typeShort, string typeLong, string mainExe, string secondaryCommand, string secondaryExe)
            => from javaHome in GetRegistredPaths(@"JavaSoft\" + typeLong + @"\1." + version, "JavaHome")
                let mainPath = Path.Combine(javaHome.Value, @"bin\" + mainExe + ".exe")
                let secondaryPath = Path.Combine(javaHome.Value, @"bin\" + secondaryExe + ".exe")
                where File.Exists(mainPath) && File.Exists(secondaryPath)
                select new ExternalImplementation(DistributionName,
                    package: "openjdk-" + version + "-" + typeShort,
                    version: new ImplementationVersion(FileVersionInfo.GetVersionInfo(mainPath).ProductVersion.GetLeftPartAtLastOccurrence(".")), // Trim patch level
                    cpu: javaHome.Key)
                {
                    Commands =
                    {
                        new Command {Name = Command.NameRun, Path = mainPath},
                        new Command {Name = secondaryCommand, Path = secondaryPath}
                    },
                    IsInstalled = true,
                    QuickTestFile = mainPath
                };

        private static IEnumerable<KeyValuePair<Cpu, string>> GetRegistredPaths(string registrySuffix, string valueName)
        {
            // Check for system native architecture (may be 32-bit or 64-bit)
            string path = RegistryUtils.GetString(@"HKEY_LOCAL_MACHINE\SOFTWARE\" + registrySuffix, valueName);
            if (!string.IsNullOrEmpty(path))
                yield return new KeyValuePair<Cpu, string>(Architecture.CurrentSystem.Cpu, path);

            // Check for 32-bit on a 64-bit system
            if (OSUtils.Is64BitProcess)
            {
                path = RegistryUtils.GetString(@"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\" + registrySuffix, valueName);
                if (!string.IsNullOrEmpty(path))
                    yield return new KeyValuePair<Cpu, string>(Cpu.I486, path);
            }
        }

        // Uses detection logic described here: http://msdn.microsoft.com/library/hh925568
        private IEnumerable<ExternalImplementation> FindNetFx(ImplementationVersion version, string clrVersion, string registryVersion, int releaseNumber = 0)
        {
            ExternalImplementation Impl(Cpu cpu) => new ExternalImplementation(DistributionName, "netfx", version, cpu)
            {
                // .NET executables do not need a runner on Windows
                Commands = {new Command {Name = Command.NameRun, Path = ""}},
                IsInstalled = true,
                QuickTestFile = Path.Combine(WindowsUtils.GetNetFxDirectory(clrVersion), "mscorlib.dll")
            };

            // Check for system native architecture (may be 32-bit or 64-bit)
            int install = RegistryUtils.GetDword(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\NET Framework Setup\NDP\" + registryVersion, "Install");
            int release = RegistryUtils.GetDword(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\NET Framework Setup\NDP\" + registryVersion, "Release");
            if (install == 1 && release >= releaseNumber)
                yield return Impl(Architecture.CurrentSystem.Cpu);

            // Check for 32-bit on a 64-bit system
            if (OSUtils.Is64BitProcess)
            {
                install = RegistryUtils.GetDword(@"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Microsoft\NET Framework Setup\NDP\" + registryVersion, "Install");
                release = RegistryUtils.GetDword(@"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Microsoft\NET Framework Setup\NDP\" + registryVersion, "Release");
                if (install == 1 && release >= releaseNumber)
                    yield return Impl(Cpu.I486);
            }
        }
    }
}
