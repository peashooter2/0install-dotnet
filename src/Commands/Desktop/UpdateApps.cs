// Copyright Bastian Eicher et al.
// Licensed under the GNU Lesser Public License

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NanoByte.Common;
using NanoByte.Common.Collections;
using NanoByte.Common.Tasks;
using ZeroInstall.Commands.Properties;
using ZeroInstall.DesktopIntegration;
using ZeroInstall.Model;
using ZeroInstall.Model.Selection;

namespace ZeroInstall.Commands.Desktop
{
    /// <summary>
    /// Updates all applications in the <see cref="AppList"/>.
    /// </summary>
    public class UpdateApps : IntegrationCommand
    {
        #region Metadata
        /// <summary>The name of this command as used in command-line arguments in lower-case.</summary>
        public const string Name = "update-all";

        /// <summary>The alternative name of this command as used in command-line arguments in lower-case.</summary>
        public const string AltName = "update-apps";

        /// <inheritdoc/>
        public override string Description => Resources.DescriptionUpdateApps;

        /// <inheritdoc/>
        public override string Usage => "[OPTIONS]";

        /// <inheritdoc/>
        protected override int AdditionalArgsMax => 0;
        #endregion

        #region State
        private bool _clean;

        /// <inheritdoc/>
        public UpdateApps(ICommandHandler handler)
            : base(handler)
        {
            Options.Add("c|clean", () => Resources.OptionClean, _ => _clean = true);
        }
        #endregion

        /// <inheritdoc/>
        public override ExitCode Execute()
        {
            var implementations = SolveAll(GetApps());
            DownloadUncachedImplementations(implementations);
            SelfUpdateCheck();

            if (_clean)
            {
                Handler.CancellationToken.ThrowIfCancellationRequested();
                Clean(implementations);
            }

            return ExitCode.OK;
        }

        private IEnumerable<Requirements> GetApps()
            => from entry in AppList.LoadSafe(MachineWide).Entries
               where entry.AutoUpdate
               where entry.Hostname == null || Regex.IsMatch(Environment.MachineName, entry.Hostname)
               select entry.Requirements ?? new Requirements(entry.InterfaceUri);

        private ICollection<ImplementationSelection> SolveAll(IEnumerable<Requirements> apps)
        {
            FeedManager.Refresh = true;

            var result = new ConcurrentSet<ImplementationSelection>(ManifestDigestPartialEqualityComparer<ImplementationSelection>.Instance);

            try
            {
                Parallel.ForEach(
                    apps,
                    new() {CancellationToken = Handler.CancellationToken, MaxDegreeOfParallelism = Config.MaxParallelDownloads},
                    requirements => result.AddRange(Solver.Solve(requirements).Implementations));
            }
            #region Error handling
            catch (AggregateException ex)
            {
                ex.InnerExceptions.FirstOrDefault()?.Rethrow();
                throw;
            }
            #endregion

            return result;
        }

        private void DownloadUncachedImplementations(IEnumerable<ImplementationSelection> implementations)
        {
            var uncachedImplementations = SelectionsManager.GetUncachedSelections(new Selections(implementations)).ToList();
            if (uncachedImplementations.Count == 0) return;

            Handler.ShowSelections(new Selections(uncachedImplementations), FeedManager);

            try
            {
                Fetcher.Fetch(SelectionsManager.GetImplementations(uncachedImplementations));
            }
            #region Error handling
            catch
            {
                // Suppress any left-over errors if the user canceled anyway
                Handler.CancellationToken.ThrowIfCancellationRequested();
                throw;
            }
            #endregion
        }

        private void Clean(IEnumerable<ImplementationSelection> implementations)
        {
            var digestsToKeep = implementations.Select(x => x.ManifestDigest);
            var digestsToRemove = ImplementationStore.ListAll().Except(digestsToKeep, ManifestDigestPartialEqualityComparer.Instance);
            Handler.RunTask(ForEachTask.Create(
                name: Resources.RemovingOutdated,
                target: digestsToRemove.ToList(),
                work: digest => ImplementationStore.Remove(digest, Handler)));
        }
    }
}
