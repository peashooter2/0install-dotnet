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

#if !NETCOREAPP2_0
using System.IO;

namespace ZeroInstall.Store.Implementations.Archives
{
    /// <summary>
    /// Contains test methods for <see cref="TarLzmaGenerator"/>.
    /// </summary>
    public class TarLzmaGeneratorTest : TarGeneratorTest
    {
        protected override TarGenerator CreateGenerator(string sourceDirectory, Stream stream) => new TarLzmaGenerator(sourceDirectory, stream);

        protected override Stream BuildArchive(string sourcePath) => TarLzmaExtractor.GetDecompressionStream(base.BuildArchive(sourcePath));
    }
}
#endif
