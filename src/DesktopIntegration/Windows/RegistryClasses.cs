﻿// Copyright Bastian Eicher et al.
// Licensed under the GNU Lesser Public License

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using Microsoft.Win32;
using NanoByte.Common;
using NanoByte.Common.Native;
using ZeroInstall.Model;
using ZeroInstall.Model.Capabilities;
using ZeroInstall.Store.Icons;

namespace ZeroInstall.DesktopIntegration.Windows
{
    /// <summary>
    /// Helpers for registering <see cref="Capability"/>s in the HKCR section of the Windows Registry.
    /// </summary>
    internal static class RegistryClasses
    {
        /// <summary>
        /// Prepended before any programmatic identifiers used by Zero Install in the registry. This prevents conflicts with non-Zero Install installations.
        /// </summary>
        public const string Prefix = "ZeroInstall.";

        /// <summary>
        /// Prepended before any registry purpose flags. Purpose flags indicate what a registry key was created for and whether it is still required.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1726:UsePreferredTerms", MessageId = "Flag")]
        public const string PurposeFlagPrefix = "ZeroInstall.";

        /// <summary>
        /// Indicates a registry key is required by a capability.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1726:UsePreferredTerms", MessageId = "Flag")]
        public const string PurposeFlagCapability = PurposeFlagPrefix + "Capability";

        /// <summary>
        /// Indicates a registry key is required by an access point.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1726:UsePreferredTerms", MessageId = "Flag")]
        public const string PurposeFlagAccessPoint = PurposeFlagPrefix + "AccessPoint";

        /// <summary>
        /// Opens the HKCU/HKLM registry key backing HKCR.
        /// </summary>
        public static RegistryKey OpenHive(bool machineWide)
            => (machineWide ? Registry.LocalMachine : Registry.CurrentUser).OpenSubKeyChecked(@"SOFTWARE\Classes", writable: true);

        /// <summary>
        /// Registers a <see cref="VerbCapability"/> in a registry key.
        /// </summary>
        /// <param name="registryKey">The registry key to write the new data to.</param>
        /// <param name="target">The application being integrated.</param>
        /// <param name="capability">The capability to register.</param>
        /// <param name="iconStore">Stores icon files downloaded from the web as local files.</param>
        /// <param name="machineWide">Assume <paramref name="registryKey"/> is effective machine-wide instead of just for the current user.</param>
        /// <exception cref="OperationCanceledException">The user canceled the task.</exception>
        /// <exception cref="IOException">A problem occurred while writing to the filesystem or registry.</exception>
        /// <exception cref="WebException">A problem occurred while downloading additional data (such as icons).</exception>
        /// <exception cref="UnauthorizedAccessException">Write access to the filesystem or registry is not permitted.</exception>
        public static void Register(RegistryKey registryKey, FeedTarget target, VerbCapability capability, IIconStore iconStore, bool machineWide)
        {
            #region Sanity checks
            if (capability == null) throw new ArgumentNullException(nameof(capability));
            if (iconStore == null) throw new ArgumentNullException(nameof(iconStore));
            #endregion

            var icon = capability.GetIcon(Icon.MimeTypeIco) ?? target.Feed.Icons.GetIcon(Icon.MimeTypeIco);
            if (icon != null)
            {
                using var iconKey = registryKey.CreateSubKeyChecked("DefaultIcon");
                iconKey.SetValue("", iconStore.GetPath(icon) + ",0");
            }

            foreach (var verb in capability.Verbs)
            {
                using var verbKey = registryKey.CreateSubKeyChecked($@"shell\{verb.Name}");
                Register(verbKey, target, verb, iconStore, machineWide);
            }

            // Prevent conflicts with existing entries
            registryKey.DeleteSubKeyTree(@"shell\ddeexec", throwOnMissingSubKey: false);
        }

        /// <summary>
        /// Registers a <see cref="Verb"/> in a registry key.
        /// </summary>
        /// <param name="verbKey">The registry key to write the new data to.</param>
        /// <param name="target">The application being integrated.</param>
        /// <param name="verb">The verb to register.</param>
        /// <param name="iconStore">Stores icon files downloaded from the web as local files.</param>
        /// <param name="machineWide">Assume <paramref name="verbKey"/> is effective machine-wide instead of just for the current user.</param>
        /// <exception cref="OperationCanceledException">The user canceled the task.</exception>
        /// <exception cref="IOException">A problem occurred while writing to the filesystem or registry.</exception>
        /// <exception cref="WebException">A problem occurred while downloading additional data (such as icons).</exception>
        /// <exception cref="UnauthorizedAccessException">Write access to the filesystem or registry is not permitted.</exception>
        public static void Register(RegistryKey verbKey, FeedTarget target, Verb verb, IIconStore iconStore, bool machineWide)
        {
            string? description = verb.Descriptions.GetBestLanguage(CultureInfo.CurrentUICulture);
            if (!string.IsNullOrEmpty(description))
            {
                verbKey.SetValue("", description);
                verbKey.SetValue("MUIVerb", description);
            }

            if (verb.Extended) verbKey.SetValue("Extended", "");

            var icon = target.Feed.GetBestIcon(Icon.MimeTypeIco, verb.Command);
            if (icon != null) verbKey.SetValue("Icon", iconStore.GetPath(icon));

            using var commandKey = verbKey.CreateSubKeyChecked("command");
            commandKey.SetValue("", GetLaunchCommandLine(target, verb, iconStore, machineWide));
        }

        /// <summary>
        /// Generates a command-line string for launching a <see cref="Verb"/>.
        /// </summary>
        /// <param name="target">The application being integrated.</param>
        /// <param name="verb">The verb to get to launch command for.</param>
        /// <param name="iconStore">Stores icon files downloaded from the web as local files.</param>
        /// <param name="machineWide">Store the stub in a machine-wide directory instead of just for the current user.</param>
        /// <exception cref="IOException">A problem occurred while writing to the filesystem.</exception>
        /// <exception cref="WebException">A problem occurred while downloading additional data (such as icons).</exception>
        /// <exception cref="InvalidOperationException">Write access to the filesystem is not permitted.</exception>
        internal static string GetLaunchCommandLine(FeedTarget target, Verb verb, IIconStore iconStore, bool machineWide)
        {
            IEnumerable<string> GetCommandLine()
            {
                try
                {
                    return new StubBuilder(iconStore).GetRunCommandLine(target, verb.Command, machineWide);
                }
                #region Error handling
                catch (InvalidOperationException ex)
                {
                    // Wrap exception since only certain exception types are allowed
                    throw new IOException(ex.Message, ex);
                }
                #endregion
            }

            if (verb.Arguments.Count == 0)
            {
                string arguments = string.IsNullOrEmpty(verb.ArgumentsLiteral) ? "\"%V\"" : verb.ArgumentsLiteral;
                return GetCommandLine().JoinEscapeArguments() + " " + arguments;
            }

            return GetCommandLine()
                  .Concat(verb.Arguments.Select(x => x.Value))
                  .JoinEscapeArguments()
                  .Replace("${item}", "\"%V\"");
        }
    }
}
