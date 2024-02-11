// Copyright Bastian Eicher et al.
// Licensed under the GNU Lesser Public License

using NanoByte.Common.Native;
using ZeroInstall.Services.Feeds;
using ZeroInstall.Store.Feeds;

namespace ZeroInstall.Commands.Desktop;

/// <summary>
/// Contains integration tests for <see cref="AddApp"/>.
/// </summary>
[Collection("Desktop integration")]
public class AddAppTest : CliCommandTestBase<AddApp>
{
    public AddAppTest()
        => GetMock<IFeedCache>().Setup(x => x.GetFeed(Fake.Feed1Uri)).Returns(Fake.Feed);

    private void MockCatalog(Catalog catalog)
        => GetMock<ICatalogManager>().Setup(x => x.GetCached()).Returns(catalog);

    [Fact]
    public void WithoutAlias()
    {
        if (WindowsUtils.IsWindows)
            MockCatalog(new());

        RunAndAssert(null, ExitCode.OK, Fake.Feed1Uri.ToStringRfc());
    }
}
