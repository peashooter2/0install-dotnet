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

using System.Collections.Generic;
using JetBrains.Annotations;
using Moq;
using NanoByte.Common.Collections;

namespace ZeroInstall
{
    /// <summary>
    /// Extension methods for <see cref="Moq"/>.
    /// </summary>
    public static class MoqExtensions
    {
        /// <summary>
        /// Ensures a collection is equal to this one (same elements in same order).
        /// </summary>
        [Matcher]
        public static ICollection<T> IsEqual<T>([NotNull] this ICollection<T> expected)
            => Match.Create<ICollection<T>>(x => x.SequencedEquals(expected));

        /// <summary>
        /// Ensures a collection is equivalet to this one (same elements in any order).
        /// </summary>
        [Matcher]
        public static ICollection<T> IsEquivalent<T>([NotNull] this ICollection<T> expected)
            => Match.Create<ICollection<T>>(x => x.UnsequencedEquals(expected));
    }
}
