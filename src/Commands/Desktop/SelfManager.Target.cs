// Copyright Bastian Eicher et al.
// Licensed under the GNU Lesser Public License

using System.Runtime.Versioning;
using NanoByte.Common.Native;
using ZeroInstall.Store.FileSystem;
using ZeroInstall.Store.Manifests;

namespace ZeroInstall.Commands.Desktop;

partial class SelfManager
{
    /// <summary>
    /// Loads the <see cref="Manifest"/> file in a directory.
    /// </summary>
    /// <param name="dirPath">The directory to check for a manifest file.</param>
    /// <returns>The loaded <see cref="Manifest"/>.</returns>
    private Manifest LoadManifest(string dirPath)
    {
        string manifestPath = Path.Combine(dirPath, Manifest.ManifestFile);
        if (File.Exists(manifestPath))
            return Manifest.Load(manifestPath, ManifestFormat.Sha256New);
        else if (Directory.Exists(dirPath) && Directory.GetFileSystemEntries(dirPath).Length != 0)
        {
            Log.Info($"No .manifest file found in '{dirPath}'. Assuming directory is clean.");
            var builder = new ManifestBuilder(ManifestFormat.Sha256New);
            Handler.RunTask(new ReadDirectory(dirPath, builder));
            return builder.Manifest;
        }
        else
            return new Manifest(ManifestFormat.Sha256);
    }

    /// <summary>A mutex that prevents Zero Install instances from being launched while an update is in progress.</summary>
    private AppMutex? _updateMutex;

    /// <summary>
    /// Waits for any Zero Install instances running in <see cref="TargetDir"/> to terminate and then prevents new ones from starting.
    /// </summary>
    /// <remarks>The <see cref="TargetDir"/> is encoded into an <see cref="AppMutex"/> name using <see cref="object.GetHashCode"/>.</remarks>
    private void MutexAcquire()
    {
        if (TargetDir == Locations.InstallBase)
        {
            Log.Info("Cannot use Mutex because source and target directory are the same: " + TargetDir);
            return;
        }

        if (ZeroInstallEnvironment.MutexName(TargetDir) == ZeroInstallEnvironment.MutexName(Locations.InstallBase))
        {
            Log.Warn($"Hash collision between {TargetDir} and {Locations.InstallBase}! Not using Mutex.");
            return;
        }

        if (WindowsUtils.IsWindows)
            MutexAcquireWindows();
    }

    [SupportedOSPlatform("windows")]
    private void MutexAcquireWindows()
    {
        Handler.RunTask(new SimpleTask(Resources.MutexWait, () =>
        {
            // Wait for existing instances to terminate
            while (AppMutex.Probe(ZeroInstallEnvironment.MutexName(TargetDir)))
            {
                Thread.Sleep(1000);
                Handler.CancellationToken.ThrowIfCancellationRequested();
            }

            // Prevent new instances from starting during the update
            _updateMutex = AppMutex.Create(ZeroInstallEnvironment.UpdateMutexName(TargetDir));

            // Detect any new instances that started in the short time between detecting existing ones and blocking new ones
            while (AppMutex.Probe(ZeroInstallEnvironment.MutexName(TargetDir)))
            {
                Thread.Sleep(1000);
                Handler.CancellationToken.ThrowIfCancellationRequested();
            }
        }));
    }

    /// <summary>
    /// Counterpart to <see cref="MutexAcquire"/>.
    /// </summary>
    private void MutexRelease()
    {
        if (WindowsUtils.IsWindows) _updateMutex?.Close();
    }

    /// <summary>
    /// Try to remove OneGet Bootstrap module to prevent future PowerShell sessions from loading it again.
    /// </summary>
    private void RemoveOneGetBootstrap()
    {
        if (!WindowsUtils.IsWindows) return;

        RemoveOneGetBootstrap(Path.Combine(
            Environment.GetFolderPath(MachineWide ? Environment.SpecialFolder.ProgramFiles : Environment.SpecialFolder.MyDocuments),
            "PackageManagement", "ProviderAssemblies", "0install"));
        RemoveOneGetBootstrap(Path.Combine(
            Environment.GetFolderPath(MachineWide ? Environment.SpecialFolder.ProgramFiles : Environment.SpecialFolder.LocalApplicationData),
            "WindowsPowerShell", "Modules", "0install"));
    }

    private static void RemoveOneGetBootstrap(string dirPath)
    {
        if (!WindowsUtils.IsWindows || !Directory.Exists(dirPath)) return;

        try
        {
            foreach (string subDirPath in Directory.GetDirectories(dirPath))
            {
                foreach (string filePath in Directory.GetFiles(subDirPath))
                {
                    string tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                    Log.Debug($"Trying to move '{filePath}' to '{tempPath}' to prevent future PowerShell sessions from loading it again");
                    File.Move(filePath, tempPath);
                }
                Directory.Delete(subDirPath);
            }
            Directory.Delete(dirPath);
        }
        #region Error handling
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Debug("Failed to remove OneGet bootstrap directory: " + dirPath, ex);
        }
        #endregion
    }
}
