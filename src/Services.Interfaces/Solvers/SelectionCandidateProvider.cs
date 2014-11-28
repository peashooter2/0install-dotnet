﻿/*
 * Copyright 2010-2014 Bastian Eicher
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
using NanoByte.Common;
using NanoByte.Common.Collections;
using NanoByte.Common.Info;
using NanoByte.Common.Native;
using ZeroInstall.Services.Feeds;
using ZeroInstall.Store;
using ZeroInstall.Store.Implementations;
using ZeroInstall.Store.Model;
using ZeroInstall.Store.Model.Preferences;
using ZeroInstall.Store.Model.Selection;

namespace ZeroInstall.Services.Solvers
{
    /// <summary>
    /// Generates <see cref="SelectionCandidate"/>s for <see cref="ISolver"/>s to choose among.
    /// </summary>
    /// <remarks>Caches loaded <see cref="Feed"/>s, preferences, etc..</remarks>
    public class SelectionCandidateProvider
    {
        #region Depdendencies
        private readonly Config _config;

        /// <summary>
        /// Creates a new <see cref="SelectionCandidate"/> provider.
        /// </summary>
        /// <param name="config">User settings controlling network behaviour, solving, etc.</param>
        /// <param name="feedManager">Provides access to remote and local <see cref="Feed"/>s. Handles downloading, signature verification and caching.</param>
        /// <param name="store">Used to check which <see cref="Implementation"/>s are already cached.</param>
        public SelectionCandidateProvider(Config config, IFeedManager feedManager, IStore store)
        {
            #region Sanity checks
            if (config == null) throw new ArgumentNullException("config");
            if (feedManager == null) throw new ArgumentNullException("feedManager");
            if (store == null) throw new ArgumentNullException("store");
            #endregion

            _config = config;

            var implementations = store.ListAll();
            _isCached = new TransparentCache<ManifestDigest, bool>(x => implementations.Contains(x, ManifestDigestPartialEqualityComparer.Instance));
            //_isCached = new TransparentCache<ManifestDigest, bool>(store.Contains);

            _comparer = new TransparentCache<FeedUri, SelectionCandidateComparer>(id => new SelectionCandidateComparer(config, _isCached, _interfacePreferences[id].StabilityPolicy));
            _feeds = new TransparentCache<FeedUri, Feed>(feedManager.GetFeed);
        }
        #endregion

        #region Caches
        /// <summary>Maps interface URIs to <see cref="InterfacePreferences"/>.</summary>
        private readonly TransparentCache<FeedUri, InterfacePreferences> _interfacePreferences = new TransparentCache<FeedUri, InterfacePreferences>(InterfacePreferences.LoadForSafe);

        /// <summary>Maps interface URIs to <see cref="SelectionCandidateComparer"/>s.</summary>
        private readonly TransparentCache<FeedUri, SelectionCandidateComparer> _comparer;

        /// <summary>Maps feed URIs to <see cref="Feed"/>s. Transparent caching ensures individual feeds do not change during solver run.</summary>
        private readonly TransparentCache<FeedUri, Feed> _feeds;

        /// <summary>Indicates which implementations (identified by <see cref="ManifestDigest"/>) are already cached in the <see cref="IStore"/>.</summary>
        private readonly TransparentCache<ManifestDigest, bool> _isCached;
        #endregion

        /// <summary>
        /// Gets all <see cref="SelectionCandidate"/>s for a specific set of <see cref="Requirements"/> sorted from best to worst.
        /// </summary>
        public IList<SelectionCandidate> GetSortedCandidates(Requirements requirements)
        {
            var candidates = GetFeeds(requirements)
                .SelectMany(x => GetCandidates(x.Key, x.Value, requirements))
                .ToList();
            candidates.Sort(_comparer[requirements.InterfaceUri]);
            return candidates;
        }

        private IDictionary<FeedUri, Feed> GetFeeds(Requirements requirements)
        {
            var dictionary = new Dictionary<FeedUri, Feed>();

            AddFeed(dictionary, requirements.InterfaceUri, requirements);
            foreach (var reference in _interfacePreferences[requirements.InterfaceUri].Feeds)
                AddFeed(dictionary, reference.Source, requirements);

            return dictionary;
        }

        private void AddFeed(IDictionary<FeedUri, Feed> dictionary, FeedUri feedUri, Requirements requirements)
        {
            if (dictionary.ContainsKey(feedUri)) return;

            var feed = _feeds[feedUri];
            if (feed.MinInjectorVersion != null && new ImplementationVersion(AppInfo.Current.Version) < feed.MinInjectorVersion)
            {
                Log.Warn(string.Format("The solver version is too old. The feed '{0}' requires at least version {1} but the installed version is {2}. Try updating Zero Install.", feedUri, feed.MinInjectorVersion, AppInfo.Current.Version));
                return;
            }

            dictionary.Add(feedUri, feed);
            foreach (var reference in feed.Feeds
                .Where(reference => reference.Architecture.IsCompatible(requirements.Architecture) &&
                                    reference.Languages.ContainsAny(requirements.Languages)))
                AddFeed(dictionary, reference.Source, requirements);
        }

        private IEnumerable<SelectionCandidate> GetCandidates(FeedUri feedUri, Feed feed, Requirements requirements)
        {
            if (UnixUtils.IsUnix && feed.Elements.OfType<PackageImplementation>().Any())
                Log.Warn("Linux native package managers not supported yet!");
            // TODO: Windows <package-implementation>s

            var feedPreferences = FeedPreferences.LoadForSafe(feedUri);
            return
                from implementation in feed.Elements.OfType<Implementation>()
                select new SelectionCandidate(feedUri, feedPreferences, implementation, requirements,
                    offlineUncached: (_config.NetworkUse == NetworkLevel.Offline) && !_isCached[implementation.ManifestDigest]);
        }

        /// <summary>
        /// Retrieves the original <see cref="Implementation"/> an <see cref="ImplementationSelection"/> was based ofF.
        /// </summary>
        public Implementation LookupOriginalImplementation(ImplementationSelection implemenationSelection)
        {
            #region Sanity checks
            if (implemenationSelection == null) throw new ArgumentNullException("implemenationSelection");
            #endregion

            return _feeds[implemenationSelection.FromFeed ?? implemenationSelection.InterfaceUri][implemenationSelection.ID];
        }
    }
}
