﻿// Copyright Bastian Eicher et al.
// Licensed under the GNU Lesser Public License

using System.Diagnostics;
using NanoByte.Common.Native;

namespace ZeroInstall.Client;

/// <summary>
/// Runs Zero Install as a child process.
/// </summary>
internal class ZeroInstallLauncher : ProcessLauncher
{
    private readonly string _mutexName, _updateMutexName;
    private readonly string _legacyMutexName, _legacyUpdateMutexName;

    public ZeroInstallLauncher(string commandLine)
        : base(ProcessUtils.FromCommandLine(commandLine))
    {
        string? installBase = Path.GetDirectoryName(FileName);
        _mutexName = ZeroInstallEnvironment.MutexName(installBase);
        _updateMutexName = ZeroInstallEnvironment.UpdateMutexName(installBase);
        _legacyMutexName = ZeroInstallEnvironment.LegacyMutexName(installBase);
        _legacyUpdateMutexName = ZeroInstallEnvironment.LegacyUpdateMutexName(installBase);
    }

    public override void Run(params string[] arguments)
    {
        using (AppMutex.Create(_mutexName))
        using (AppMutex.Create(_legacyMutexName))
            base.Run(arguments);
    }

    public override string RunAndCapture(Action<StreamWriter>? onStartup, params string[] arguments)
    {
        using (AppMutex.Create(_mutexName))
        using (AppMutex.Create(_legacyMutexName))
            return base.RunAndCapture(onStartup, arguments);
    }

    public override ProcessStartInfo GetStartInfo(params string[] arguments)
    {
        if (AppMutex.Probe(_updateMutexName) || AppMutex.Probe(_legacyUpdateMutexName))
            throw new TemporarilyUnavailableException();

        return base.GetStartInfo(arguments);
    }

    protected override void HandleExitCode(ProcessStartInfo startInfo, int exitCode, string? message = null)
    {
        if (exitCode is 0 or 1) return;

        try
        {
            base.HandleExitCode(startInfo, exitCode, message);
        }
        catch (ExitCodeException ex)
        {
            switch (ex.ExitCode)
            {
                case 10: // Web error
                    throw new WebException(ex.Message, ex);
                case 11: // Access denied
                    throw new UnauthorizedAccessException(ex.Message, ex);
                case 12: // IO error
                    throw new IOException(ex.Message, ex);
                case 15 or 20 or 21: // Conflict, solver error or executor error
                    throw new InvalidOperationException(ex.Message, ex);
                case 25 or 26 or 27: // Invalid data, digest mismatch or invalid signature
                    throw new InvalidDataException(ex.Message, ex);
                case 50: // Not supported
                    throw new NotSupportedException(ex.Message, ex);
                case 100 or -1073741510: // User canceled
                    throw new OperationCanceledException();
                case 999: // Self-update in progress
                    throw new TemporarilyUnavailableException();
                default:
                    throw;
            }
        }
    }
}
