using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 商品を使っている改変と Unity のプロジェクトの見張り（<c>docs/spec/used-updates.md</c>・ユーザ判断 2026-11-09）。
/// 調べるのは改変に紐付けたプロジェクトと、今開いている Unity のプロジェクト。ID・場所は作り物。
/// </summary>
public class UsedItemsWatchTests
{
    private const string Used = "9907301";
    private const string Linked = @"D:\fake\linked-project";
    private const string Opened = @"D:\fake\opened-project";

    private static async Task<(MainViewModel Main, List<string> Scanned)> StartAsync(TestApp app)
    {
        await app.AddItemAsync(Make.Item(Used, "作り物の衣装"));
        await app.Store.Modifications.SaveAsync(new ModificationRecord
        {
            Id = "mod-0000aaaa",
            AvatarItemId = "9907302",
            Name = "作り物の改変",
            UnityProject = Linked,
            Members = [new ModificationMember { ItemId = Used }],
        });

        var scanned = new List<string>();
        app.Services.ProjectFolderState = _ => DiskAnswer.Present;
        app.Services.ScanUnityProject = (project, _, _, _) =>
        {
            lock (scanned)
            {
                scanned.Add(project);
            }

            return new UnityProjectScan([new UnityProjectMatch(Used, 1, 1)], new Dictionary<string, IReadOnlyList<UnityImageMatch>>());
        };
        app.OpenEditors = [new OpenUnityEditor(1, "opened", Opened)];

        var main = await app.StartAsync();
        await main.UsedItems.RequestAsync();
        return (main, scanned);
    }

    [Fact]
    public Task 改変に紐付けたプロジェクトと開いているプロジェクトを調べて数える() => TestApp.Run(async app =>
    {
        var (main, scanned) = await StartAsync(app);

        Assert.Equal(new UsedBy(1, 2), main.UsedItems.Current.Of(Used));
        Assert.Contains(Linked, scanned, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Opened, scanned, StringComparer.OrdinalIgnoreCase);
    });

    [Fact]
    public Task 窓に戻って調べ直すのは_開いているプロジェクトだけ() => TestApp.Run(async app =>
    {
        var (main, scanned) = await StartAsync(app);
        scanned.Clear();

        await main.UsedItems.RequestAsync();

        Assert.Equal([Opened], scanned.Select(path => path.TrimEnd('\\')), StringComparer.OrdinalIgnoreCase);
    });

    [Fact]
    public Task Unityを閉じたら開いていたプロジェクトは数えない() => TestApp.Run(async app =>
    {
        var (main, _) = await StartAsync(app);
        app.OpenEditors = [];

        await main.UsedItems.RequestAsync();

        Assert.Equal(new UsedBy(1, 1), main.UsedItems.Current.Of(Used));
    });
}
