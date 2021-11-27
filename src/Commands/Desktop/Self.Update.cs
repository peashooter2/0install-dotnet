// Copyright Bastian Eicher et al.
// Licensed under the GNU Lesser Public License

using System;
using System.Collections.Generic;
using System.Net;
using NanoByte.Common;
using NanoByte.Common.Storage;
using NDesk.Options;
using ZeroInstall.Commands.Basic;
using ZeroInstall.Commands.Properties;
using ZeroInstall.Model;
using ZeroInstall.Services.Solvers;

namespace ZeroInstall.Commands.Desktop
{
    partial class Self
    {
        /// <summary>
        /// Updates Zero Install itself to the most recent version.
        /// </summary>
#if NETFRAMEWORK
        [System.Diagnostics.CodeAnalysis.SuppressMessage("ReSharper", "AssignNullToNotNullAttribute")]
#endif
        public class Update : Download, ICliSubCommand
        {
            public new const string Name = "update";

            public const string TopLevelName = "self-update";

            public string ParentName => Self.Name;

            /// <inheritdoc/>
            public override string Description => Resources.DescriptionSelfUpdate;

            /// <inheritdoc/>
            public override string Usage => "[OPTIONS]";

            /// <summary>Perform the update even if the currently installed version is the same or newer.</summary>
            private bool _force;

            /// <summary>Restart the <see cref="Central"/> GUI after the update.</summary>
            private bool _restartCentral;

            /// <summary>
            /// Creates a new self update command.
            /// </summary>
            /// <param name="handler">A callback object used when the the user needs to be asked questions or informed about download and IO tasks.</param>
            public Update(ICommandHandler handler)
                : base(handler, outputOptions: false, refreshOptions: false, customizeOptions: false)
            {
                Options.Add("force", () => Resources.OptionForceSelfUpdate, _ => _force = true);
                Options.Add("restart-central", () => Resources.OptionRestartCentral, _ => _restartCentral = true);
            }

            /// <inheritdoc/>
            public override void Parse(IReadOnlyList<string> args)
            {
                // NOTE: Does not call base method

                if (Options.Parse(args).Count != 0) throw new OptionException(Resources.TooManyArguments + Environment.NewLine + AdditionalArgs.JoinEscapeArguments(), null);

                SetInterfaceUri(Config.SelfUpdateUri ?? throw new UriFormatException(Resources.SelfUpdateDisabled));
                if (ProgramUtils.GuiAssemblyName != null) Requirements.Command = Command.NameRunGui;

                FeedManager.Refresh = true;
            }

            /// <inheritdoc/>
            public override ExitCode Execute()
            {
                // NOTE: Does not call base method

                if (!ZeroInstallInstance.IsDeployed) throw new NotSupportedException(Resources.SelfUpdateBlocked);

                try
                {
                    Solve();
                }
                #region Error handling
                catch (WebException ex) when (Handler.Background)
                {
                    Log.Info("Suppressed network-related error message due to background mode");
                    Log.Info(ex);
                    return ExitCode.WebError;
                }
                catch (SolverException ex) when (Handler.Background)
                {
                    Log.Info("Suppressed Solver-related error message due to background mode");
                    Log.Info(ex);
                    return ExitCode.SolverError;
                }
                #endregion

                if (UpdateFound())
                {
                    DownloadUncachedImplementations();

                    Handler.CancellationToken.ThrowIfCancellationRequested();
                    if (!Handler.Ask(string.Format(Resources.SelfUpdateAvailable, Selections!.MainImplementation.Version), defaultAnswer: true))
                        throw new OperationCanceledException();

                    var builder = Executor.Inject(Selections).AddArguments(Self.Name, Deploy.Name, "--batch", Locations.InstallBase);
                    if (Handler.Background) builder.AddArguments("--background");
                    if (_restartCentral) builder.AddArguments("--restart-central");
                    if (ZeroInstallInstance.IsLibraryMode) builder.AddArguments("--library");
                    builder.Start();

                    return ExitCode.OK;
                }
                else return ExitCode.OK;
            }

            private bool UpdateFound()
                => _force || (Selections != null && Selections.MainImplementation.Version > ZeroInstallInstance.Version);
        }
    }
}
