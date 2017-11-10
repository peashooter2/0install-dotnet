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

#if !NETSTANDARD2_0
using System;
using System.IO;
using Microsoft.Deployment.Compression;
using Microsoft.Deployment.Compression.Cab;
using NanoByte.Common.Native;
using ZeroInstall.Store.Properties;

namespace ZeroInstall.Store.Implementations.Archives
{
    /// <summary>
    /// Common base class for exractors for Microsoft archive formats.
    /// </summary>
    public abstract class MicrosoftExtractor : ArchiveExtractor, IUnpackStreamContext
    {
        protected readonly CabEngine CabEngine = new CabEngine();
        protected Stream CabStream;

        protected MicrosoftExtractor(string targetPath)
            : base(targetPath)
        {
            if (!WindowsUtils.IsWindows) throw new NotSupportedException(Resources.ExtractionOnlyOnWindows);
        }

        Stream IUnpackStreamContext.OpenArchiveReadStream(int archiveNumber, string archiveName, CompressionEngine compressionEngine)
        {
            return new DuplicateStream(CabStream);
        }

        void IUnpackStreamContext.CloseArchiveReadStream(int archiveNumber, string archiveName, Stream stream)
        {}

        private long _bytesStaged;

        Stream IUnpackStreamContext.OpenFileWriteStream(string path, long fileSize, DateTime lastWriteTime)
        {
            #region Sanity checks
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            #endregion

            CancellationToken.ThrowIfCancellationRequested();

            string relativePath = GetRelativePath(path);
            if (relativePath == null) return null;

            _bytesStaged = fileSize;

            string absolutePath = DirectoryBuilder.NewFilePath(relativePath, lastWriteTime);
            return File.Create(absolutePath);
        }

        void IUnpackStreamContext.CloseFileWriteStream(string path, Stream stream, FileAttributes attributes, DateTime lastWriteTime)
        {
            #region Sanity checks
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            #endregion

            stream.Dispose();
            UnitsProcessed += _bytesStaged;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CabStream?.Dispose();
                CabEngine.Dispose();
            }
        }
    }
}
#endif
