using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 改変の右の「紐付ける先」の候補（Unity Hub・VCC の一覧）は、開いた時と「読み直す」の時だけ読む（ユーザ判断 2026-11-20）。
/// 前は使ったもの・写真・紐付けを変えるたびに読み直していた。
/// </summary>
public class ModificationProjectCandidatesReloadTests
{
    private static UnityProjectCandidate Project(TestApp app, string name) => new()
    {
        Path = System.IO.Path.Combine(app.Root, name), Name = name, Folder = app.Root, Version = "2022.3.22f1",
        Exists = true, IsOpen = false, Source = UnityProjectSource.Hub,
    };

    [Fact]
    public Task 使ったものを変えても候補は読み直さず_紐付けると今の目印だけ付け直す() => TestApp.Run(async app =>
    {
        app.UnityProjects = [Project(app, "ProjectA"), Project(app, "ProjectB")];
        var main = await app.StartAsync();
        var discovered = 0;
        var discover = app.Services.DiscoverUnityProjects;
        app.Services.DiscoverUnityProjects = () =>
        {
            Interlocked.Increment(ref discovered);
            return discover();
        };

        var created = (CommandResult.ModificationCreated)await app.Services.Commands.ExecuteAsync(new UiCommand.CreateModification("1000001", "夏の改変"));
        var modification = new ModificationViewModel(created.Record, app.Services, main, main.Thumbnails);
        await UiThread.Until(() => modification.ProjectCandidates.Count == 2, "開いた時に候補を読む");
        Assert.Equal(1, Volatile.Read(ref discovered));

        // 外から使ったものが足された（使ったものを変えるのと同じ読み直しの道）。候補は読み直さない
        await app.Services.Commands.ExecuteAsync(new UiCommand.AddModificationMember(created.Record.Id, new ModificationMember { ItemId = "1000002", AddedAt = DateTimeOffset.Now }));
        await modification.NoteChangedOutsideAsync();
        Assert.Single(modification.Members);
        Assert.Equal(1, Volatile.Read(ref discovered));

        // 紐付けると、読み直さずに今の紐付け先の目印だけ移る
        modification.LinkProjectCommand.Execute(modification.ProjectCandidates[1]);
        await UiThread.Until(() => modification.ProjectCandidates.Count == 2 && modification.ProjectCandidates[1].IsCurrent, "紐付けた先に目印が付く");
        Assert.False(modification.ProjectCandidates[0].IsCurrent);
        Assert.Equal(1, Volatile.Read(ref discovered));

        // 「読み直す」を押したときは読む
        modification.RefreshProjectsCommand.Execute(null);
        await UiThread.Until(() => Volatile.Read(ref discovered) == 2, "読み直すで候補を読む");
        await UiThread.Until(() => modification.ProjectCandidates.Count == 2 && modification.ProjectCandidates[1].IsCurrent, "読み直した後も目印は今の紐付け先");
    });
}
