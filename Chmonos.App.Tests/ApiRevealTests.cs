using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>API の「エクスプローラで開く」の場所の選び方（段4）。エクスプローラは開かず、開いた場所を控える。パスは作り物。</summary>
public sealed class ApiRevealTests
{
    private static ItemRecord ItemWith(IReadOnlyList<string> files, IReadOnlyList<string> folders, IReadOnlyList<string>? detached = null)
    {
        var item = Make.Item("9600501", "作り物の衣装");
        return item with
        {
            Local = item.Local with
            {
                LocalFiles =
                [
                    .. files.Select((path, index) => new LocalFileRecord { Hash = $"{index:x2}", Paths = [path], SizeBytes = 1 }),
                    .. (detached ?? []).Select(path => new LocalFileRecord { Hash = "ff", Paths = [path], SizeBytes = 1, Detached = true }),
                ],
                LocalFolders = folders.Select(path => new LocalFolderRecord { Path = path }).ToList(),
            },
        };
    }

    private static async Task<(string Outcome, List<string> Opened)> RevealAsync(TestApp app, ItemRecord item, string? path, bool exists = true)
    {
        var opened = new List<string>();
        var outcome = await ApiReveal.RevealAsync(item, path, app.Services.Volumes, target =>
        {
            opened.Add(target);
            return Task.FromResult(exists);
        });
        return (outcome, opened);
    }

    [Fact]
    public Task 場所が1つなら省けて_外したファイルは数えない() => TestApp.Run(async app =>
    {
        await app.StartAsync();
        var item = ItemWith([@"D:\作り物\a.zip"], [], detached: [@"D:\作り物\old.zip"]);

        var (outcome, opened) = await RevealAsync(app, item, null);

        Assert.Equal(ApiReveal.Opened, outcome);
        Assert.Equal([@"D:\作り物\a.zip"], opened);
    });

    [Fact]
    public Task 場所が複数なら指定させ_その商品の場所だけ開く() => TestApp.Run(async app =>
    {
        await app.StartAsync();
        var item = ItemWith([@"D:\作り物\a.zip"], [@"D:\作り物\衣装"]);

        Assert.Equal(ApiReveal.Ambiguous, (await RevealAsync(app, item, null)).Outcome);
        Assert.Equal(ApiReveal.UnknownPath, (await RevealAsync(app, item, @"C:\Windows")).Outcome);

        var (outcome, opened) = await RevealAsync(app, item, @"d:\作り物\衣装");
        Assert.Equal(ApiReveal.Opened, outcome);
        Assert.Equal([@"D:\作り物\衣装"], opened);
    });

    [Fact]
    public Task 場所が無い商品と_見つからない場所を言い分ける() => TestApp.Run(async app =>
    {
        await app.StartAsync();

        Assert.Equal(ApiReveal.NoLocation, (await RevealAsync(app, ItemWith([], []), null)).Outcome);
        Assert.Equal(ApiReveal.Missing, (await RevealAsync(app, ItemWith([@"E:\外付け\a.zip"], []), null, exists: false)).Outcome);
    });
}
