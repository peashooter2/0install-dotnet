// Copyright Bastian Eicher et al.
// Licensed under the GNU Lesser Public License

using NanoByte.Common.Dispatch;
using NanoByte.Common.Native;

namespace ZeroInstall.Store.Implementations;

/// <summary>
/// Manages a directory that stores implementations. Also known as an implementation cache.
/// </summary>
public partial class ImplementationStore : ImplementationSink, IImplementationStore, IEquatable<ImplementationStore>
{
    private readonly ITaskHandler _handler;

    /// <inheritdoc/>
    public ImplementationStoreKind Kind => ReadOnly ? ImplementationStoreKind.ReadOnly : ImplementationStoreKind.ReadWrite;

    /// <summary>
    /// Creates a new implementation store using a specific path to a directory.
    /// </summary>
    /// <param name="path">A fully qualified directory path. The directory will be created if it doesn't exist yet.</param>
    /// <param name="handler">A callback object used when the the user is to be informed about progress or asked questions.</param>
    /// <param name="useWriteProtection">Controls whether implementation directories are made write-protected once added to the store to prevent unintentional modification (which would invalidate the manifest digests).</param>
    /// <exception cref="IOException">The <paramref name="path"/> could not be created or the underlying filesystem can not store file-changed times accurate to the second.</exception>
    /// <exception cref="UnauthorizedAccessException">Creating the <paramref name="path"/> is not permitted.</exception>
    public ImplementationStore(string path, ITaskHandler handler, bool useWriteProtection = true)
        : base(path, useWriteProtection)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <inheritdoc cref="IImplementationStore.GetPath" />
    public override string? GetPath(ManifestDigest manifestDigest)
    {
        if (base.GetPath(manifestDigest) is not {} path) return null;

        static bool HasNoFiles(string path)
        {
            try
            {
                return Directory.GetFiles(path).Length == 0;
            }
            #region Error handling
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
            #endregion
        }

        if (!manifestDigest.PartialEquals(ManifestDigest.Empty) && HasNoFiles(path))
        {
            Log.Info($"Directory for {manifestDigest} has no .manifest file or any other files. Almost certainly damaged. Deleting.");
            Remove(manifestDigest);
            return null;
        }

        return path;
    }

    /// <inheritdoc/>
    public IEnumerable<ManifestDigest> ListAll()
    {
        if (!Directory.Exists(Path)) yield break;

        foreach (string path in Directory.GetDirectories(Path))
        {
            var digest = new ManifestDigest();
            digest.TryParse(System.IO.Path.GetFileName(path));
            if (digest.AvailableDigests.Any()) yield return digest;
        }
    }

    /// <inheritdoc/>
    public IEnumerable<string> ListTemp()
        => Directory.Exists(Path)
            ? Directory.GetDirectories(Path, "0install-*")
            : Enumerable.Empty<string>();

    /// <inheritdoc/>
    public void Verify(ManifestDigest manifestDigest)
    {
        try
        {
            ImplementationStoreUtils.Verify(base.GetPath(manifestDigest) ?? throw new ImplementationNotFoundException(manifestDigest), manifestDigest, _handler);
        }
        catch (DigestMismatchException ex) when (ex.ExpectedDigest != null)
        {
            Log.Info(ex.LongMessage);
            if (_handler.Ask(
                    question: string.Format(Resources.ImplementationDamaged + Environment.NewLine + Resources.ImplementationDamagedAskRemove, ex.ExpectedDigest),
                    defaultAnswer: false, alternateMessage: string.Format(Resources.ImplementationDamaged + Environment.NewLine + Resources.ImplementationDamagedBatchInformation, ex.ExpectedDigest)))
                Remove(new(ex.ExpectedDigest));
        }
    }

    /// <inheritdoc/>
    public bool Remove(ManifestDigest manifestDigest)
    {
        if (base.GetPath(manifestDigest) is not {} path) return false;
        Log.Info(string.Format(Resources.DeletingImplementation, manifestDigest));

        if (FileUtils.PathEquals(path, Locations.InstallBase))
        {
            Log.Warn(Resources.NoStoreSelfRemove);
            return false;
        }
        if (MissingAdminRights) throw new NotAdminException(Resources.MustBeAdminToRemove);
        if (BlockedByOpenFileHandles(path)) return false;

        _handler.RunTask(new SimpleTask(
            string.Format(Resources.DeletingImplementation, System.IO.Path.GetFileName(path)),
            () =>
            {
                string tempDir = System.IO.Path.Combine(Path, $"0install-remove-{System.IO.Path.GetRandomFileName()}");
                DisableWriteProtection(path);
                Directory.Move(path, tempDir);
                Directory.Delete(tempDir, recursive: true);
            }));

        return true;
    }

    private bool BlockedByOpenFileHandles(string path)
    {
        if (!WindowsUtils.IsWindowsVista) return false;

        try
        {
            using var restartManager = new WindowsRestartManager();
            restartManager.RegisterResources(FilesToCheckForOpenFileHandles(path));
            if (restartManager.ListApps(_handler.CancellationToken) is {Length: > 0} apps)
            {
                string appsList = string.Join(Environment.NewLine, apps);
                if (_handler.Ask($"{Resources.FilesInUse} {Resources.FilesInUseAskClose}{Environment.NewLine}{appsList}", defaultAnswer: _purging.Value))
                    restartManager.ShutdownApps(_handler);
                else return true;
            }
        }
        #region Error handling
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
        {
            Log.Warn(string.Format(Resources.FailedToUnlockFiles, path), ex);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Problem using Windows Restart Manager", ex);
            return false;
        }
        #endregion

        return false;
    }

    /// <summary>
    /// Prioritize EXEs over DLLs and limit total number of files to avoid slow scan.
    /// </summary>
    private static string[] FilesToCheckForOpenFileHandles(string path)
    {
        List<string> exe = new(), dll = new();
        try
        {
            FileUtils.GetFilesRecursive(path)
                     .Bucketize(System.IO.Path.GetExtension)
                     .Add(".exe", exe)
                     .Add(".dll", dll).Run();
        }
        #region Error handling
        catch (Exception ex)
        {
            Log.Error($"Problem enumerating files in '{path}'.", ex);
        }
        #endregion

        return exe.Concat(dll).Take(16).ToArray();
    }

    private static void DisableWriteProtection(string path)
    {
        try
        {
            Log.Debug($"Disabling write protection for: {path}");
            FileUtils.DisableWriteProtection(path);
        }
        #region Error handling
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Failed to disable write protection for: {path}", ex);
        }
        #endregion
    }

    /// <inheritdoc/>
    public bool RemoveTemp(string path)
    {
        #region Sanity checks
        if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
        #endregion

        if (!path.StartsWith(Path + System.IO.Path.DirectorySeparatorChar) || !Directory.Exists(path)) return false;
        if (MissingAdminRights) throw new NotAdminException(Resources.MustBeAdminToRemove);

        _handler.RunTask(new SimpleTask(
            string.Format(Resources.DeletingDirectory, path),
            () => Directory.Delete(path, recursive: true)));
        return true;
    }

    /// <summary>Used to indicate if a <see cref="Purge"/> operation is currently running.</summary>
    private readonly ThreadLocal<bool> _purging = new();

    /// <inheritdoc/>
    public void Purge()
    {
        _purging.Value = true;
        try
        {
            _handler.RunTask(ForEachTask.Create(string.Format(Resources.DeletingDirectory, Path),
                ListTemp().Concat(ListAll().Cast<object>()).ToList(),
                toRemove =>
                {
                    if (toRemove is string path) RemoveTemp(path);
                    else if (toRemove is ManifestDigest digest) Remove(digest);
                }));
        }
        finally
        {
            _purging.Value = false;
        }

        RemoveDeleteInfoFile();
    }

    /// <inheritdoc/>
    public long Optimise()
    {
        if (!Directory.Exists(Path)) return 0;
        if (MissingAdminRights) throw new NotAdminException(Resources.MustBeAdminToOptimise);

        using var run = new OptimiseRun(Path);
        _handler.RunTask(ForEachTask.Create(string.Format(Resources.FindingDuplicateFiles, Path), ListAll(), run.Work));
        return run.SavedBytes;
    }

    private bool MissingAdminRights => ReadOnly && WindowsUtils.IsWindowsNT && !WindowsUtils.IsAdministrator;

    /// <summary>
    /// Creates string representation suitable for console output.
    /// </summary>
    public override string ToString()
        => $"{Kind}: {Path}";

    /// <inheritdoc/>
    public bool Equals(ImplementationStore? other)
        => other != null
        && Path == other.Path;

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (obj == null) return false;
        if (obj == this) return true;
        return obj.GetType() == typeof(ImplementationStore) && Equals((ImplementationStore)obj);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
        => Path.GetHashCode();
}
