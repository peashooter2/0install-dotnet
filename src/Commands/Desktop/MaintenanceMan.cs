// Copyright Bastian Eicher et al.
// Licensed under the GNU Lesser Public License

using System;
using System.Collections.Generic;
using System.IO;
using JetBrains.Annotations;
using NanoByte.Common;
using NanoByte.Common.Native;
using NDesk.Options;
using ZeroInstall.Commands.Properties;

namespace ZeroInstall.Commands.Desktop
{
    /// <summary>
    /// Manages the integration of Zero Install itself in the operating system (deployment and removal).
    /// </summary>
    public sealed partial class MaintenanceMan : CliMultiCommand
    {
        #region Metadata
        /// <summary>The name of this command as used in command-line arguments in lower-case.</summary>
        public new const string Name = "maintenance";

        /// <inheritdoc/>
        public MaintenanceMan([NotNull] ICommandHandler handler)
            : base(handler)
        {}
        #endregion

        /// <inheritdoc/>
        public override IEnumerable<string> SubCommandNames => new[] {Deploy.Name, Remove.Name};

        /// <inheritdoc/>
        public override CliSubCommand GetCommand(string commandName)
            => (commandName ?? throw new ArgumentNullException(nameof(commandName))) switch
            {
                Deploy.Name => (CliSubCommand)new Deploy(Handler),
                Remove.Name => new Remove(Handler),
                RemoveHelper.Name => new RemoveHelper(Handler),
                _ => throw new OptionException(string.Format(Resources.UnknownCommand, commandName), commandName)
            };

        public abstract class MaintenanceSubCommand : CliSubCommand
        {
            protected override string ParentName => MaintenanceMan.Name;

            protected MaintenanceSubCommand([NotNull] ICommandHandler handler)
                : base(handler)
            {}

            /// <summary>
            /// Tries to find an existing instance of Zero Install deployed on this system.
            /// </summary>
            /// <param name="machineWide"><c>true</c> to look only for machine-wide instances; <c>false</c> to look only for instances in the current user profile.</param>
            /// <returns>The installation directory of an instance of Zero Install; <c>null</c> if none was found.</returns>
            [CanBeNull]
            protected static string FindExistingInstance(bool machineWide)
            {
                if (!WindowsUtils.IsWindows) return null;

                string installLocation = RegistryUtils.GetSoftwareString("Zero Install", "InstallLocation", machineWide);
                if (string.IsNullOrEmpty(installLocation)) return null;
                if (!File.Exists(Path.Combine(installLocation, "0install.exe"))) return null;
                return installLocation;
            }
        }
    }
}
