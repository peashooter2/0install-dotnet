﻿// Copyright Bastian Eicher et al.
// Licensed under the GNU Lesser Public License

namespace ZeroInstall.Model;

/// <summary>
/// Provides access to Zero Install-related environment variables.
/// </summary>
public static class ZeroInstallEnvironment
{
    /// <summary>
    /// The name of the environment variable for <see cref="Cli"/>.
    /// </summary>
    public const string CliName = "ZEROINSTALL";

    /// <summary>
    /// A command-line for launching the CLI version of Zero Install.
    /// </summary>
    public static string? Cli => Environment.GetEnvironmentVariable(CliName);

    /// <summary>
    /// The name of the environment variable for <see cref="Gui"/>.
    /// </summary>
    public const string GuiName = "ZEROINSTALL_GUI";

    /// <summary>
    /// A command-line for launching the graphical version of Zero Install.
    /// </summary>
    public static string? Gui => Environment.GetEnvironmentVariable(GuiName);

    /// <summary>
    /// The name of the environment variable for <see cref="ExternalFetch"/>.
    /// </summary>
    public const string ExternalFetcherName = "ZEROINSTALL_EXTERNAL_FETCHER";

    /// <summary>
    /// A command-line that downloads a set of <see cref="Implementation"/>s piped in as XML via stdin.
    /// </summary>
    public static string? ExternalFetch => Environment.GetEnvironmentVariable(ExternalFetcherName);

    /// <summary>
    /// The name of the environment variable for <see cref="FeedUri"/>.
    /// </summary>
    public const string FeedUriName = "ZEROINSTALL_FEED_URI";

    /// <summary>
    /// The URI of the feed used to start this program.
    /// </summary>
    public static FeedUri? FeedUri
    {
        get
        {
            string? uri = Environment.GetEnvironmentVariable(FeedUriName);
            try
            {
                if (!string.IsNullOrEmpty(uri)) return new FeedUri(uri);
            }
            catch (UriFormatException)
            {}
            return null;
        }
    }
}
