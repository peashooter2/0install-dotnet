﻿// Copyright Bastian Eicher et al.
// Licensed under the GNU Lesser Public License

using System.IO;
using NanoByte.Common.Streams;

namespace ZeroInstall.Store.Implementations.Archives
{
    /// <summary>
    /// Common test cases for <see cref="IArchiveBuilder"/> implementations.
    /// </summary>
    public abstract class ArchiveBuilderTestBase
    {
        protected virtual Stream GetArchiveStream()
        {
            var stream = new MemoryStream();
            using (var builder = NewBuilder(stream))
                AddElements(builder);
            return new MemoryStream(stream.AsArray());
        }

        protected abstract IArchiveBuilder NewBuilder(Stream stream);

        protected abstract void AddElements(IForwardOnlyBuilder builder);
    }
}
