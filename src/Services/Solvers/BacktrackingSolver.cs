﻿/*
 * Copyright 2010-2017 Bastian Eicher
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
using System.Linq;
using JetBrains.Annotations;
using NanoByte.Common;
using NanoByte.Common.Collections;
using NanoByte.Common.Dispatch;
using NanoByte.Common.Tasks;
using ZeroInstall.Services.PackageManagers;
using ZeroInstall.Services.Properties;
using ZeroInstall.Store;
using ZeroInstall.Store.Model;
using ZeroInstall.Store.Model.Selection;

namespace ZeroInstall.Services.Solvers
{
    /// <summary>
    /// Uses limited backtracking to solve <see cref="Requirements"/>. Does not find all possible solutions!
    /// </summary>
    public class BacktrackingSolver : ISolver
    {
        private readonly ISelectionCandidateProvider _candidateProvider;
        private readonly ITaskHandler _handler;

        /// <summary>
        /// Creates a new backtracking solver.
        /// </summary>
        /// <param name="candidateProvider">Generates <see cref="SelectionCandidate"/>s for the solver to choose among.</param>
        /// <param name="handler">A callback object used when the the user needs to be asked questions or informed about download and IO tasks.</param>
        public BacktrackingSolver([NotNull] ISelectionCandidateProvider candidateProvider, [NotNull] ITaskHandler handler)
        {
            _candidateProvider = candidateProvider ?? throw new ArgumentNullException(nameof(candidateProvider));
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        /// <inheritdoc/>
        public Selections Solve(Requirements requirements)
        {
            #region Sanity checks
            if (requirements == null) throw new ArgumentNullException(nameof(requirements));
            if (requirements.InterfaceUri == null) throw new ArgumentException(Resources.MissingInterfaceUri, nameof(requirements));
            #endregion

            Log.Info($"Running Backtracking Solver for {requirements}");

            _candidateProvider.Clear();
            var successfulAttempt = requirements
                .GetNormalizedAlternatives()
                .Select(req => new Attempt(req, _handler.CancellationToken, _candidateProvider))
                .FirstOrDefault(x => x.Successful);

            if (successfulAttempt == null) throw new SolverException("No solution found");
            return successfulAttempt.Selections;
        }

        /// <summary>
        /// Represents a single attempt to solve specific <see cref="Requirements"/>.
        /// </summary>
        private class Attempt
        {
            private readonly CancellationToken _cancellationToken;
            private readonly ISelectionCandidateProvider _candidateProvider;
            private readonly Requirements _topLevelRequirements;

            public Selections Selections { private set; get; }
            public bool Successful { get; }

            public Attempt([NotNull] Requirements requirements, CancellationToken cancellationToken, [NotNull] ISelectionCandidateProvider candidateProvider)
            {
                _cancellationToken = cancellationToken;
                _candidateProvider = candidateProvider;
                _topLevelRequirements = requirements ?? throw new ArgumentNullException(nameof(requirements));

                Selections = new Selections
                {
                    InterfaceUri = requirements.InterfaceUri,
                    Command = requirements.Command
                };
                Successful = TryToMeet(Demand(requirements));
                Selections.PurgeRestrictions();
                Selections.Implementations.Sort();
            }

            private bool TryToMeet([NotNull, ItemNotNull] IEnumerable<SolverDemand> demands)
            {
                var essential = new List<SolverDemand>();
                var recommended = new List<SolverDemand>();
                demands.Bucketize(x => x.Importance)
                    .Add(Importance.Essential, essential)
                    .Add(Importance.Recommended, recommended)
                    .Run();

                // Quickly reject impossible sets of demands
                if (essential.Any(demand => !demand.Candidates.Any(candidate => candidate.IsSuitable))) return false;

                var backtrackingSnapshot = Selections.Clone();
                foreach (var premutation in essential.Permutate())
                {
                    if (premutation.All(TryToMeet))
                    {
                        recommended.ForEach(x => TryToMeet(x));
                        return true;
                    }
                    else Selections = backtrackingSnapshot.Clone();
                }
                return false;
            }

            private bool TryToMeet([NotNull] SolverDemand demand)
            {
                _cancellationToken.ThrowIfCancellationRequested();

                var compatibleCandidates = GetCompatibleCandidates(demand);

                var existingSelection = Selections.GetImplementation(demand.Requirements.InterfaceUri);
                if (existingSelection == null)
                { // Try to make new selection
                    foreach (var selection in compatibleCandidates.Select(x => x.ToSelection(demand.Candidates, demand.Requirements)))
                    {
                        Selections.Implementations.Add(selection);
                        if (TryToMeet(DemandsFor(selection, demand.Requirements))) return true;
                        else
                        {
                            Selections.Implementations.RemoveLast();
                            return false;
                        }
                    }
                    return false;
                }
                else
                { // Try to use existing selection
                    if (!compatibleCandidates.Contains(existingSelection)) return false;

                    if (!existingSelection.ContainsCommand(demand.Requirements.Command))
                    { // Add additional command to selection if needed
                        var command = existingSelection.AddCommand(demand.Requirements, from: _candidateProvider.LookupOriginalImplementation(existingSelection));
                        return (command == null) || TryToMeet(DemandsFor(command, demand.Requirements.InterfaceUri));
                    }
                    return true;
                }
            }

            [NotNull]
            private IEnumerable<SelectionCandidate> GetCompatibleCandidates([NotNull] SolverDemand demand) => demand.Candidates.Where(candidate =>
            {
                if (!candidate.IsSuitable) return false;

                var nativeImplementation = candidate.Implementation as ExternalImplementation;

                // Ensure the candidate does not conflict with restricions of existing selections
                foreach (var restriction in Selections.RestrictionsFor(demand.Requirements.InterfaceUri))
                {
                    if (restriction.Versions != null && !restriction.Versions.Match(candidate.Version)) return false;
                    if (nativeImplementation != null && !restriction.Distributions.ContainsOrEmpty(nativeImplementation.Distribution)) return false;
                }

                // Ensure the existing selections do not conflict with restrictions of the candidate
                foreach (var restriction in candidate.Implementation.GetEffectiveRestrictions())
                {
                    var selection = Selections.GetImplementation(restriction.InterfaceUri);
                    if (selection != null)
                    {
                        if (restriction.Versions != null && !restriction.Versions.Match(selection.Version)) return false;
                        if (nativeImplementation != null && !restriction.Distributions.ContainsOrEmpty(nativeImplementation.Distribution)) return false;
                    }
                }

                return true;
            });

            [NotNull, ItemNotNull]
            private IEnumerable<SolverDemand> DemandsFor([NotNull] ImplementationSelection selection, [NotNull] Requirements requirements)
            {
                foreach (var demand in selection.Dependencies.SelectMany(DemandsFor))
                    yield return demand;

                foreach (var demand in BuildRequirements(selection, selection.InterfaceUri).Select(x => Demand(x)))
                    yield return demand;

                var command = selection[requirements.Command ?? Command.NameRun];
                if (command != null)
                {
                    foreach (var demand in DemandsFor(command, requirements.InterfaceUri))
                        yield return demand;
                }
            }

            [NotNull, ItemNotNull]
            private IEnumerable<SolverDemand> DemandsFor([NotNull] Dependency dependency)
            {
                {
                    var requirements = BuildRequirements(dependency.InterfaceUri, command: "");
                    requirements.Distributions.AddRange(dependency.Distributions);
                    requirements.AddRestriction(dependency);
                    yield return Demand(requirements, dependency.Importance);
                }

                foreach (var requirements in BuildRequirements(dependency, dependency.InterfaceUri))
                    yield return Demand(requirements, dependency.Importance);
            }

            [NotNull, ItemNotNull]
            private IEnumerable<SolverDemand> DemandsFor([NotNull] Command command, [NotNull] FeedUri interfaceUri)
            {
                if (command.Bindings.OfType<ExecutableInBinding>().Any())
                    throw new NotSupportedException("<executable-in-*> not supported in <command>");

                if (command.Runner != null)
                {
                    var requirements = BuildRequirements(command.Runner.InterfaceUri, command.Runner.Command);
                    requirements.AddRestriction(command.Runner);
                    yield return Demand(requirements);
                }

                foreach (var requirements in command.Dependencies.SelectMany(DemandsFor))
                    yield return requirements;

                foreach (var requirements in BuildRequirements(command, interfaceUri))
                    yield return Demand(requirements);
            }

            [NotNull]
            private SolverDemand Demand([NotNull] Requirements requirements, Importance importance = Importance.Essential)
                => new SolverDemand(requirements, _candidateProvider, importance);

            private Requirements BuildRequirements([NotNull] FeedUri interfaceUri, [CanBeNull] string command)
            {
                var requirements = new Requirements(interfaceUri, command ?? Command.NameRun, _topLevelRequirements.Architecture);
                requirements.AddRestrictions(_topLevelRequirements);
                requirements.Languages.AddRange(_topLevelRequirements.Languages);
                return requirements;
            }

            [NotNull, ItemNotNull]
            private IEnumerable<Requirements> BuildRequirements([NotNull] IBindingContainer bindingContainer, [NotNull] FeedUri interfaceUri)
                => bindingContainer.Bindings.OfType<ExecutableInBinding>().Select(x => BuildRequirements(interfaceUri, x.Command));
        }
    }
}
